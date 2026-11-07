using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;
using Verse.AI;

namespace BuildMonitor
{
    /// <summary>
    /// 材料预算管理器（每张地图，周期性重建）：
    /// - 在途账本：直接读原版 <c>map.enrouteManager</c>（蓝图/框架都实现 IHaulEnroute，
    ///   原版送料任务会按 targetC → targetB → targetQueueB 逐个容器分摊记账，多堆多送不漏算）；
    ///   「在手上」的那部分已离开储存区、不再计入库存，因此只从库存占用里扣掉它；
    /// - 预算：每种材料先保护最高名次组的剩余需求，剩余额度按（名次, 订单号）
    ///   依次分配给低名次订单；订单必须同时拿到全部所需材料的额度才放行；
    ///   最高名次组内部自由竞争，不受额度限制；
    /// - 名次读的是订单的 EffRank（含「等待老化」提权），所以久等的订单会排到队首那一批，
    ///   否则队列一长队尾永远轮不到（见设计文档 5.2）；
    /// - 纯算术软约束：不占游戏预约、不扣库存，只决定送料任务是否生成。
    /// </summary>
    public static class MaterialBudget
    {
        /// <summary>重建间隔：1 = 每 tick；15 = 每 15 tick（对比测试用）。</summary>
        public const int RebuildInterval = 15;

        // 仪表：两次概览之间的调用/节流/重建次数，以及有多少个不同 tick 触发过调用
        private static int calls;
        private static int throttled;
        private static int rebuilds;
        private static int callTicks;
        private static int lastCallTick = -1;

        private static Map budgetMap;
        private static int budgetTick = -1000000;
        private static int budgetVersion = -1;

        // 原版账本汇总：enroute 为认领总量，carried 为其中已在手上（已离开储存区）的部分
        private static readonly Dictionary<ThingDef, int> enrouteByDef = new Dictionary<ThingDef, int>();
        private static readonly Dictionary<ThingDef, int> carriedByDef = new Dictionary<ThingDef, int>();
        private static int enrouteTotal;
        private static int carriedTotal;

        // 按订单的放行结果（业务判定用）；诊断明细统一在 SchedulingDiagnostics
        private static readonly Dictionary<int, bool> granted = new Dictionary<int, bool>();
        private static int lastOverviewTick = -1000000;

        // 结构化日志缓冲：复用集合，避免每次输出都分配
        private const int EventLimit = 120;
        private static readonly List<int> changedIds = new List<int>();
        private static readonly Dictionary<string, int> groupSample = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> groupCount = new Dictionary<string, int>();
        private static readonly List<string> groupKeys = new List<string>();

        private static readonly Dictionary<long, int> needOnOrder = new Dictionary<long, int>();
        private static readonly Dictionary<ThingDef, int> topRankOf = new Dictionary<ThingDef, int>();
        private static readonly Dictionary<ThingDef, int> topReserved = new Dictionary<ThingDef, int>();
        private static readonly Dictionary<ThingDef, int> committedTotal = new Dictionary<ThingDef, int>();
        private static readonly Dictionary<ThingDef, int> allocatedSoFar = new Dictionary<ThingDef, int>();
        private static readonly Dictionary<ThingDef, int> demandByDef = new Dictionary<ThingDef, int>();

        // 有限期保护：计时只看「这种材料还有没有订单需求」，一直有需求就连续计；
        // 超过设置的保护期后该材料的保护额度只保留一半，另一半开放给低名次借用。
        private static readonly Dictionary<ThingDef, int> protectSince = new Dictionary<ThingDef, int>();
        private static readonly HashSet<ThingDef> expiryLogged = new HashSet<ThingDef>();

        // 仪表：保护统计
        private static int protKinds;
        private static int expiredKinds;
        private static int permanentKinds;
        private static int agedCount;      // 本单被「等待老化」提权的订单数
        private static int protectTicks;   // 每次重建取一次设置，避免逐单取组件

        private static int diagLogs;

        private static long KeyOf(int orderId, ThingDef def)
        {
            return ((long)orderId << 32) | (uint)def.shortHash;
        }

