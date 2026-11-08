using System.Collections.Generic;
using RimWorld;
using Verse;

namespace BuildMonitor
{
    /// <summary>一张订单当前的调度结论。</summary>
    public enum VerdictCode
    {
        Granted,          // 放行：判定允许执行
        WaitingMaterial,  // 等料：预算不给额度
        Yielded,          // 让路：存在更高名次且他能做的单
        NoWorker,         // 无人可做：权限、等级、白名单三层里有任一层把全部殖民者挡在外面
        Forbidden         // 禁建：玩家在游戏里禁用了这张蓝图/框架
    }

    /// <summary>结论是谁判的。界面据它决定这条信息有多"权威"。</summary>
    public enum JudgeKind
    {
        None,
        Budget,     // 材料预算（每 15 tick 重建，覆盖全部订单）
        Gate,       // 建造让路闸门（只在有人扫到该订单时判）
        NoWorker,   // 无人可做（每 600 tick 独立算一次）
        Player      // 玩家禁用
    }

    /// <summary>
    /// 一张订单最近一次的调度结论。对象在字典里复用，字段覆盖写，
    /// 避免每 15 tick 给几百张订单分配新对象。
    /// </summary>
    public sealed class OrderVerdict
    {
        public VerdictCode code;
        public JudgeKind judge;

        // WaitingMaterial 的明细：卡住的材料与算式各项
        public ThingDef def;
        public int need, avail, stock, enroute, protect, alloc;
        public bool permanent;   // 永缺（不是这一时抢空）

        // Yielded 的明细：让给谁
        public int byRank = -1;
        public Thing byThing;

        // Granted 的显示明细：这张单当前有没有执行者（原版预约状态）。
        // 只影响诊断页「原因」列的措辞，不参与任何判定。默认 true = 不提示「无人被派」，
        // 等 600 tick 的采样实测后再定——宁可少说一句，也不要在没实测过时说「没人被派」。
        public bool reserved = true;

        public int tick;    // 最近一次判定时刻
        public int since;   // 本结论从哪个 tick 起就没再变过

        // 日志去重：上次为这条订单输出的是哪个结论、什么时候
        public int loggedCode = -1;
        public int loggedTick = -1000000;

        public void Begin(VerdictCode c, JudgeKind j, int now)
        {
            if (code != c || judge != j) since = now;
            code = c;
            judge = j;
            tick = now;
            def = null;
            need = avail = stock = enroute = protect = alloc = 0;
            permanent = false;
            byRank = -1;
            byThing = null;
            reserved = true;    // 结论变了就先回到「不提示无人被派」，等下一次采样实测
        }
    }

    /// <summary>
    /// 统一调度诊断：把「预算 / 让路 / 无人可做 / 禁建」四处判定收敛成每订单一条结论，
    /// 界面与结构化日志都只读这一份，避免各自扫描原始对象（见框架蓝图第二节末条）。
    ///
    /// 只做观测，不参与任何判定：这里写什么都不会反过来改变放行结果。
    /// </summary>
    public static class SchedulingDiagnostics
    {
        /// <summary>「无人可做」的重算间隔。它要遍历殖民者算权限/等级，不进热路径。</summary>
        public const int NoWorkerInterval = 600;

        /// <summary>
        /// 让路结论的保留时长。预算每 15 tick 就会覆盖一次结论，
        /// 不保留的话「让路」几乎不可能被界面（120 tick 刷新）或日志抓到。
        /// </summary>
        public const int YieldHoldTicks = 240;

        private static readonly Dictionary<int, OrderVerdict> verdicts = new Dictionary<int, OrderVerdict>();
        private static readonly HashSet<string> seenKeys = new HashSet<string>();
        private static int lastNoWorkerTick = -1000000;

        /// <summary>订单号：框架沿用蓝图的订单号，其余用实体身份号。</summary>
        public static int OrderIdOf(Thing t)
        {
            if (t == null) return 0;
            if (t is Frame frame && OrderLedger.frameToOrder.TryGetValue(frame.thingIDNumber, out int carried))
            {
                return carried;
            }
            return t.thingIDNumber;
        }

        public static bool TryGet(int orderId, out OrderVerdict verdict)
        {
            return verdicts.TryGetValue(orderId, out verdict);
        }

