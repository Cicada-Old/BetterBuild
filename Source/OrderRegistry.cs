using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace BuildMonitor
{
    /// <summary>订单物理状态：蓝图 / 部分入框 / 材料齐待施工。</summary>
    public enum OrderStage
    {
        Blueprint,
        FramePartial,
        FrameReady
    }

    /// <summary>一张建造订单：一个建筑实体（蓝图或框架），蓝图转框架时订单号延续。</summary>
    public sealed class OrderInfo
    {
        public int Id;
        public Thing Thing;
        public string Key;
        public OrderStage Stage;
        public int Rank = int.MaxValue;

        /// <summary>
        /// 实际参与调度的名次：默认等于 Rank，等待过久的订单会被「老化提权」（见 OrderRegistry.ApplyAging）。
        /// 门控与材料预算都读这个值，所以提权对两者同时生效。
        /// </summary>
        public int EffRank = int.MaxValue;

        /// <summary>每种材料的剩余需求（注册表刷新时缓存，供材料预算使用）。</summary>
        public readonly Dictionary<ThingDef, int> remaining = new Dictionary<ThingDef, int>();
    }

    /// <summary>
    /// 跨存读档的订单账本：框架承接的订单号映射（蓝图转框架时写入）。
    /// 转换是同步过程，carryingBlueprintId 只在转换期间有效。
    /// </summary>
    public class OrderLedger : GameComponent
    {
        public static Dictionary<int, int> frameToOrder = new Dictionary<int, int>();
        public static readonly HashSet<int> converting = new HashSet<int>();

        public OrderLedger(Game game)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref frameToOrder, "BuildOrderFrameToOrder", LookMode.Value);
            if (frameToOrder == null) frameToOrder = new Dictionary<int, int>();
        }

        public override void LoadedGame()
        {
            converting.Clear();
        }

        public static void EnsureInstance()
        {
            Game g = Current.Game;
            if (g != null) g.GetComponent<OrderLedger>();
        }
    }

    /// <summary>
    /// 每张地图一份的订单注册表。生成/销毁走事件即时增删，
    /// 材料入框与完工进度每 60 tick 用原版分组列表轻量刷新，
    /// 闸门预演、材料预留、未建造统计都读这一份数据。
    /// </summary>
    public class OrderRegistry : MapComponent
    {
        public const int RefreshInterval = 60;
        public const long RankRetentionTicks = 1800000L; // 名次保留 30 游戏日

        public readonly Dictionary<int, OrderInfo> orders = new Dictionary<int, OrderInfo>();
        public readonly List<OrderInfo> byRank = new List<OrderInfo>();
        private int lastRefresh = -1000000;

        public OrderRegistry(Map map) : base(map)
        {
        }

        public static OrderRegistry Of(Map map)
        {
            if (map == null) return null;
            return map.GetComponent<OrderRegistry>();
        }

        public void RegisterOrder(int id, Thing thing)
        {
            if (id <= 0 || thing == null) return;
            OrderInfo info;
            if (!orders.TryGetValue(id, out info))
            {
                info = new OrderInfo { Id = id };
                orders[id] = info;
            }
            info.Thing = thing;
            info.Key = BuildUtil.GroupKey(thing);
            info.Stage = StageOf(thing);
            ForceRefresh();
        }

        public void DropOrder(int id)
        {
            if (id <= 0) return;
            if (orders.Remove(id)) ForceRefresh();
        }

        private void ForceRefresh()
        {
            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            lastRefresh = now - RefreshInterval - 1;
        }

        public void EnsureFresh()
        {
            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            if (now - lastRefresh < RefreshInterval) return;
            lastRefresh = now;
            Rebuild(now);
        }

        private void Rebuild(int now)
        {
            Map m = map;
            OrderLedger.EnsureInstance();
            orders.Clear();
            if (m != null)
            {
                ScanGroup(m.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint));
                ScanGroup(m.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame));
            }

            BuildOrderComp comp = BuildOrderComp.Instance;
            if (comp != null && comp.order != null)
            {
                HashSet<string> present = new HashSet<string>();
                foreach (OrderInfo info in orders.Values)
                {
                    if (info.Key == null) continue;
                    present.Add(info.Key);
                    if (!comp.order.Contains(info.Key))
                    {
                        comp.order.Add(info.Key);   // 新类型自动追加到末尾
                        BuildOrderComp.Version++;
                    }
                }
                for (int i = comp.order.Count - 1; i >= 0; i--)
                {
                    string key = comp.order[i];
                    if (present.Contains(key)) continue;
                    int seen;
                    if (comp.seenTicks.TryGetValue(key, out seen) && now - seen > RankRetentionTicks)
                    {
                        comp.order.RemoveAt(i);     // 连续 30 游戏日无待建，清理名次
                        BuildOrderComp.Version++;
                    }
                }
                foreach (string key in present) comp.seenTicks[key] = now;
            }

            byRank.Clear();
            int minRank = int.MaxValue;
            foreach (OrderInfo info in orders.Values)
            {
                info.Rank = comp != null ? comp.Rank(info.Key) : int.MaxValue;
                byRank.Add(info);
                if (info.Rank < minRank) minRank = info.Rank;
            }
            ApplyAging(now, comp, minRank);
            byRank.Sort(delegate(OrderInfo a, OrderInfo b)
            {
                int c = a.EffRank.CompareTo(b.EffRank);
                return c != 0 ? c : a.Id.CompareTo(b.Id);
            });
        }

        // 订单等待计时：上次的需求总量、以及"最近一次有进展"的 tick
        private readonly Dictionary<int, int> waitLastTotal = new Dictionary<int, int>();
        private readonly Dictionary<int, int> waitProgressTick = new Dictionary<int, int>();

        /// <summary>
        /// 老化提权：订单连续等待（材料没减少 = 没进料）超过设定时长后，
        /// 把它的调度名次提到「最高名次之后一位」，让它能排到队首那一批里。
        ///
        /// 为什么必须有这一条：材料预算按名次顺序分配额度，只要队列总需求大于供给，
        /// 队尾就永远轮不到（第三阶段实测已量化，见设计文档 5.2）。保护额度只是杯水车薪。
        ///
        /// 为什么只提到「最高名次 + 1」而不是最高名次：最高名次组是用户定义的真实优先级，
        /// 老化不该把真·最高优先级挤下去——只让久等的订单越过它后面所有的队。
        /// </summary>
        private void ApplyAging(int now, BuildOrderComp comp, int minRank)
        {
            int ageTicks = comp != null ? comp.AgeTicks : 0;
            int promote = minRank == int.MaxValue ? int.MaxValue : minRank + 1;

            for (int i = 0; i < byRank.Count; i++)
            {
                OrderInfo info = byRank[i];
                info.EffRank = info.Rank;

                int total = 0;
                foreach (KeyValuePair<ThingDef, int> kv in info.remaining) total += kv.Value;

                int prev;
                bool known = waitLastTotal.TryGetValue(info.Id, out prev);
                // 首次见到、材料变少（= 有进料）、或已经没有材料需求 → 计时归零
                if (!known || total < prev || total == 0) waitProgressTick[info.Id] = now;
                waitLastTotal[info.Id] = total;

                if (ageTicks <= 0 || total <= 0) continue;
                int since;
                if (!waitProgressTick.TryGetValue(info.Id, out since)) since = now;
                if (now - since < ageTicks) continue;
                if (promote < info.EffRank) info.EffRank = promote;
            }

            // 订单消失后清掉计时，避免字典随存档长期增长
            if (waitLastTotal.Count > byRank.Count + 64)
            {
                List<int> stale = null;
                foreach (int id in waitLastTotal.Keys)
                {
                    if (orders.ContainsKey(id)) continue;
                    if (stale == null) stale = new List<int>();
                    stale.Add(id);
                }
                if (stale != null)
                {
                    for (int i = 0; i < stale.Count; i++)
                    {
                        waitLastTotal.Remove(stale[i]);
                        waitProgressTick.Remove(stale[i]);
                    }
                }
            }
        }

        /// <summary>该订单连续多久没有进料（tick）；没有记录时返回 0。供诊断界面显示「卡了多久」。</summary>
        public int WaitTicksOf(int orderId, int now)
        {
            int since;
            if (!waitProgressTick.TryGetValue(orderId, out since)) return 0;
            return now > since ? now - since : 0;
        }

        private void ScanGroup(List<Thing> things)
        {
            if (things == null) return;
            for (int i = 0; i < things.Count; i++)
            {
                Thing t = things[i];
                if (t == null || t.Destroyed || !t.Spawned) continue;
                if (t.Faction != Faction.OfPlayer) continue;
                int id = t.thingIDNumber;
                if (t is Frame && OrderLedger.frameToOrder.TryGetValue(id, out int carried)) id = carried;
                OrderInfo info = new OrderInfo
                {
                    Id = id,
                    Thing = t,
                    Key = BuildUtil.GroupKey(t),
                    Stage = StageOf(t)
                };
                CacheRemaining(info, t);
                orders[id] = info;
            }
        }

        /// <summary>缓存每订单每种材料的剩余需求（每 60 tick 重算一次，预算直接读取）。</summary>
        private static void CacheRemaining(OrderInfo info, Thing t)
        {
            info.remaining.Clear();
            if (t is Blueprint_Install) return;
            IConstructible constructible = t as IConstructible;
            if (constructible == null) return;
            List<ThingDefCountClass> costs = constructible.TotalMaterialCost();
            if (costs == null) return;
            for (int i = 0; i < costs.Count; i++)
            {
                ThingDefCountClass cost = costs[i];
                if (cost == null || cost.thingDef == null) continue;
                int need = constructible.ThingCountNeeded(cost.thingDef);
                if (need > 0) info.remaining[cost.thingDef] = need;
            }
        }

        public static OrderStage StageOf(Thing t)
        {
            Frame frame = t as Frame;
            if (frame != null)
            {
                return frame.IsCompleted() ? OrderStage.FrameReady : OrderStage.FramePartial;
            }
            return OrderStage.Blueprint;
        }
    }

    // ---------------- 生命周期钩子 ----------------

    /// <summary>蓝图转框架：订单号延续。</summary>
    [HarmonyPatch(typeof(Blueprint), nameof(Blueprint.TryReplaceWithSolidThing))]
    public static class Patch_BpToFrameCarry
    {
        private static void Prefix(Blueprint __instance)
        {
            OrderLedger.converting.Add(__instance.thingIDNumber);
        }

        private static void Postfix(Blueprint __instance, Thing createdThing, bool __result)
        {
            OrderLedger.converting.Remove(__instance.thingIDNumber);
            if (!__result || !(createdThing is Frame frame)) return;
            OrderLedger.frameToOrder[frame.thingIDNumber] = __instance.thingIDNumber;
            OrderRegistry reg = OrderRegistry.Of(frame.Map);
            if (reg != null) reg.RegisterOrder(__instance.thingIDNumber, frame);
        }
    }

    [HarmonyPatch(typeof(Blueprint), nameof(Blueprint.SpawnSetup))]
    public static class Patch_OrderBlueprintSpawn
    {
        private static void Postfix(Blueprint __instance)
        {
            OrderRegistry reg = OrderRegistry.Of(__instance.Map);
            if (reg != null) reg.RegisterOrder(__instance.thingIDNumber, __instance);
        }
    }

    [HarmonyPatch(typeof(Blueprint), nameof(Blueprint.DeSpawn))]
    public static class Patch_OrderBlueprintDespawn
    {
        private static void Prefix(Blueprint __instance, out Map __state)
        {
            __state = __instance.Map;
        }

        private static void Postfix(Blueprint __instance, Map __state)
        {
            if (OrderLedger.converting.Contains(__instance.thingIDNumber)) return;
            OrderRegistry reg = OrderRegistry.Of(__state);
            if (reg != null) reg.DropOrder(__instance.thingIDNumber);
        }
    }

    /// <summary>框架建成：订单结束并清理承接映射。取消/拆除等其余消失路径由 60 tick 刷新兜底。</summary>
    [HarmonyPatch(typeof(Frame), nameof(Frame.CompleteConstruction))]
    public static class Patch_OrderFrameComplete
    {
        private static void Prefix(Frame __instance)
        {
            int id = __instance.thingIDNumber;
            if (!OrderLedger.frameToOrder.TryGetValue(__instance.thingIDNumber, out int carried)) carried = id;
            OrderLedger.frameToOrder.Remove(__instance.thingIDNumber);
            OrderRegistry reg = OrderRegistry.Of(__instance.Map);
            if (reg != null) reg.DropOrder(carried);
        }
    }
}