        public static void EnsureBudget(Map map)
        {
            if (map == null) return;
            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            calls++;
            if (now != lastCallTick)
            {
                lastCallTick = now;
                callTicks++;
            }
            if (budgetMap == map && budgetVersion == BuildOrderComp.Version
                && now >= budgetTick && now - budgetTick < RebuildInterval)
            {
                throttled++;
                return;
            }
            if (budgetMap != map)
            {
                // 换地图/读档后计时不跨图沿用，避免拿旧的 tick 乱算
                protectSince.Clear();
                expiryLogged.Clear();
                // 诊断节流同样是进程级静态：新档的 tick 可能低于上一轮，
                // 不复位就会让概览与 EVT 被静默到 tick 追平为止（T-056）
                lastOverviewTick = -1000000;
                diagLogs = 0;
            }
            budgetMap = map;
            budgetTick = now;
            budgetVersion = BuildOrderComp.Version;
            rebuilds++;
            Rebuild(map);
        }

        /// <summary>订单是否获得送料额度（注册表缺失/无材料清单时按放行，交给原版判定）。</summary>
        public static bool Granted(Map map, Thing target)
        {
            if (map == null || target == null) return true;
            if (target is Blueprint_Install) return true;
            EnsureBudget(map);
            int id = OrderIdOf(target);
            bool g;
            return !granted.TryGetValue(id, out g) || g;
        }

        public static bool ShouldWaitForMaterials(Map map, Thing target)
        {
            if (target is Blueprint_Install) return false;
            return !Granted(map, target);
        }

        private static int OrderIdOf(Thing t)
        {
            if (t is Frame frame && OrderLedger.frameToOrder.TryGetValue(frame.thingIDNumber, out int carried))
            {
                return carried;
            }
            return t.thingIDNumber;
        }

        private static void Rebuild(Map map)
        {
            granted.Clear();
            needOnOrder.Clear();
            topRankOf.Clear();
            topReserved.Clear();
            committedTotal.Clear();
            allocatedSoFar.Clear();
            demandByDef.Clear();
            enrouteByDef.Clear();
            carriedByDef.Clear();
            enrouteTotal = 0;
            carriedTotal = 0;

            OrderRegistry registry = OrderRegistry.Of(map);
            if (registry == null) return;
            registry.EnsureFresh();

            agedCount = 0;
            foreach (OrderInfo info in registry.orders.Values)
            {
                if (info.EffRank < info.Rank) agedCount++;
            }

            ScanCarried(map);

            Allocate(registry, map);

            // 诊断：预算覆盖全部订单，是「无人可做」之外唯一的全量结论来源
            int diagNow = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            SchedulingDiagnostics.RefreshNoWorker(map, registry, diagNow);
            SchedulingDiagnostics.Prune(registry);

            LogChanges(map);
        }

        /// <summary>
        /// 扫「在手上」的送料：只统计小人手上正拿着、且目的地是蓝图/框架的物品。
        /// 这部分已离开储存区（resourceCounter 只算储存区内的物品），
        /// 从原版在途里扣掉后才是真正占用库存的量。总在途以原版账本为准，这里只做拆分。
        /// </summary>
        private static void ScanCarried(Map map)
        {
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int p = 0; p < pawns.Count; p++)
            {
                Pawn pawn = pawns[p];
                if (pawn == null || pawn.carryTracker == null) continue;
                Thing held = pawn.carryTracker.CarriedThing;
                if (held == null || held.def == null) continue;

                Job job = pawn.CurJob;
                if (job == null || job.def != JobDefOf.HaulToContainer) continue;
                Thing dest = job.GetTarget(TargetIndex.B).Thing;
                if (dest == null) dest = job.GetTarget(TargetIndex.C).Thing;
                if (!(dest is Blueprint) && !(dest is Frame)) continue;

                int count = held.stackCount;
                if (count <= 0) continue;
                int prev;
                carriedByDef[held.def] = (carriedByDef.TryGetValue(held.def, out prev) ? prev : 0) + count;
                carriedTotal += count;
            }
        }

        /// <summary>原版账本里该订单该材料的认领总量（含已取货与仅预约未取货）。</summary>
        private static int EnrouteOf(Map map, Thing t, ThingDef def)
        {
            if (map == null || map.enrouteManager == null) return 0;
            if (!(t is IHaulEnroute enroute)) return 0;
            int count = map.enrouteManager.GetEnroute(enroute, def);
            return count > 0 ? count : 0;
        }