        public static OrderVerdict Of(int orderId)
        {
            OrderVerdict v;
            if (!verdicts.TryGetValue(orderId, out v))
            {
                v = new OrderVerdict();
                verdicts[orderId] = v;
            }
            return v;
        }

        /// <summary>预算重建时的结论（每 15 tick 覆盖一次，是唯一覆盖全部订单的来源）。</summary>
        public static void RecordBudget(int orderId, VerdictCode code, ThingDef def, int need, int avail,
            int stock, int enroute, int protect, int alloc, bool permanent, int now)
        {
            if (orderId <= 0) return;
            OrderVerdict v = Of(orderId);

            // 「无人可做」更根本（有料也没人搬），且在它的重算周期内不该被预算刷掉
            if (v.judge == JudgeKind.NoWorker && now - v.tick < NoWorkerInterval) return;
            // 让路结论短暂保留，否则预算每 15 tick 一次会把它瞬间抹掉
            if (v.judge == JudgeKind.Gate && now - v.tick < YieldHoldTicks) return;

            v.Begin(code, JudgeKind.Budget, now);
            if (code == VerdictCode.WaitingMaterial)
            {
                v.def = def;
                v.need = need;
                v.avail = avail;
                v.stock = stock;
                v.enroute = enroute;
                v.protect = protect;
                v.alloc = alloc;
                v.permanent = permanent;
            }
        }

        /// <summary>让路闸门的结论。只在当前是「放行」时记——等料/无人可做是更靠前的原因。</summary>
        public static void RecordYield(int orderId, int byRank, Thing byThing, int now)
        {
            if (orderId <= 0) return;
            OrderVerdict v = Of(orderId);
            if (v.code == VerdictCode.WaitingMaterial || v.code == VerdictCode.NoWorker
                || v.code == VerdictCode.Forbidden) return;
            v.Begin(VerdictCode.Yielded, JudgeKind.Gate, now);
            v.byRank = byRank;
            v.byThing = byThing;
        }

        public static void RecordForbidden(int orderId, int now)
        {
            if (orderId <= 0) return;
            Of(orderId).Begin(VerdictCode.Forbidden, JudgeKind.Player, now);
        }

        public static void RecordNoWorker(int orderId, int now)
        {
            if (orderId <= 0) return;
            Of(orderId).Begin(VerdictCode.NoWorker, JudgeKind.NoWorker, now);
        }

        /// <summary>
        /// 放行时补一条显示细节：这张单当前有没有执行者（原版预约状态，T-076）。
        /// 只补措辞、不改结论：等料/让路/禁建/无人可做都是更靠前的原因，一律不覆盖；
        /// 也没被任何判定读过的条目不凭空建（预算还没扫到它时，界面本来就不显示结论）。
        /// </summary>
        public static void RecordGranted(int orderId, bool reservedNow, int now)
        {
            if (orderId <= 0) return;
            OrderVerdict v;
            if (!verdicts.TryGetValue(orderId, out v)) return;
            if (v.code != VerdictCode.Granted) return;
            v.reserved = reservedNow;
            v.tick = now;
        }