        private static void Allocate(OrderRegistry registry, Map map)
        {
            List<OrderInfo> all = registry.byRank;

            // 第一遍：每订单每种材料的有效需求（剩余 − 原版在途），并记下每种材料的最高名次
            for (int i = 0; i < all.Count; i++)
            {
                OrderInfo info = all[i];
                Thing t = info.Thing;
                if (t == null || t.Destroyed || !t.Spawned) continue;
                if (t is Blueprint_Install) continue;
                if (t.IsForbidden(Faction.OfPlayer)) continue;

                foreach (KeyValuePair<ThingDef, int> kv in info.remaining)
                {
                    // 总需求（不分名次），用于区分永缺与暂缺
                    int prevDemand;
                    demandByDef[kv.Key] = (demandByDef.TryGetValue(kv.Key, out prevDemand) ? prevDemand : 0) + kv.Value;

                    int enroute = EnrouteOf(map, t, kv.Key);
                    if (enroute > 0)
                    {
                        int prev;
                        enrouteByDef[kv.Key] = (enrouteByDef.TryGetValue(kv.Key, out prev) ? prev : 0) + enroute;
                        enrouteTotal += enroute;
                    }

                    int need = kv.Value - enroute;
                    if (need <= 0) continue;

                    needOnOrder[KeyOf(info.Id, kv.Key)] = need;

                    int top;
                    if (!topRankOf.TryGetValue(kv.Key, out top) || info.EffRank < top)
                    {
                        topRankOf[kv.Key] = info.EffRank;
                    }
                }
            }

            // 在途里仍在库存中的部分（在途 − 在手）才真正占用库存
            foreach (KeyValuePair<ThingDef, int> kv in enrouteByDef)
            {
                int carried;
                carriedByDef.TryGetValue(kv.Key, out carried);
                committedTotal[kv.Key] = kv.Value > carried ? kv.Value - carried : 0;
            }

            // 第二遍：每种材料的最高名次组预留 = 该组全部订单的有效需求之和
            for (int i = 0; i < all.Count; i++)
            {
                OrderInfo info = all[i];
                foreach (KeyValuePair<ThingDef, int> kv in topRankOf)
                {
                    if (info.EffRank != kv.Value) continue;
                    int need;
                    if (needOnOrder.TryGetValue(KeyOf(info.Id, kv.Key), out need))
                    {
                        int sum;
                        topReserved.TryGetValue(kv.Key, out sum);
                        topReserved[kv.Key] = sum + need;
                    }
                }
            }

            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            UpdateProtectionState(map, now);

            // 第三遍：低名次订单按（名次, 订单号）依次申请，整单原子放行
            for (int i = 0; i < all.Count; i++)
            {
                OrderInfo info = all[i];
                Thing t = info.Thing;
                if (t == null || t.Destroyed || !t.Spawned) continue;
                if (t is Blueprint_Install)
                {
                    // 重新安装没有材料清单，不受额度限制
                    granted[info.Id] = true;
                    SchedulingDiagnostics.RecordBudget(info.Id, VerdictCode.Granted, null, 0, 0, 0, 0, 0, 0, false, now);
                    continue;
                }
                if (t.IsForbidden(Faction.OfPlayer))
                {
                    SchedulingDiagnostics.RecordForbidden(info.Id, now);
                    continue;
                }

                bool free = true;       // 所有材料自己都是最高名次组 → 不受额度限制
                bool hasNeed = false;
                foreach (KeyValuePair<ThingDef, int> kv in info.remaining)
                {
                    int need;
                    if (!needOnOrder.TryGetValue(KeyOf(info.Id, kv.Key), out need) || need <= 0) continue;
                    hasNeed = true;
                    int topRank;
                    if (!topRankOf.TryGetValue(kv.Key, out topRank) || info.EffRank > topRank) free = false;
                }
                if (!hasNeed || free)
                {
                    granted[info.Id] = true;
                    SchedulingDiagnostics.RecordBudget(info.Id, VerdictCode.Granted, null, 0, 0, 0, 0, 0, 0, false, now);
                    continue;
                }

                bool ok = true;
                foreach (KeyValuePair<ThingDef, int> kv in info.remaining)
                {
                    int need;
                    if (!needOnOrder.TryGetValue(KeyOf(info.Id, kv.Key), out need) || need <= 0) continue;
                    int topRank;
                    if (!topRankOf.TryGetValue(kv.Key, out topRank) || info.EffRank <= topRank) continue;

                    int stock = map.resourceCounter != null ? map.resourceCounter.GetCount(kv.Key) : 0;
                    int cTotal;
                    committedTotal.TryGetValue(kv.Key, out cTotal);
                    bool expired;
                    int prot = EffectiveProtection(kv.Key, now, out expired);
                    // 预留不能超过「库存 − 在途占用」：否则需求 > 库存时 avail 被算成负数，
                    // 「差多少」会被放大成 reserve 的量级，界面读起来像天量缺口
                    int protCap = stock - cTotal;
                    if (protCap < 0) protCap = 0;
                    if (prot > protCap) prot = protCap;
                    int alloc;
                    allocatedSoFar.TryGetValue(kv.Key, out alloc);
                    int avail = stock - cTotal - prot - alloc;
                    if (avail < need)
                    {
                        ok = false;
                        SchedulingDiagnostics.RecordBudget(info.Id, VerdictCode.WaitingMaterial, kv.Key,
                            need, avail, stock, cTotal, prot, alloc, IsPermanentShortage(map, kv.Key), now);
                        break;
                    }
                }
                if (!ok)
                {
                    granted[info.Id] = false;
                    continue;
                }
                granted[info.Id] = true;
                SchedulingDiagnostics.RecordBudget(info.Id, VerdictCode.Granted, null, 0, 0, 0, 0, 0, 0, false, now);
                foreach (KeyValuePair<ThingDef, int> kv in info.remaining)
                {
                    int need;
                    if (!needOnOrder.TryGetValue(KeyOf(info.Id, kv.Key), out need) || need <= 0) continue;
                    int topRank;
                    if (!topRankOf.TryGetValue(kv.Key, out topRank) || info.EffRank <= topRank) continue;
                    int alloc;
                    allocatedSoFar.TryGetValue(kv.Key, out alloc);
                    allocatedSoFar[kv.Key] = alloc + need;
                }
            }
        }