        /// <summary>
        /// 重算「无人可做」：按类型组抽样一张订单（权限、等级、白名单都是类型级的），
        /// 若所有能工作的殖民者都过不了「权限 AND 等级 AND 白名单」三层中的任一层，就标成 NoWorker（CR-152 对齐完工闸门）。
        /// 这一类失败当前没有任何日志，是唯一的完全盲区。
        /// </summary>
        public static void RefreshNoWorker(Map map, OrderRegistry registry, int now)
        {
            if (map == null || registry == null) return;
            // now 比上次小 = 读了一张更早的档，视为已过期（T-056 同一根因）
            if (now >= lastNoWorkerTick && now - lastNoWorkerTick < NoWorkerInterval) return;
            lastNoWorkerTick = now;

            List<Pawn> colonists = map.mapPawns != null ? map.mapPawns.FreeColonistsSpawned : null;
            if (colonists == null || colonists.Count == 0) return;

            seenKeys.Clear();
            foreach (OrderInfo info in registry.orders.Values)
            {
                Thing t = info.Thing;
                if (t == null || t.Destroyed || !t.Spawned) continue;
                // 安装蓝图参与抽样（CR-161）：它没有材料清单，但 T-067 之后要过 ShouldBlockInstall
                // （权限 + 白名单，**不查等级**，T-071），与普通蓝图同一把键（TypeKey 走 EntityOf → 建筑 def）。
                // 原先的 `if (t is Blueprint_Install) continue;` 是「怕 TotalMaterialCost 刷 Error」留下的，
                // 而抽样循环根本不读材料，那个理由已不成立；留着会让「只剩安装蓝图」的组漏报无人可做。
                if (info.Key == null || !seenKeys.Add(info.Key)) continue;
                if (t.IsForbidden(Faction.OfPlayer)) continue;          // 禁建另有结论

                bool any = false;
                for (int i = 0; i < colonists.Count; i++)
                {
                    Pawn p = colonists[i];
                    if (p == null || p.Dead || p.Downed) continue;
                    if (!BuildPermsComp.Allowed(p, t)) continue;
                    // 安装蓝图不查等级（T-071）：原版给安装蓝图的 constructionSkillPrerequisite 恒为 0
                    // （ThingDefGenerator_Buildings.cs L103-108），装了等级判定就会出现「诊断说无人可做、
                    // 实际任何等级都能装」——那是诊断在说谎。闸门侧同口径：ShouldBlockInstall 也不查等级。
                    if (!(t is Blueprint_Install) && !BuildSkillRangeComp.Allows(p, t)) continue;
                    // 白名单对建造与安装都生效，所以这一层不区分蓝图种类
                    // （与完工闸门 ShouldBlockFinish / 安装闸门 ShouldBlockInstall 同一套口径，CR-152）
                    if (!BuildWorkerComp.Allows(p, t)) continue;
                    any = true;
                    break;
                }
                if (!any) RecordNoWorker(info.Id, now);
                // 三层都过得去 → 这张单「可做」。但可做不等于已派人：原版有没有把活派下去，
                // 看它被预约没有（派人时先 Reserve，这是最早能观测到的信号，一次字典查询就够）。
                else RecordGranted(info.Id, map.reservationManager.IsReserved(t), now);
            }
        }

        /// <summary>订单消失后清理结论，避免字典随存档无限增长。</summary>
        public static void Prune(OrderRegistry registry)
        {
            if (registry == null || verdicts.Count <= registry.orders.Count + 64) return;
            List<int> stale = null;
            foreach (int id in verdicts.Keys)
            {
                if (registry.orders.ContainsKey(id)) continue;
                if (stale == null) stale = new List<int>();
                stale.Add(id);
            }
            if (stale == null) return;
            for (int i = 0; i < stale.Count; i++) verdicts.Remove(stale[i]);
        }

        /// <summary>
        /// 收集「结论变化且已过限频」的订单号，并记下本次已输出，供结构化日志使用。
        /// 变化指的是结论码变了（放行↔等料↔让路↔无人可做↔禁建），等料换一种材料不算。
        /// </summary>
        public static void CollectChanges(OrderRegistry registry, int now, List<int> outIds)
        {
            if (registry == null || outIds == null) return;
            foreach (OrderInfo info in registry.orders.Values)
            {
                OrderVerdict v;
                if (!verdicts.TryGetValue(info.Id, out v)) continue;
                int code = (int)v.code;
                if (v.loggedCode >= 0 && v.loggedCode == code) continue;
                if (now - v.loggedTick < 900) continue;
                v.loggedCode = code;
                v.loggedTick = now;
                outIds.Add(info.Id);
            }
        }

        /// <summary>按结论计数，供界面汇总行使用。</summary>
        public static void Count(OrderRegistry registry, int[] counts)
        {
            if (counts == null) return;
            for (int i = 0; i < counts.Length; i++) counts[i] = 0;
            if (registry == null) return;
            foreach (OrderInfo info in registry.orders.Values)
            {
                OrderVerdict v;
                if (!verdicts.TryGetValue(info.Id, out v)) continue;
                int idx = (int)v.code;
                if (idx >= 0 && idx < counts.Length) counts[idx]++;
            }
        }
    }
}