        /// <summary>保护期长度（tick）：设置项按游戏日给，默认 1 日；0 表示不保护。</summary>
        private static int ProtectTicks()
        {
            BuildOrderComp comp = BuildOrderComp.Instance;
            float days = comp != null ? comp.protectDays : 1f;
            if (days <= 0f) return 0;
            if (days > 30f) days = 30f;
            double ticks = days * (double)GenDate.TicksPerDay;
            return ticks > int.MaxValue ? int.MaxValue : (int)ticks;
        }

        /// <summary>
        /// 维护保护计时：按「该材料连续被保护的时长」算，不因需求量涨落而重置
        /// （否则高名次一直在缓慢消耗材料时，保护永远不到期，低名次照样被饿死）。
        /// 计时的唯一依据是「这种材料还有没有订单需求」（demandByDef）：
        /// 首次有需求开始计，一直有需求就连续计，彻底没需求才停表。
        /// 不看名次数字、不看最高名次组是哪一类、也不看在途——这些都会随正常施工不断变化，
        /// 拿它们当重置条件等于永远重新计时，到期从此不会发生（v2.1.1 实测 1.6 游戏日 0 次到期，
        /// v2.2 审查 CR-121/CR-126 又指出：按 topReserved 判会被在途打断、按类型键判会随使用者轮换重置）。
        /// </summary>
        private static void UpdateProtectionState(Map map, int now)
        {
            protKinds = 0;
            expiredKinds = 0;
            permanentKinds = 0;
            protectTicks = ProtectTicks();

            // 停表判据：这种材料彻底没有订单需求了（不是「本轮不需要预留」），下次再有需求从头计时
            List<ThingDef> stale = null;
            foreach (ThingDef known in protectSince.Keys)
            {
                if (demandByDef.ContainsKey(known)) continue;
                if (stale == null) stale = new List<ThingDef>();
                stale.Add(known);
            }
            if (stale != null)
            {
                for (int i = 0; i < stale.Count; i++)
                {
                    protectSince.Remove(stale[i]);
                    expiryLogged.Remove(stale[i]);
                }
            }

            // 0 = 不保护：不计时、不计保护种数。关闭期间材料并没有「连续被保护」，
            // 所以把旧进度清掉，重新打开时从当下重新计时，而不是接着关掉前的进度直接到期。
            if (protectTicks <= 0)
            {
                protectSince.Clear();
                expiryLogged.Clear();
            }
            else
            {
                // 开始计时：材料有需求的第一次记当下，之后只读不写——连续计
                foreach (ThingDef def in demandByDef.Keys)
                {
                    if (!protectSince.ContainsKey(def)) protectSince[def] = now;
                }
            }

            foreach (KeyValuePair<ThingDef, int> kv in topReserved)
            {
                ThingDef def = kv.Key;
                if (kv.Value <= 0) continue;

                if (protectTicks > 0)
                {
                    protKinds++;
                    bool expired;
                    EffectiveProtection(def, now, out expired);
                    if (expired)
                    {
                        expiredKinds++;
                        if (expiryLogged.Add(def))
                        {
                            // 名义值与生效值都打：只打名义值会说假话（库存 75 却报"可借 571"），
                            // 只打生效值又丢掉「最高名次组需求远超库存」这条诊断信息。
                            // stock / cTotal 与 Allocate 里 clamp 用的是同一数据源，避免两处口径再次分叉。
                            int stock = map != null && map.resourceCounter != null ? map.resourceCounter.GetCount(def) : 0;
                            int cTotal;
                            committedTotal.TryGetValue(def, out cTotal);
                            int reserveable = stock - cTotal;
                            if (reserveable < 0) reserveable = 0;
                            int nominal = kv.Value;
                            int half = nominal / 2;
                            int effective = half < reserveable ? half : reserveable;
                            Log.Message("[BM] 保护到期：" + def.label + " 名义预留 " + nominal
                                + " → 开放一半 " + half + "；库存 " + stock + " 可预留 " + reserveable
                                + " → 生效保护 " + effective + "，实际可借 " + (reserveable - effective));
                        }
                    }
                    else
                    {
                        expiryLogged.Remove(def);
                    }
                }

                if (IsPermanentShortage(map, def)) permanentKinds++;
            }
        }

        /// <summary>
        /// 实际生效的保护额度：
        /// 保护期内全额保护，低名次一点都借不到；到期后只保留一半，
        /// 低名次按名次顺序最多借走另一半（保护额度的 1/2），不会把最高名次饿死。
        /// 保护期填 0 = 不保护，直接返回 0 且**不算到期**——「到期」是跑满一个窗口后仍留一半，
        /// 和「关掉这套机制」是两回事；把 0 当成立即到期，玩家就没有退出它的入口了。
        /// </summary>
        private static int EffectiveProtection(ThingDef def, int now, out bool expired)
        {
            expired = false;
            int prot;
            if (!topReserved.TryGetValue(def, out prot) || prot <= 0) return 0;

            int limit = protectTicks;
            if (limit <= 0) return 0;

            int since;
            if (!protectSince.TryGetValue(def, out since)) since = now;
            if (now - since < limit) return prot;

            expired = true;
            return prot - prot / 2;
        }

        /// <summary>
        /// 永缺：库存 + 在途占库存 仍小于全部订单的总需求（等到货也不够，低名次等不到）；
        /// 暂缺：总供给够，只是排在更高名次后面。仅用于诊断口径，不改变放行判定。
        /// </summary>
        private static bool IsPermanentShortage(Map map, ThingDef def)
        {
            int stock = map != null && map.resourceCounter != null ? map.resourceCounter.GetCount(def) : 0;
            int committed;
            committedTotal.TryGetValue(def, out committed);
            int demand;
            demandByDef.TryGetValue(def, out demand);
            return stock + committed < demand;
        }

        /// <summary>
        /// 诊断输出分两种：
        /// - 每 600 tick 一行人类可读概览（一眼看总量与在途/保护/老化）；
        /// - 结论变化时输出**结构化** <c>[BM] EVT</c> 行，固定 key=value 顺序，脚本按字段取，
        ///   不再靠正则猜中文（旧格式改一次文案就崩过一次解析器）。
        /// 同一类型组的多张订单结论一致，合并成一条并带 <c>n=</c> 组内单数，避免刷屏。
        /// </summary>
        private static void LogChanges(Map map)
        {
            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            LogOverview(now);

            if (diagLogs >= EventLimit) return;
            OrderRegistry registry = OrderRegistry.Of(map);
            if (registry == null) return;

            changedIds.Clear();
            SchedulingDiagnostics.CollectChanges(registry, now, changedIds);
            if (changedIds.Count == 0) return;

            groupSample.Clear();
            groupCount.Clear();
            groupKeys.Clear();
            for (int i = 0; i < changedIds.Count; i++)
            {
                string key = GroupKeyOf(registry, changedIds[i]);
                if (key == null) continue;
                int prev;
                if (groupCount.TryGetValue(key, out prev))
                {
                    groupCount[key] = prev + 1;
                    continue;
                }
                groupSample[key] = changedIds[i];
                groupCount[key] = 1;
                groupKeys.Add(key);
            }

            for (int i = 0; i < groupKeys.Count; i++)
            {
                string key = groupKeys[i];
                int id = groupSample[key];
                OrderInfo info;
                if (!registry.orders.TryGetValue(id, out info)) continue;
                OrderVerdict v;
                SchedulingDiagnostics.TryGet(id, out v);
                diagLogs++;                                 // 只计真正输出的行
                Log.Message(FormatEvent(id, key, info.Rank, info.EffRank, v, groupCount[key], now,
                    registry.WaitTicksOf(id, now)));
                if (diagLogs >= EventLimit) return;
            }
        }

        private static void LogOverview(int now)
        {
            // now 比上次小 = 读了一张更早的档，视为已过期，否则概览会被永久静默（T-056）
            if (now >= lastOverviewTick && now - lastOverviewTick < 600) return;
            lastOverviewTick = now;
            int allowed = 0;
            int blocked = 0;
            foreach (KeyValuePair<int, bool> kv in granted)
            {
                if (kv.Value) allowed++;
                else blocked++;
            }
            int occupied = enrouteTotal - carriedTotal;
            if (occupied < 0) occupied = 0;
            Log.Message("[BM] 预算概览：待放行 " + allowed + " 单，被暂停 " + blocked + " 单"
                + " | 调用" + calls + "次 涉及" + callTicks + "个tick 节流" + throttled + "次 重建" + rebuilds + "次"
                + " | 在途 原版" + enrouteTotal + " 在手" + carriedTotal + " 占库存" + occupied
                + " | 保护" + protKinds + "种 到期" + expiredKinds + "种 永缺" + permanentKinds + "种"
                + " | 老化" + agedCount + "单");
            calls = 0;
            callTicks = 0;
            throttled = 0;
            rebuilds = 0;
            diagLogs = 0;      // 事件配额按概览窗口重置，长跑也不会永久停掉
        }

        /// <summary>结构化事件行：字段顺序固定，缺省字段直接不出现。</summary>
        private static string FormatEvent(int orderId, string key, int rank, int effRank,
            OrderVerdict v, int count, int now, int waitedTicks)
        {
            StringBuilder sb = new StringBuilder(192);
            // 类型组名里有空格（如「[无品质] 墙」），必须加引号，否则 key=value 会被空格切断
            sb.Append("[BM] EVT id=").Append(orderId).Append(" key=\"").Append(key).Append("\"")
              .Append(" rank=").Append(rank).Append(" eff=").Append(effRank);
            if (v == null)
            {
                sb.Append(" code=Unknown");
                return sb.ToString();
            }
            sb.Append(" code=").Append(v.code);
            if (v.code == VerdictCode.WaitingMaterial && v.def != null)
            {
                sb.Append(" def=").Append(v.def.defName)
                  .Append(" need=").Append(v.need)
                  .Append(" avail=").Append(v.avail)
                  .Append(" stock=").Append(v.stock)
                  .Append(" enroute=").Append(v.enroute)
                  .Append(" prot=").Append(v.protect)
                  .Append(" alloc=").Append(v.alloc)
                  .Append(" short=").Append(v.permanent ? "permanent" : "temporary");
            }
            else if (v.code == VerdictCode.Yielded)
            {
                sb.Append(" byRank=").Append(v.byRank);
                if (v.byThing != null) sb.Append(" by=\"").Append(BuildUtil.GroupKey(v.byThing)).Append("\"");
            }
            sb.Append(" judge=").Append(v.judge);
            // 这单已经多久没进料（秒）：诊断上比「结论持续多久」有用，后者在变化时总是 0
            sb.Append(" waited=").Append(waitedTicks / 60);
            if (count > 1) sb.Append(" n=").Append(count);
            return sb.ToString();
        }

        private static string GroupKeyOf(OrderRegistry registry, int orderId)
        {
            OrderInfo info;
            if (registry == null || !registry.orders.TryGetValue(orderId, out info)) return null;
            Thing t = info.Thing;
            return t != null ? BuildUtil.GroupKey(t) : ("#" + orderId);
        }
    }
}
