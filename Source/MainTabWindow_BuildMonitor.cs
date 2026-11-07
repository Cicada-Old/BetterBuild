using System;
using System.Collections.Generic;
using RimWorld;
using ResearchStyleUI;
using UnityEngine;
using Verse;

namespace BuildMonitor
{
    /// <summary>
    /// 「建造」一级页签，外观与交互沿用原版「研究」菜单：
    /// 底部页签切换「建造」「诊断」，右上角搜索框按名称筛选。
    /// v2.0.0 起工作台优先级拆到「更好的制造」（BuildCraft），本窗口只剩建造侧。
    /// </summary>
    public class MainTabWindow_BuildMonitor : ResearchStyleTabWindow
    {
        private const int BuildTab = 0;
        private const int DiagTab = 1;
        private const int RefreshIntervalTicks = 120;

        private Vector2 scroll;
        private Vector2 diagScroll;
        private string buildDragKey;
        private int buildDragIndex = -1;
        private List<Pawn> pawns = new List<Pawn>();
        private Dictionary<string, List<Thing>> groups = new Dictionary<string, List<Thing>>();
        private int qualityCount;
        private int plainCount;
        private int lastRefreshTick = -1000000;

        // 诊断页签状态：执行者视角默认「全部」，点一下切到第一个小人，再点下一个
        private int diagViewer;
        private Thing diagBestThing;
        private int diagBestRank;
        private bool diagBestComplete = true;
        private readonly int[] verdictCounts = new int[5];
        private readonly List<DiagGroup> diagGroups = new List<DiagGroup>();
        private readonly Dictionary<string, DiagGroup> diagGroupMap = new Dictionary<string, DiagGroup>();

        // 布局：中区表格填满「内容宽 − 右栏」，右栏常驻固定宽；排不开时折到表格下方。
        // 表格不再无限拉伸，展开内容全部搬到右栏，行高因此恒定。
        // windowRect.width 取 UI.screenWidth，已按 GUI 缩放除过，所以高缩放天然表现为「变窄」。
        private const float RightPaneW = 300f;
        private const float PaneGap = 12f;
        private const float MaxTableW = 1500f;
        private const float ColGap = 14f;       // 数字列之间的最小间距，避免「215 377s」读成一列

        // 建造页列宽（前面那段是拖动手柄 + 图标，宽度随行高变，见 RowLeadW）
        private const float RowNameX = 46f;     // 行高 24 档的引导宽（手柄 24 + 图标 18 + 间距 4），同时是引导宽的下限
        private const float NameMinW = 160f;
        private const float WLevel = 70f, WCount = 42f;
        // 缺料 / 在途 两列要容纳「不缺 / 未计」这类两字短词：Medium 字体实测「不缺」「未计」各 42px、英文 none 48px，
        // 44 / 48 只剩 2px 甚至贴边，所以各加宽 8px（T-079，实测数据见 tmp/t079_verify_20261105/）。
        // 材料列没有目标宽度，这 16px 从它身上扣；表宽下限 BuildTableMinW 会自动 +16（窄屏早折行 16px，可接受）。
        private const float WLack = 52f, WEnroute = 56f, WJump = 74f;  // WJump 要装得下英文「Too many」，且 Medium 档「Jump」不贴边（T-070）
        private const float WWorkers = 72f;   // 「允许执行」列：要么「不限」要么「N 人」
        // 材料列没有目标宽度：其它列都是定值，剩下的全给它（宽屏时天然很宽）。
        // WMatMin 只是窄屏下限——抬高它不会让宽屏的材料列更宽，只会把折行阈值顶上去（110→150 会让阈值 766→806）
        private const float WMatMin = 110f;
        // 跳转按钮/「过多」列右侧留白：表宽已按滚动条收窄，再紧贴右沿就会压到滚动条上
        private const float JumpRightPad = 10f;
        // 出滚动条时原版 AdjustRectsForScrollView 从 viewRect 扣掉的宽度；折行阈值要把它算进去（CR-163）
        private const float ScrollBarW = 20f;
        // 诊断页列宽（WJump2 由 50 加到 68：Medium 档两个汉字 ≈34，60 仍偏紧，T-070 再给 8px 呼吸）
        private const float WRank = 44f, WState = 76f, WOrders = 42f, WWait = 52f, WJump2 = 68f;
        private const float TextMinW = 140f;    // 「类型组」「原因」两列的地板宽
        private const float PermW = 62f;          // 权限勾选格宽：英文「Quality」在 54 会与相邻格互压

        // 小人网格（白名单多选 / 执行者视角单选）共用一套尺寸；格子窄了放不下「名字（等级）」
        private const float PawnCellMinW = 88f;
        private const float PawnCellPad = 16f;    // 文字两侧的内边距，避免名字被格边裁掉
        private const float PawnCellH = 24f;
        private const float PawnCellGap = 4f;
        private const float PawnGridMaxH = 120f;  // 超过这个高度就内嵌滚动

        // 白名单展开态：记着「为哪个组展开」，切组时自动收起（不必额外监听点击别处）
        private string workersExpandedFor;
        private Vector2 workersScroll;
        private bool viewerExpanded;
        private Vector2 viewerScroll;
        private Vector2 rightScroll;              // 右区整栏的滚动位置（名单展开后内容会超出右区高度）
        private string rightScrollFor;            // 上次滚动位置属于哪个组，切组即归零

        /// <summary>图标与行高固定比例：24 档正好 18px（现状外观不变），36→27、64→48。</summary>
        private static float IconSize(float rowH)
        {
            return rowH * 0.75f;
        }

        /// <summary>行首引导宽 = 手柄位 24 + 图标 + 间距 4；下限取 24 档的 46，行高再小也不缩手柄。</summary>
        private static float RowLeadW(float rowH)
        {
            return Mathf.Max(RowNameX, 24f + IconSize(rowH) + 4f);
        }

        /// <summary>诊断页类型组列前让出的图标位（与建造页同一位置关系：间距 4 + 图标 + 间距 4）。</summary>
        private static float DiagLeadW(float rowH)
        {
            return 4f + IconSize(rowH) + 4f;
        }

        // 两页排得开的最小表宽：折行阈值由列宽反推，改列宽不必回来改常数。
        // 必须按**当前行高**算——图标随行高放大，按 24 档的 46 算会在 64 档少 30px 把表挤爆。
        private static float BuildTableMinW(float rowH)
        {
            return RowLeadW(rowH) + NameMinW + ColGap + WMatMin + ColGap + WLevel + ColGap
                + WWorkers + ColGap + WCount + ColGap + WLack + ColGap + WEnroute + ColGap + WJump + JumpRightPad;
        }

        private static float DiagTableMinW(float rowH)
        {
            return DiagLeadW(rowH) + TextMinW + ColGap + TextMinW + ColGap + WRank + ColGap
                + WState + ColGap + WOrders + ColGap + WWait + ColGap + WJump2 + JumpRightPad;
        }

        private string selGroup;                // 右栏展示的类型组键；null = 未选中，右栏显示汇总
        private float nameColW = NameMinW;      // 名字列自适应宽度（Rebuild 时按最长的组名 + 余量测出来）
        private readonly Dictionary<string, GroupStat> groupStats = new Dictionary<string, GroupStat>();

        /// <summary>类型组的材料概览（每 120 tick 随 Rebuild 汇总一次，不进绘制热路径）。</summary>
        private sealed class GroupStat
        {
            public List<KeyValuePair<ThingDef, int>> mats;   // 组内**每种材料**的合计用量，按用量降序；第一项即 mainDef
            public int lack;            // 真·缺料：按材料分桶后的组总缺口（共享的「可用」只减一次）
            public bool permanent;      // 其中只要有永缺就标永缺（不是这一时抢空）
            public int needed;          // 还需投入的材料单位（材料成本 − 已装进框架的部分）
            public int enroute;         // 原版在途账本里已在路上的单位
            public ThingDef mainDef;    // 用量最大的那根料（「材料」列的第一项与行内图标同源）
        }

        private struct PaneSplit
        {
            public Rect table;
            public Rect right;
            public bool stacked;
        }

        private static bool NarrowMode(Rect content, float rowH)
        {
            // 最小表宽的语义是「**扣掉滚动条之后**仍能排开」，所以阈值要把 AdjustRectsForScrollView 扣掉的
            // 那 20px 算进去；否则表宽正好等于阈值时材料列会拿到 < WMatMin，设计里的下界就成了假承诺（CR-163）
            return content.width < Mathf.Max(BuildTableMinW(rowH), DiagTableMinW(rowH)) + ScrollBarW + PaneGap + RightPaneW;
        }

        private static PaneSplit SplitPane(Rect area, float rowH)
        {
            if (NarrowMode(area, rowH))
            {
                // 折到下方时高度要容得下最长的一栏（诊断页等料算式：标题+按钮+5 行汇总+算式 8 行 ≈ 338），
                // 但矮窗口不能把表格挤没：最小高度按窗口高的一半与 45% 取小
                float rh = Mathf.Clamp(area.height * 0.52f, Mathf.Min(340f, area.height * 0.45f), 400f);
                return new PaneSplit
                {
                    table = new Rect(area.x, area.y, area.width, Mathf.Max(80f, area.height - rh - PaneGap)),
                    right = new Rect(area.x, area.yMax - rh, area.width, rh),
                    stacked = true
                };
            }
            float rw = Mathf.Min(RightPaneW, area.width * 0.34f);
            float tw = Mathf.Min(area.width - rw - PaneGap, MaxTableW);
            // 表格封顶后剩下的空隙均分到两侧，免得右侧出现一条无主的空白
            float off = Mathf.Max(0f, (area.width - (tw + PaneGap + rw)) * 0.5f);
            return new PaneSplit
            {
                table = new Rect(area.x + off, area.y, tw, area.height),
                right = new Rect(area.x + off + tw + PaneGap, area.y, rw, area.height),
                stacked = false
            };
        }

        // 左栏固定宽度：按百分比会被宽屏拉得很宽，而内容只有标题 + 统计 + 几个小人
        protected override float LeftAreaWidthPercent
        {
            get { return 0f; }
        }

        protected override float LeftAreaMinWidth
        {
            get { return 260f; }
        }

        protected override int TabCount
        {
            get { return 2; }
        }

        protected override string TabLabel(int index)
        {
            if (index == BuildTab) return "BM_TabBuild".Translate().ToString();
            return "BM_TabDiag".Translate().ToString();
        }

        private static int Ticks
        {
            get { return Find.TickManager != null ? Find.TickManager.TicksGame : 0; }
        }

        public override void DoWindowContents(Rect inRect)
        {
            int tick = Ticks;
            if (tick - lastRefreshTick >= RefreshIntervalTicks || tick < lastRefreshTick)
            {
                Rebuild();
                lastRefreshTick = tick;
            }
            base.DoWindowContents(inRect);
        }

        protected override void OnOpened()
        {
            // 排序值必须在 Rebuild() 之前清：Rebuild 内部就按当前排序排一次，放在后面会「按钮显示默认、列表还是上次的顺序」
            // （左区这条是既有问题，T-074 给右区加了同样的状态后两条一起暴露）
            colonistSort = 0;       // 每次打开回到默认（名字升序）
            workerSort = 0;         // T-074：右区排序也回默认，与左区各自独立
            Rebuild();
            lastRefreshTick = Ticks;
            buildDragKey = null;
            buildDragIndex = -1;
            rangeDragId = 0;
            rangeDragMin = false;
            workersExpandedFor = null;
            workersScroll = Vector2.zero;
            viewerExpanded = false;
            viewerScroll = Vector2.zero;
            rightScroll = Vector2.zero;
            rightScrollFor = null;
        }

        protected override void OnTabChanged()
        {
            buildDragKey = null;
            buildDragIndex = -1;
            rangeDragId = 0;
            rangeDragMin = false;
            // 两个内联网格都是「临时展开」，切页签就收起，否则回来时界面被自己上次点开的面板占掉一半
            workersExpandedFor = null;
            workersScroll = Vector2.zero;
            viewerExpanded = false;
            viewerScroll = Vector2.zero;
            rightScroll = Vector2.zero;
            rightScrollFor = null;
        }

        /// <summary>
        /// 没松手就关窗（ESC、或切到别的一级菜单）= 取消这次拖动，把等级范围恢复成拖动开始时的值（T-081）。
        /// 松手时已把 rangeDragStartValid 置 false（视为确认），所以这里不会回滚已经确认过的值。
        /// 切页签也走 WindowStack.TryRemove → PreClose，不需要在 OnTabChanged 里另写回滚。
        /// </summary>
        public override void PreClose()
        {
            if (rangeDragId != 0 && rangeDragStartValid && !string.IsNullOrEmpty(rangeDragTypeKey))
            {
                BuildSkillRangeComp comp = BuildSkillRangeComp.Instance;
                if (comp != null) comp.Set(rangeDragTypeKey, rangeDragStart);
            }
            rangeDragId = 0;
            rangeDragMin = false;
            rangeDragStartValid = false;
            rangeDragTypeKey = null;
            base.PreClose();
        }

        private void Rebuild()
        {
            pawns.Clear();
            groups.Clear();
            for (int i = 0; i < verdictCounts.Length; i++) verdictCounts[i] = 0;
            qualityCount = 0;
            plainCount = 0;

            // 名字列宽每次重建都从当前数据重新测，不能只增不减（长名建完后要能回落）
            nameColW = NameMinW;

            Map map = Find.CurrentMap;
            if (map == null) return;

            // 量宽必须用与行内绘制同一档字体，否则字体跟随行高之后名字列宽会系统性偏小/偏大
            GameFont prevFont = Text.Font;
            Text.Font = FontForRowHeight(BuildOrderComp.RowHeight);

            OrderRegistry registry = OrderRegistry.Of(map);
            if (registry != null) registry.EnsureFresh();

            BuildWorkerComp workerComp = BuildWorkerComp.Instance;
            if (workerComp != null) workerComp.PruneInvalidIds(map);

            List<Pawn> all = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < all.Count; i++)
            {
                Pawn p = all[i];
                if (p != null && HasConstructionWork(p)) pawns.Add(p);
            }
            SortPawns();

            // 小人变少（关建造/死亡/离图）后 diagViewer 可能越界：ViewerPawn 返回 null 让按钮显示「全部」，
            // 而网格「全部」格的判定是 diagViewer<=0，两处不一致 → 一个高亮都没有。越界直接归零（CR-156）
            if (diagViewer > pawns.Count) diagViewer = 0;

            if (registry == null) return;

            foreach (OrderInfo info in registry.orders.Values)
            {
                Thing t = info.Thing;
                if (t == null || t.Destroyed || !t.Spawned) continue;
                if (!BuildUtil.IsPendingBuild(t)) continue;

                if (BuildUtil.KindOf(t) == BuildKind.Quality) qualityCount++;
                else plainCount++;

                // 分组键 = 品质前缀 + 种类 defName（不同尺寸是不同 defName，不会合并；只有材质不同才合并）
                string key = info.Key;
                if (string.IsNullOrEmpty(key)) key = BuildUtil.GroupKey(t);

                List<Thing> list;
                if (!groups.TryGetValue(key, out list))
                {
                    list = new List<Thing>();
                    groups[key] = list;

                    // 名字列宽度按组名实测，不再靠弹性拉满——否则名字短时与等级列之间空一大截
                    string groupLabel = (BuildUtil.KindOf(t) == BuildKind.Quality
                                            ? "BM_PrefixQuality".Translate().ToString()
                                            : "BM_PrefixPlain".Translate().ToString())
                                        + BuildUtil.BaseLabelOf(t);
                    float w = Text.CalcSize(groupLabel).x + 8f;
                    if (w > nameColW) nameColW = w;
                }
                list.Add(t);
            }

            // 诊断数据：预算重建时已经写过，这里补齐「无人可做」与汇总计数
            SchedulingDiagnostics.RefreshNoWorker(map, registry, Ticks);
            SchedulingDiagnostics.Prune(registry);
            SchedulingDiagnostics.Count(registry, verdictCounts);
            RebuildDiagRows(registry);
            RefreshViewerBest();
            RefreshGroupStats(map, registry);

            Text.Font = prevFont;
        }

        /// <summary>
        /// 按类型组汇总材料、真·缺料、还需投入与在途量（每 120 tick 一次，不进绘制热路径）。
        /// 三列都按「组内全部订单的全部材料」聚合，不再只看第一件代表物——材质合并进来的组里，
        /// 代表物可能和大家用的料不一样，只读 things[0] 会让材料/缺料/在途三列互相矛盾。
        /// </summary>
        private void RefreshGroupStats(Map map, OrderRegistry registry)
        {
            groupStats.Clear();
            if (registry == null || map == null) return;
            var enroute = map.enrouteManager;

            // 分桶容器整批复用，每组开头清空：几十组 × 每 120 tick，没必要逐组 new
            Dictionary<ThingDef, int> costByDef = new Dictionary<ThingDef, int>();
            Dictionary<ThingDef, int> needByDef = new Dictionary<ThingDef, int>();
            Dictionary<ThingDef, int> availByDef = new Dictionary<ThingDef, int>();
            HashSet<ThingDef> permDefs = new HashSet<ThingDef>();
            HashSet<ThingDef> allDefs = new HashSet<ThingDef>();

            foreach (KeyValuePair<string, List<Thing>> kv in groups)
            {
                List<Thing> things = kv.Value;
                if (things == null || things.Count == 0) continue;

                costByDef.Clear();
                needByDef.Clear();
                availByDef.Clear();
                permDefs.Clear();
                allDefs.Clear();

                GroupStat gs = new GroupStat();

                for (int i = 0; i < things.Count; i++)
                {
                    Thing t = things[i];

                    // 安装蓝图没有材料清单：TotalMaterialCost() 直接 Log.Error 并返回空表
                    // （Blueprint_Install.cs L69-73），本方法每 120 tick 跑一次 = 每 2 秒一条 Error。
                    // 按单跳过，不依赖「一个组里全是安装蓝图」这个未证实的前提（CR-133）
                    if (t is Blueprint_Install) continue;

                    IConstructible ic = t as IConstructible;
                    List<ThingDefCountClass> costs = ic != null ? ic.TotalMaterialCost() : null;
                    if (costs != null)
                    {
                        for (int c = 0; c < costs.Count; c++)
                        {
                            ThingDefCountClass cc = costs[c];
                            if (cc == null || cc.thingDef == null) continue;
                            int prev;
                            costByDef.TryGetValue(cc.thingDef, out prev);
                            costByDef[cc.thingDef] = prev + cc.count;
                            allDefs.Add(cc.thingDef);
                        }
                    }

                    OrderInfo info;
                    if (registry.orders.TryGetValue(t.thingIDNumber, out info) && info != null)
                    {
                        if (info.remaining != null)
                        {
                            foreach (KeyValuePair<ThingDef, int> m in info.remaining) gs.needed += m.Value;
                        }

                        // 缺料只统计真被材料卡住的订单，并且按材料分桶：同一种料的「可用」是共享库存，
                        // 一组 N 张单只减一次，不能逐单累减（否则缺料虚高成 Σneed − N×avail）
                        OrderVerdict v;
                        if (SchedulingDiagnostics.TryGet(info.Id, out v)
                            && v != null && v.code == VerdictCode.WaitingMaterial && v.def != null)
                        {
                            int n;
                            needByDef.TryGetValue(v.def, out n);
                            needByDef[v.def] = n + v.need;
                            availByDef[v.def] = v.avail;    // 同一材料各单读到的是同一份账，覆盖写即代表值
                            if (v.permanent) permDefs.Add(v.def);
                        }
                    }
                }

                // 可用为负（全局超分配）时 need − avail 会放大，含义是「要立刻开工还得补多少」，如实呈现
                foreach (KeyValuePair<ThingDef, int> nd in needByDef)
                {
                    int a;
                    availByDef.TryGetValue(nd.Key, out a);
                    int deficit = nd.Value - a;
                    if (deficit > 0) gs.lack += deficit;
                    if (permDefs.Contains(nd.Key)) gs.permanent = true;
                }

                // 材料列要的是「每种材料的合计」，不是「主料 ×量 +K」：按用量降序把整份成本表拷出来。
                // 同用量按 defName 排，保证每次重建的顺序一致（字典枚举顺序不保证，会让列内容来回跳）
                if (costByDef.Count > 0)
                {
                    if (gs.mats == null) gs.mats = new List<KeyValuePair<ThingDef, int>>();
                    gs.mats.Clear();
                    foreach (KeyValuePair<ThingDef, int> c in costByDef) gs.mats.Add(c);
                    gs.mats.Sort((a, b) =>
                    {
                        int byCount = b.Value.CompareTo(a.Value);
                        if (byCount != 0) return byCount;
                        return string.Compare(a.Key.defName, b.Key.defName, StringComparison.Ordinal);
                    });
                    gs.mainDef = gs.mats[0].Key;    // 图标仍取用量最大的那根，与材料列第一项同源
                }

                if (enroute != null)
                {
                    for (int i = 0; i < things.Count; i++)
                    {
                        IHaulEnroute target = things[i] as IHaulEnroute;
                        if (target == null) continue;
                        foreach (ThingDef d in allDefs) gs.enroute += enroute.GetEnroute(target, d);
                    }
                }

                groupStats[kv.Key] = gs;
            }
        }

        /// <summary>诊断列表按「类型组」聚合后的一行：同类订单合并，避免几百行一模一样的内容。</summary>
        private sealed class DiagGroup
        {
            public string key;
            public string label;
            public Thing rep;
            public int count;
            public int minRank = int.MaxValue;
            public int minEffRank = int.MaxValue;
            public int maxWait;
            public int worstOrderId;
            public int worstSev = 99;
        }

        /// <summary>
        /// 按类型组聚合后排序：卡住的组排前面（无人可做 → 等料 → 让路 → 禁建 → 放行），同档按名次。
        /// 之前是逐单一行，535 张订单里同类的能占几百行，翻半天才看得出问题在哪一类。
        /// </summary>
        private void RebuildDiagRows(OrderRegistry registry)
        {
            diagGroups.Clear();
            diagGroupMap.Clear();
            if (registry == null) return;
            int now = Ticks;

            foreach (OrderInfo info in registry.orders.Values)
            {
                Thing t = info.Thing;
                if (t == null || t.Destroyed || !t.Spawned) continue;

                string key = string.IsNullOrEmpty(info.Key) ? BuildUtil.GroupKey(t) : info.Key;
                DiagGroup g;
                if (!diagGroupMap.TryGetValue(key, out g))
                {
                    g = new DiagGroup
                    {
                        key = key,
                        rep = t,
                        label = (BuildUtil.KindOf(t) == BuildKind.Quality
                                    ? "BM_PrefixQuality".Translate().ToString()
                                    : "BM_PrefixPlain".Translate().ToString())
                                + BuildUtil.BaseLabelOf(t)
                    };
                    diagGroupMap[key] = g;
                    diagGroups.Add(g);
                }

                g.count++;
                if (info.Rank < g.minRank) g.minRank = info.Rank;
                if (info.EffRank < g.minEffRank) g.minEffRank = info.EffRank;
                int wait = registry.WaitTicksOf(info.Id, now);
                if (wait > g.maxWait) g.maxWait = wait;

                int sev = DiagSeverity(info.Id);
                if (sev < g.worstSev)
                {
                    g.worstSev = sev;
                    g.worstOrderId = info.Id;
                }
            }

            diagGroups.Sort(DiagGroupOrder);
        }

        private static int DiagGroupOrder(DiagGroup a, DiagGroup b)
        {
            if (a.worstSev != b.worstSev) return a.worstSev.CompareTo(b.worstSev);
            if (a.minEffRank != b.minEffRank) return a.minEffRank.CompareTo(b.minEffRank);
            return string.Compare(a.key, b.key, StringComparison.Ordinal);
        }

        /// <summary>
        /// 组内最严重的结论档位：无人可做 → 等料 → 让路 → 禁建 → 放行 → 待判。
        /// 「待判」单独一档压到最后：它只是还没被扫到，不该和真卡在材料上的等料混在一起抢前排。
        /// </summary>
        private static int DiagSeverity(int orderId)
        {
            OrderVerdict v;
            if (!SchedulingDiagnostics.TryGet(orderId, out v)) return 5;
            switch (v.code)
            {
                case VerdictCode.NoWorker: return 0;
                case VerdictCode.WaitingMaterial: return 1;
                case VerdictCode.Yielded: return 2;
                case VerdictCode.Forbidden: return 3;
                default: return 4;
            }
        }

        /// <summary>执行者视角：这个小人当前最该做的是哪一张单（复用调度同一套预演）。</summary>
        private void RefreshViewerBest()
        {
            diagBestThing = null;
            diagBestRank = int.MaxValue;
            diagBestComplete = true;
            Pawn p = ViewerPawn;
            if (p == null) return;
            diagBestThing = BuildPriorityGate.BestFor(p, false, out diagBestRank, out diagBestComplete);
        }

        /// <summary>当前选中的执行者；null = 只看汇总，不做视角分析。</summary>
        private Pawn ViewerPawn
        {
            get
            {
                if (diagViewer <= 0 || diagViewer > pawns.Count) return null;
                return pawns[diagViewer - 1];
            }
        }

        /// <summary>只列出工作栏里启用了「建造」的小人。</summary>
        private static bool HasConstructionWork(Pawn p)
        {
            if (p == null || p.workSettings == null) return false;
            WorkTypeDef wt = WorkTypeDefOf.Construction;
            if (wt == null) return false;
            try
            {
                if (p.workSettings.WorkIsActive(wt)) return true;
            }
            catch { }
            try
            {
                return p.workSettings.GetPriority(wt) > 0;
            }
            catch { return false; }
        }

        private static string ConstructionLevelText(Pawn p)
        {
            if (p == null || p.skills == null) return "BM_LevelNone".Translate().ToString();
            SkillRecord sk = p.skills.GetSkill(SkillDefOf.Construction);
            if (sk == null || sk.TotallyDisabled) return "BM_LevelNone".Translate().ToString();
            return "BM_LevelText".Translate(sk.Level).ToString();
        }

        /// <summary>建造小人列表的排序：0=名字升序（默认）、1=建造等级升序、2=等级降序。</summary>
        private static int colonistSort;

        // T-074（设计 v2.7.15 §三）：右区白名单网格的排序与左区各自独立。两个网格画的是同一个 pawns 列表，
        // 右区若直接调 SortPawns() 会把左区的顺序一起排掉，所以右区只切 workerSort，绘制前复制一份再排。
        private static int workerSort;
        private readonly List<Pawn> rightSortBuf = new List<Pawn>();

        private static string SortLabelKey(int mode)
        {
            if (mode == 1) return "BM_SortAsc";
            if (mode == 2) return "BM_SortDesc";
            return "BM_SortDefault";
        }

        /// <summary>按 mode 重排给定的那个列表，不碰其它列表（左区传 pawns、右区传 rightSortBuf）。</summary>
        private static void SortPawnList(List<Pawn> list, int mode)
        {
            if (mode == 1)
            {
                list.Sort((a, b) =>
                {
                    int c = BuildSkillRangeComp.LevelOf(a).CompareTo(BuildSkillRangeComp.LevelOf(b));
                    return c != 0 ? c : string.Compare(a.LabelShort, b.LabelShort, StringComparison.Ordinal);
                });
            }
            else if (mode == 2)
            {
                list.Sort((a, b) =>
                {
                    int c = BuildSkillRangeComp.LevelOf(b).CompareTo(BuildSkillRangeComp.LevelOf(a));
                    return c != 0 ? c : string.Compare(a.LabelShort, b.LabelShort, StringComparison.Ordinal);
                });
            }
            else
            {
                list.Sort((a, b) => string.Compare(a.LabelShort, b.LabelShort, StringComparison.Ordinal));
            }
        }

        /// <summary>按当前排序重排小人；切排序只重排，不重新扫订单。</summary>
        private void SortPawns()
        {
            SortPawnList(pawns, colonistSort);
        }

        private static string ConstructionLevelShort(Pawn p)
        {
            if (p == null || p.skills == null) return "-";
            SkillRecord sk = p.skills.GetSkill(SkillDefOf.Construction);
            if (sk == null || sk.TotallyDisabled) return "-";
            return "BM_LevelShort".Translate(sk.Level).ToString();
        }

        protected override void DrawLeftPane(Rect rect)
        {
            Rect inner = rect.ContractedBy(12f);
            float y = inner.y + 2f;

            // 标题 + 说明入口：原来那三段常驻说明收进提示，只在需要时展开
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inner.x, y, inner.width - 42f, 28f), "BM_WindowTitle".Translate());
            GUI.color = new Color(0.6f, 0.68f, 0.76f);
            // Medium 字体下 "(?)" 约 40px，22f 宽的 Rect 会把问号裁掉
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(inner.xMax - 40f, y, 40f, 28f), "(?)");
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            TooltipHandler.TipRegion(new Rect(inner.xMax - 40f, y, 40f, 28f), "BM_WindowHint".Translate());
            Text.Font = GameFont.Small;
            y += 34f;

            // 全界面只保留这一处统计（右栏那块重复的已删除）
            Widgets.Label(new Rect(inner.x, y, inner.width, 22f),
                "BM_StatsLine".Translate(qualityCount + plainCount, qualityCount, plainCount));
            y += 27f;
            Widgets.DrawLineHorizontal(inner.x, y, inner.width);
            y += 12f;

            // 建造小人：名字 + 等级 + 两个权限勾选，一行一人；不再单开一张只读列表
            Widgets.Label(new Rect(inner.x, y, inner.width - 60f, 22f), "BM_Builders".Translate());
            if (Widgets.ButtonText(new Rect(inner.xMax - 56f, y + 1f, 56f, 20f), SortLabelKey(colonistSort).Translate()))
            {
                colonistSort = (colonistSort + 1) % 3;
                SortPawns();
            }
            y += 24f;

            if (pawns.Count == 0)
            {
                Widgets.Label(new Rect(inner.x, y, inner.width, 22f), "BM_NoColonists".Translate());
                y += 24f;
            }
            // 权限格右对齐排布：两格 + 等级数字都从右边倒着排，窄屏时先牺牲小人名称的宽度
            float permPX = inner.xMax - 4f - PermW;
            float permQX = permPX - PermW;
            float levelX = permQX - 4f - 38f;
            float nameW = Mathf.Max(40f, levelX - inner.x - 7f);
            for (int i = 0; i < pawns.Count; i++)
            {
                if (y + 24f > inner.yMax - 26f) break;      // 给底部「设置」按钮留一整行
                Pawn p = pawns[i];
                Rect row = new Rect(inner.x, y, inner.width, 24f);
                if (i % 2 == 0) Widgets.DrawAltRect(row);

                Widgets.Label(new Rect(inner.x + 3f, y + 1f, nameW, 22f), p.LabelShort.Truncate(nameW, null));

                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = new Color(0.72f, 0.78f, 0.84f);
                Widgets.Label(new Rect(levelX, y + 1f, 38f, 22f), ConstructionLevelShort(p));
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;

                bool q = BuildPermsComp.Allowed(p, BuildKind.Quality);
                bool pl = BuildPermsComp.Allowed(p, BuildKind.Plain);

                bool nq = q;
                Widgets.CheckboxLabeled(new Rect(permQX, y + 1f, PermW, 22f),
                    "BM_PermQualityShort".Translate(), ref nq);
                if (nq != q) BuildPermsComp.SetAllowed(p, BuildKind.Quality, nq);
                TooltipHandler.TipRegion(new Rect(permQX, y + 1f, PermW, 22f), "BM_AllowQuality".Translate());

                bool np = pl;
                Widgets.CheckboxLabeled(new Rect(permPX, y + 1f, PermW, 22f),
                    "BM_PermPlainShort".Translate(), ref np);
                if (np != pl) BuildPermsComp.SetAllowed(p, BuildKind.Plain, np);
                TooltipHandler.TipRegion(new Rect(permPX, y + 1f, PermW, 22f), "BM_AllowPlain".Translate());

                y += 24f;
            }

            // 低频配置收进弹窗，不常驻
            Rect btn = new Rect(inner.x, inner.yMax - 26f, inner.width, 26f);
            if (Widgets.ButtonText(btn, "BM_SchedSettings".Translate()))
            {
                Find.WindowStack.Add(new Dialog_ScheduleSettings());
            }
            TooltipHandler.TipRegion(btn, "BM_SchedHint".Translate());
        }

        protected override void DrawTabContents(Rect contentRect, int tab)
        {
            Rect rect = contentRect.ContractedBy(8f);
            if (tab == DiagTab)
            {
                DrawDiagTab(rect);
                return;
            }
            DrawBuildPane(rect);
        }

        // 行高由「调度设置」里的输入框决定（BuildOrderComp.RowHeight），不再是常量

        private void DrawBuildPane(Rect outRect)
        {
            float rowH = BuildOrderComp.RowHeight;
            PaneSplit split = SplitPane(outRect, rowH);

            // 排序与过滤都必须在算 contentH 之前完成（CR-131）：contentH 要的是「真正会画出来的行数」。
            // 用未过滤的 groups.Count 会让 AdjustRectsForScrollView 误判「有滚动条」，
            // 白扣 20px 宽、表头下移 6px，且在打字/清空之间来回跳
            List<KeyValuePair<string, List<Thing>>> list2 = new List<KeyValuePair<string, List<Thing>>>(groups);
            BuildOrderComp orderComp = BuildOrderComp.Instance;
            list2.Sort((a, b) =>
            {
                int ra = orderComp != null ? orderComp.Rank(a.Key) : int.MaxValue;
                int rb = orderComp != null ? orderComp.Rank(b.Key) : int.MaxValue;
                if (ra != rb) return ra.CompareTo(rb);
                int byCount = b.Value.Count.CompareTo(a.Value.Count);
                if (byCount != 0) return byCount;
                return string.Compare(a.Key, b.Key, StringComparison.Ordinal);
            });

            List<KeyValuePair<string, List<Thing>>> visible = list2;
            if (SearchActive)
            {
                visible = new List<KeyValuePair<string, List<Thing>>>(list2.Count);
                for (int i = 0; i < list2.Count; i++)
                {
                    Thing rep0 = list2[i].Value[0];
                    string label0 = (BuildUtil.KindOf(rep0) == BuildKind.Quality
                                        ? "BM_PrefixQuality".Translate().ToString()
                                        : "BM_PrefixPlain".Translate().ToString())
                                    + BuildUtil.BaseLabelOf(rep0);
                    if (MatchesSearch(label0)) visible.Add(list2[i]);
                }
            }

            float contentH = 26f + visible.Count * rowH + 12f;

            // 先按真实内容高建 viewRect，再交给原版 AdjustRectsForScrollView 决定要不要扣滚动条宽度；
            // 列坐标必须基于扣完之后的宽度，否则出现滚动条时最右列（跳转/过多）被压住 20px
            Rect scrollOut = split.table;
            Rect viewRect = new Rect(0f, 0f, scrollOut.width, contentH);
            Widgets.AdjustRectsForScrollView(split.table, ref scrollOut, ref viewRect);
            float tableW = viewRect.width;

            // 列顺序（用户 2026-11-04 16:53 定稿）：类型组 | 材料 | 等级范围 | 允许执行 | 数量 | 缺料 | 在途 | 跳转
            // 定值列从右往左排，材料列吃「表宽 − 其它列」的全部剩余：宽屏多出来的宽度只进材料列
            float xJump = tableW - WJump - JumpRightPad;
            float xEnroute = xJump - WEnroute - ColGap;
            float xLack = xEnroute - WLack - ColGap;
            float xCount = xLack - ColGap - WCount;
            float xWorkers = xCount - ColGap - WWorkers;
            float xLevel = xWorkers - ColGap - WLevel;
            float matRight = xLevel - ColGap;
            float lead = RowLeadW(rowH);
            // 名字列最多让到「材料列还剩 WMatMin」为止。WMatMin 是窄屏下限、不是目标宽度：
            // 抬高它不会让宽屏的材料列更宽，只会把折行阈值一起顶上去
            float nameMax = Mathf.Max(NameMinW, matRight - lead - ColGap - WMatMin - ColGap);
            float nameW = Mathf.Clamp(nameColW, NameMinW, nameMax);
            float xName = lead;
            float xMat = lead + nameW + ColGap;
            // 剩余全给材料列。窄到连「各列下限」都排不开时钳到 0，负宽度会把 Truncate/Rect 带崩
            float matW = Mathf.Max(0f, matRight - xMat);

            Widgets.BeginScrollView(scrollOut, ref scroll, viewRect);
            float off = 0f;

            GameFont dataFont = FontForRowHeight(rowH);

            // 表头固定 Small + 24px 带：表头是结构标签不是数据，列宽也是按 Small 表头定的。
            // 跟着行高换 Medium 会竖着越出 24 的带（Medium 行高 29）、横向压到相邻列（设计 §7.2）
            Text.Font = GameFont.Small;
            float headTextH = Text.LineHeight;
            float headY = Mathf.Max(0f, 24f - headTextH);     // 在 24 的带里垂直居中

            // 表头：把列表做成一张表，而不是一排散落的控件。列头一律列内居中
            Widgets.DrawBoxSolid(new Rect(off, 0f, tableW, 24f), new Color(1f, 1f, 1f, 0.09f));
            GUI.color = new Color(0.7f, 0.76f, 0.82f);
            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(new Rect(xName, headY, nameW, headTextH), "BM_ColGroup".Translate());
            Widgets.Label(new Rect(xMat, headY, matW, headTextH), "BM_ColMaterial".Translate());
            Widgets.Label(new Rect(xLevel, headY, WLevel, headTextH), "BM_ColLevel".Translate());
            Widgets.Label(new Rect(xWorkers, headY, WWorkers, headTextH), "BM_ColWorkers".Translate());
            Widgets.Label(new Rect(xCount, headY, WCount, headTextH), "BM_ColCount".Translate());
            Widgets.Label(new Rect(xLack, headY, WLack, headTextH), "BM_ColLack".Translate());
            Widgets.Label(new Rect(xEnroute, headY, WEnroute, headTextH), "BM_ColEnroute".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;

            // 表头之后才切到数据字体
            Text.Font = dataFont;
            float textH = Text.LineHeight;     // 文字格高随行高档位变，写死 22 会把 Medium 裁掉

            float y = 26f;
            // 可视剔除用 Adjust 之后的真实视口上下沿（CR-132）：split.table 是收窄前的高，
            // 出滚动条时实际可视高少 12px。±rowH 是滑动时的预绘余量，不是补偿
            float viewTop = scroll.y + (scrollOut.y - split.table.y) - rowH;
            float viewBottom = scroll.y + (scrollOut.yMax - split.table.y) + rowH;

            for (int i = 0; i < visible.Count; i++)
            {
                string key = visible[i].Key;
                List<Thing> things = visible[i].Value;
                Thing rep = things[0];
                string label = (BuildUtil.KindOf(rep) == BuildKind.Quality
                                   ? "BM_PrefixQuality".Translate().ToString()
                                   : "BM_PrefixPlain".Translate().ToString())
                               + BuildUtil.BaseLabelOf(rep);

                // 行高恒定：展开内容在右栏，不再把行顶高
                if (y + rowH < viewTop) { y += rowH; continue; }
                if (y > viewBottom) break;

                Rect row = new Rect(off, y, tableW, rowH);
                // 行高可变：所有纵向位置都从 rowH 推，不再写死 y+4 / y+2；
                // 文字格高用当前档位的真实行高（写死 22 在 Medium 档会把字裁掉）
                float textY = y + Mathf.Max(0f, (rowH - textH) * 0.5f);
                // 按钮高度随字体（T-070）：原来写死 20，行高 ≥32 换 Medium 后文字高 29，上下都溢出按钮边框
                float ctrlH = Mathf.Clamp(textH + 2f, 20f, rowH);
                float ctrlY = y + (rowH - ctrlH) * 0.5f;
                float iconSize = IconSize(rowH);   // 与行高固定比例（24→18），不再用 min(18, rowH−6) 那种封顶写法
                float iconY = y + (rowH - iconSize) * 0.5f;
                if (i % 2 == 0) Widgets.DrawAltRect(row);
                if (selGroup == key || buildDragKey == key) Widgets.DrawHighlightSelected(row);

                Rect handleRect = new Rect(off + 2f, y, 20f, rowH);
                GUI.color = new Color(0.7f, 0.7f, 0.7f);
                Widgets.Label(new Rect(off + 6f, textY, 18f, textH), "≡");
                GUI.color = Color.white;
                TooltipHandler.TipRegion(handleRect, "BM_DragOrderTip".Translate());

                GroupStat gs;
                groupStats.TryGetValue(key, out gs);

                // 图标走原版 Widgets.DefIcon（抽成 DrawGroupIcon，与诊断页共用，两页同图同色）：
                // stuff 优先组内用量最大的那根料（与「材料」列同源），没有就退回这一单实际的 stuff
                DrawGroupIcon(rep, new Rect(off + 24f, iconY, iconSize, iconSize), gs != null ? gs.mainDef : null);

                Widgets.Label(new Rect(xName, textY, nameW, textH), label.Truncate(nameW, null));

                string typeKey = BuildUtil.TypeKey(rep);
                BuildSkillRangeComp comp = BuildSkillRangeComp.Instance;
                IntRange current = comp != null ? comp.Get(typeKey) : new IntRange(0, 20);

                // 除「类型组」内容左对齐外，其余列内容一律列内居中
                Text.Anchor = TextAnchor.UpperCenter;

                // 材料列：每种材料的合计（「料名 ×量」用分隔符衔接）。行内按列宽截断，完整清单在 tooltip
                GUI.color = new Color(0.82f, 0.86f, 0.9f);
                Widgets.Label(new Rect(xMat, textY, matW, textH), MaterialListText(gs).Truncate(matW, null));
                GUI.color = Color.white;
                TooltipHandler.TipRegion(new Rect(xMat, textY, matW, textH), MaterialListTooltip(gs));

                GUI.color = new Color(0.8f, 0.85f, 0.9f);
                Widgets.Label(new Rect(xLevel, textY, WLevel, textH),
                    "BM_LevelRangeText".Translate(current.min, current.max));
                GUI.color = Color.white;

                // 「改」按钮已去掉：选中组走名字列的隐形按钮，等级调整在右侧详情里
                TooltipHandler.TipRegion(new Rect(xLevel, textY, WLevel, textH),
                    "BM_LevelTooltip".Translate(current.min, current.max, label,
                        EligiblePawnsText(BuildUtil.KindOf(rep), current, typeKey)));

                // 「允许执行」列：没设白名单 = 不限；设了就报人数，tooltip 列全名
                BuildWorkerComp wcomp = BuildWorkerComp.Instance;
                int workerCount = wcomp != null ? wcomp.Count(typeKey) : 0;
                GUI.color = workerCount > 0 ? new Color(0.7f, 0.9f, 0.75f) : new Color(0.5f, 0.55f, 0.6f);
                Widgets.Label(new Rect(xWorkers, textY, WWorkers, textH),
                    workerCount > 0 ? "BM_WorkersN".Translate(workerCount) : "BM_WorkersUnlimited".Translate());
                GUI.color = Color.white;
                if (workerCount > 0)
                {
                    string names = SelectedWorkersText(typeKey);
                    if (!string.IsNullOrEmpty(names))
                        TooltipHandler.TipRegion(new Rect(xWorkers, textY, WWorkers, textH),
                            "BM_WorkersWho".Translate(names));
                }

                Widgets.Label(new Rect(xCount, textY, WCount, textH), things.Count.ToString());

                // 缺料 / 在途 两列宽 52 / 56（T-079 实测加宽），只够两字短词，整句挂 tooltip；数字照常显示，0 也照实写 0（T-078）
                GUI.color = LackColor(gs);
                Rect lackRect = new Rect(xLack, textY, WLack, textH);
                string lackCell = gs == null ? "BM_CellNotCounted".Translate().ToString()
                    : (gs.lack > 0 ? gs.lack.ToString() : "BM_CellNoShortage".Translate().ToString());
                Widgets.Label(lackRect, lackCell.Truncate(WLack, null));
                if (gs == null || gs.lack <= 0)
                    TooltipHandler.TipRegion(lackRect, gs == null
                        ? "BM_CellNotCountedTip".Translate().ToString()
                        : "BM_CellNoShortageTip".Translate().ToString());
                GUI.color = gs != null && gs.enroute > 0 ? new Color(0.6f, 0.85f, 0.7f) : new Color(0.5f, 0.55f, 0.6f);
                Rect enrouteRect = new Rect(xEnroute, textY, WEnroute, textH);
                Widgets.Label(enrouteRect, (gs != null
                    ? gs.enroute.ToString() : "BM_CellNotCounted".Translate().ToString()).Truncate(WEnroute, null));
                if (gs == null)
                    TooltipHandler.TipRegion(enrouteRect, "BM_CellNotCountedTip".Translate().ToString());
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;

                if (things.Count < 5)
                {
                    if (Widgets.ButtonText(new Rect(xJump, ctrlY, WJump, ctrlH), "BM_Jump".Translate()))
                    {
                        JumpTo(rep);
                    }
                }
                else
                {
                    GUI.color = new Color(0.55f, 0.55f, 0.55f);
                    // 居中而不是右对齐：右对齐会让「过多」正好贴住列的右沿，
                    // 而那正是竖向滚动条所在的位置；居中后左右都留出余量
                    Text.Anchor = TextAnchor.UpperCenter;
                    Widgets.Label(new Rect(xJump, textY, WJump, textH), "BM_TooMany".Translate());
                    Text.Anchor = TextAnchor.UpperLeft;
                    GUI.color = Color.white;
                    // 矩形只盖「过多」这一列（T-080 裁定 A）：原版把每条 tooltip 按文案 hash 收进 activeTips、
                    // DrawActiveTips 全部叠着画，用整行 row 会让这一行每个格子都多弹一条，挤掉格子自己的说明
                    TooltipHandler.TipRegion(new Rect(xJump, textY, WJump, textH), "BM_TooManyTip".Translate());
                }

                // 点名称列 = 选中，右栏显示这一组的详情与等级滑块（不碰拖动手柄与按钮）。
                // 用隐形按钮而不是自己嗅 MouseDown：这样桥/自动化也能点到同一处
                Rect pickRect = new Rect(xName, y, nameW, rowH);
                bool pickClick = Widgets.ButtonInvisible(pickRect) && buildDragKey == null;

                if (Event.current.type == EventType.MouseDown && handleRect.Contains(Event.current.mousePosition))
                {
                    buildDragKey = key;
                    buildDragIndex = i;
                    Event.current.Use();
                }
                else if (Event.current.type == EventType.MouseDrag && buildDragKey != null)
                {
                    if (row.Contains(Event.current.mousePosition) && key != buildDragKey)
                    {
                        // visible 是过滤后的子集，i 不是 list2 的下标（CR-141）：
                        // 落点按目标行的 key 回查完整列表，否则「拖到第 7 名」会插成「第 1 名」并写进存档
                        int dropAt = list2.FindIndex(x => string.Equals(x.Key, key, StringComparison.Ordinal));
                        if (dropAt < 0) dropAt = i;
                        MoveBuildGroup(list2, buildDragKey, dropAt);
                        buildDragIndex = dropAt;
                        Event.current.Use();
                    }
                }
                else if (Event.current.rawType == EventType.MouseUp && buildDragKey != null)
                {
                    buildDragKey = null;
                    buildDragIndex = -1;
                    Event.current.Use();
                }
                else if (pickClick)
                {
                    selGroup = selGroup == key ? null : key;
                    Event.current.Use();
                }

                y += rowH;
            }

            Widgets.EndScrollView();
            // 表格按行高档位设过字体，出滚动区后必须复位：TooltipHandler 全程不设字体，
            // tooltip 会用帧末当前档位画出来，Medium 泄漏进去就撑破提示框（CR-154）
            Text.Font = GameFont.Small;

            DrawBuildRightPane(split.right);
        }

        private void DrawBuildRightPane(Rect area)
        {
            List<Thing> things = null;
            if (selGroup != null) groups.TryGetValue(selGroup, out things);
            if (things == null || things.Count == 0)
            {
                DrawSummaryCards(area, "BM_PanePickHint");
                return;
            }

            Thing rep = things[0];
            string label = (BuildUtil.KindOf(rep) == BuildKind.Quality
                              ? "BM_PrefixQuality".Translate().ToString()
                              : "BM_PrefixPlain".Translate().ToString())
                           + BuildUtil.BaseLabelOf(rep);
            string typeKey = BuildUtil.TypeKey(rep);
            BuildSkillRangeComp comp = BuildSkillRangeComp.Instance;
            IntRange current = comp != null ? comp.Get(typeKey) : new IntRange(0, 20);
            GroupStat gs;
            groupStats.TryGetValue(selGroup, out gs);

            // 需求 1：右区整栏改成滚动视口。名单展开后「按钮 + 网格 + 5 行 KV」会越过 area.yMax，
            // 而右区原本没有任何 group/clip，越界部分直接画到窗口别处；只加裁剪又会把底部的
            // 「缺料/在途/还需」藏掉（面向结果），所以裁剪只是滚动的副产物。
            BuildWorkerComp wcomp = BuildWorkerComp.Instance;
            bool listExpanded = workersExpandedFor == selGroup && wcomp != null;
            Widgets.DrawBoxSolid(area, new Color(1f, 1f, 1f, 0.06f));   // 底色画在滚动之外，否则整块背景会跟着滚

            if (rightScrollFor != selGroup)
            {
                rightScrollFor = selGroup;   // 切组回到顶部，否则新组继承上一组的滚动位置
                rightScroll = Vector2.zero;
            }

            // 宽度全程恒定：视口铺满右区、滚动条不画也不占位，所以提示的换行行数、网格的列数、内容高
            // 都不会因「要不要出滚动条」而变（T-070 §4，替代原来的两遍收敛 + AdjustRectsForScrollView）
            string hint1 = "BM_LevelOpenHint1".Translate();
            string hint2 = "BM_LevelOpenHint2".Translate();
            Rect paneOut = area;
            float innerW = Mathf.Max(60f, paneOut.width - 20f);      // 四周 10px 内边距
            float h1 = TinyHintHeight(hint1, innerW);
            float h2 = TinyHintHeight(hint2, innerW);

            // T-072 §4.1：状态行同时给「名单人数」和其中「可建造人数」，判定与完工闸门同一套，不另起口径。
            // 这串字比原来的「N 人」长得多，要先量宽：放得下就贴在按钮右侧同行，放不下就自己占一行——
            // 右对齐放不下时会被夹回 rect.x，末尾的字直接溢出窗口（实机复现过）
            int workerCount = wcomp != null ? wcomp.Count(typeKey) : 0;
            string workersStatusTip = null;
            int canBuild = workerCount > 0 ? WorkersListSummary(rep, typeKey, wcomp, out workersStatusTip) : 0;
            string workersStatus = workerCount > 0
                ? "BM_WorkersListedCan".Translate(workerCount, canBuild)
                : "BM_WorkersUnlimited".Translate();
            float statusW;
            Text.Font = GameFont.Tiny;
            statusW = Text.CalcSize(workersStatus).x + 4f;
            Text.Font = GameFont.Small;
            bool statusOwnLine = innerW - 6f - 96f < statusW;   // 按钮最少留 96，再挤就换行

            // 网格画在名单块里、两侧各缩 4px，量高必须用同一个宽度，否则多算一列、少算一行，末行被裁
            // T-074：右区名单网格用复制出来的列表排序，左区的 pawns 一字不动
            rightSortBuf.Clear();
            rightSortBuf.AddRange(pawns);
            SortPawnList(rightSortBuf, workerSort);
            float listGridH = listExpanded ? PawnGridHeight(rightSortBuf, innerW - 8f, false, null) : 0f;
            float paneH = 24f + 28f + 20f + 42f + h1 + h2 + 4f + 20f + 28f
                + (statusOwnLine ? 22f : 0f)
                + (listExpanded ? 24f + listGridH : 0f) + 5 * 24f + 8f;

            // paneView.width 必须严格等于 paneOut.width：多 1px 就会被 Unity 判成横向溢出而进入横向滚动状态
            Rect paneView = new Rect(0f, 0f, paneOut.width, paneH);
            // showScrollbars=false → 滚动条不绘制也不占宽，滚轮/触控板仍能滚（原版资源清单 ResourceReadout.cs L36-52 同款写法）
            Widgets.BeginScrollView(paneOut, ref rightScroll, paneView, false);

            // 视口内的坐标全部相对 (0,0)，四周仍留 10px 内边距（与改之前的 ContractedBy(10f) 等效）
            Rect inner = new Rect(10f, 10f, innerW, paneH - 20f);
            float y = inner.y;
            Text.Font = GameFont.Small;      // 右区正文固定 Small，不随行高变（§4.2 要点 4）

            Widgets.Label(new Rect(inner.x, y, inner.width, 24f), label.Truncate(inner.width, null));
            y += 24f;
            if (Widgets.ButtonText(new Rect(inner.x, y, 64f, 20f), "BM_Close".Translate())) selGroup = null;
            if (things.Count < 5 && GUI.enabled)
            {
                if (Widgets.ButtonText(new Rect(inner.xMax - 64f, y, 64f, 20f), "BM_Jump".Translate())) JumpTo(rep);
            }
            y += 28f;

            GUI.color = new Color(0.7f, 0.76f, 0.82f);
            Widgets.Label(new Rect(inner.x, y, inner.width, 20f), "BM_ColLevel".Translate());
            GUI.color = Color.white;
            y += 20f;

            // 滑块在右栏独占一行：两端数字有足够空间，也不会与表格任何列抢位置
            Rect bar = new Rect(inner.x + 24f, y + 8f, Mathf.Max(120f, inner.width - 48f), 22f);
            IntRange edited = current;
            int barId = ("BMRange_" + typeKey).GetHashCode();
            if (barId == 0) barId = 1;
            if (DrawRangeBar(bar, barId, typeKey, ref edited, 0, 20) && comp != null) comp.Set(typeKey, edited);
            y += 42f;

            // T-040：分两行居中画，行高按实际文字高度算——固定 26f 会把第二行裁掉。
            // h1/h2 在方法开头按视口宽量好（内容高要先于 AdjustRectsForScrollView 定死），这里只画
            GUI.color = new Color(0.66f, 0.71f, 0.77f);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(inner.x, y, inner.width, h1), hint1);
            Widgets.Label(new Rect(inner.x, y + h1, inner.width, h2), hint2);
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            y += h1 + h2 + 4f;

            // 需求 3：允许执行的小人（白名单）。展开态记着组键，切组自动收起；不弹窗，直接内联在右区
            GUI.color = new Color(0.7f, 0.76f, 0.82f);
            Widgets.Label(new Rect(inner.x, y, inner.width, 20f), "BM_WorkersTitle".Translate());
            GUI.color = Color.white;
            y += 20f;

            float wBtnW = statusOwnLine
                ? Mathf.Min(150f, inner.width * 0.55f)
                : Mathf.Min(150f, Mathf.Max(96f, inner.width - 6f - statusW));
            if (Widgets.ButtonText(new Rect(inner.x, y, wBtnW, 24f), "BM_WorkersBtn".Translate()))
            {
                workersExpandedFor = workersExpandedFor == selGroup ? null : selGroup;
                workersScroll = Vector2.zero;
                rightScroll = Vector2.zero;     // 展开/收起改变了整栏高度，留在原滚动位置会跳到别处
            }
            Rect workersStateRect = statusOwnLine
                ? new Rect(inner.x, y + 28f + 3f, inner.width, 16f)
                : new Rect(inner.x + wBtnW + 6f, y + 6f, inner.width - wBtnW - 6f, 16f);
            Text.Anchor = TextAnchor.UpperRight;
            Text.Font = GameFont.Tiny;
            // 名单非空却一个人都做不了 → 警示色，让「设了名单没人能建」一眼可见（设计 §4.1）
            GUI.color = workerCount > 0
                ? (canBuild > 0 ? new Color(0.7f, 0.9f, 0.75f) : new Color(0.95f, 0.6f, 0.6f))
                : new Color(0.5f, 0.55f, 0.6f);
            Widgets.Label(workersStateRect, workersStatus);
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            TooltipHandler.TipRegion(new Rect(inner.x, y, wBtnW, 24f), "BM_WorkersBtnTip".Translate());
            // 状态区单独挂 tooltip：逐个名单内小人说明「可建造」或被哪一层拦下（设计 §4.2）
            TooltipHandler.TipRegion(workersStateRect, workerCount > 0
                ? (workersStatusTip ?? "BM_None".Translate().ToString())
                : "BM_WorkersEmptyTip".Translate().ToString());
            y += 28f + (statusOwnLine ? 22f : 0f);

            if (listExpanded)
            {
                // CR-151 方案 A：「全部允许」= 清空名单 = 不限，所以不给「把当前这批人整体快照进名单」的按钮
                // （那样会让列显示「N 人」而不是「不限」，之后新招募/重开建造的小人被挡在名单外）。
                // 「全部禁止」也不做：空名单语义下表达不出「零人可完工」，要冻结就用等级上限或原版禁建。
                // 需求 2：按钮 + 网格是一个整块，先垫更深一档的黑底 + 1px 边框界定区域，再画控件（顺序反了会盖住控件）。
                // 块高由 PawnGridHeight 推出来，不写死；PawnGridHeight 自带 4px 间隙，这里按净网格高算
                float gridNetH = Mathf.Max(0f, listGridH - 4f);
                Rect block = new Rect(inner.x, y - 4f, inner.width, 36f + gridNetH);
                Widgets.DrawBoxSolid(block, new Color(0f, 0f, 0f, 0.18f));
                Widgets.DrawBox(block, 1);

                Rect clearRect = new Rect(inner.x + 4f, y + 4f, 140f, 20f);
                if (Widgets.ButtonText(clearRect, "BM_WorkersClear".Translate()))
                {
                    wcomp.SetAll(typeKey, null);
                }
                TooltipHandler.TipRegion(clearRect, "BM_WorkersClearTip".Translate());
                // T-074：清空名单右边那个空位放右区自己的排序按钮（三态文案复用左区那三个 key）。
                // 只切 workerSort，不重排 pawns——重排发生在上面复制进 rightSortBuf 的时候
                Rect sortRect = new Rect(clearRect.xMax + 4f, y + 4f, 56f, 20f);
                if (Widgets.ButtonText(sortRect, SortLabelKey(workerSort).Translate()))
                    workerSort = (workerSort + 1) % 3;
                y += 24f;

                string wKey = typeKey;
                y += DrawPawnGrid(new Rect(inner.x + 4f, y, inner.width - 8f, PawnGridMaxH), rightSortBuf,
                    p => wcomp.Contains(wKey, p.thingIDNumber),
                    p => wcomp.Toggle(wKey, p.thingIDNumber),
                    false, null, false, null, ref workersScroll);
                // 下一行 KV 从块外起，否则会被名单块的底色压住最后一格
                y = Mathf.Max(y, block.yMax + 4f);
            }

            y = DrawKV(inner, y, "BM_ColCount", things.Count.ToString(), null);
            // 材料值可能很长（右栏这一行只有半宽），按半宽截断，完整清单挂 tooltip——与表格里的材料列同一口径
            float matKVTop = y;
            y = DrawKV(inner, y, "BM_ColMaterial", MaterialListText(gs).Truncate(inner.width * 0.5f, null), null);
            TooltipHandler.TipRegion(new Rect(inner.x, matKVTop, inner.width, 20f), MaterialListTooltip(gs));
            // 右区值列有 140 宽，够放整句：没统计与不缺料是两回事，不再共用一个「—」（T-078）
            string lackText = gs == null ? "BM_CellNotCountedTip".Translate().ToString()
                : (gs.lack > 0 ? gs.lack.ToString() : "BM_CellNoShortageTip".Translate().ToString());
            if (gs != null && gs.lack > 0 && gs.permanent) lackText += " ·" + "BM_DiagPermanent".Translate().ToString();
            y = DrawKV(inner, y, "BM_ColLack", lackText, LackColor(gs));
            y = DrawKV(inner, y, "BM_ColEnroute", gs != null ? gs.enroute.ToString() : "BM_CellNotCountedTip".Translate().ToString(),
                gs != null && gs.enroute > 0 ? new Color(0.6f, 0.85f, 0.7f) : (Color?)null);
            y = DrawKV(inner, y, "BM_ColNeeded", gs != null ? gs.needed.ToString() : "BM_CellNotCountedTip".Translate().ToString(), null);
            Widgets.EndScrollView();
        }

        /// <summary>缺料列配色：没被材料卡住是灰的，暂缺偏黄，永缺偏红。</summary>
        private static Color LackColor(GroupStat gs)
        {
            if (gs == null || gs.lack <= 0) return new Color(0.5f, 0.55f, 0.6f);
            return gs.permanent ? new Color(0.95f, 0.6f, 0.6f) : new Color(0.95f, 0.78f, 0.45f);
        }

        /// <summary>按 Tiny 档量一段提示文字的高度（量完恢复原字体）。右区内容高必须在开滚动区之前定死，所以只能先量后画。</summary>
        private static float TinyHintHeight(string text, float width)
        {
            GameFont prev = Text.Font;
            Text.Font = GameFont.Tiny;
            float h = Text.CalcHeight(text, width);
            Text.Font = prev;
            return h;
        }

        /// <summary>
        /// 材料列的行内文本：「料名 ×合计」，各材料之间用语言文件里的分隔符（中文「、」）衔接。
        /// 行高固定、不能多行展开，所以装不下就 Truncate，完整清单交给 tooltip。
        /// </summary>
        private static string MaterialListText(GroupStat gs)
        {
            // 「没统计到」与「这组没有材料清单」是两回事，后者对重新安装单是正常状态（T-078）
            if (gs == null) return "BM_CellNotCountedTip".Translate().ToString();
            if (gs.mats == null || gs.mats.Count == 0) return "BM_CellNoMaterialList".Translate().ToString();
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < gs.mats.Count; i++)
            {
                if (i > 0) sb.Append("BM_ListSeparator".Translate().ToString());
                sb.Append(gs.mats[i].Key.label).Append(" ×").Append(gs.mats[i].Value);
            }
            return sb.ToString();
        }

        /// <summary>材料列的 tooltip：一种材料一行、带合计，列宽不够时仍能看全。</summary>
        private static string MaterialListTooltip(GroupStat gs)
        {
            // 与列内文本同一句口径，不再一处「—」一处「（无）」（T-078）
            if (gs == null) return "BM_CellNotCountedTip".Translate().ToString();
            if (gs.mats == null || gs.mats.Count == 0) return "BM_CellNoMaterialList".Translate().ToString();
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < gs.mats.Count; i++)
            {
                if (i > 0) sb.Append('\n');
                sb.Append(gs.mats[i].Key.label).Append(" ×").Append(gs.mats[i].Value);
            }
            return sb.ToString();
        }

        /// <summary>右栏的一行「标签 — 值」。</summary>
        private static float DrawKV(Rect inner, float y, string labelKey, string value, Color? tint)
        {
            GUI.color = new Color(0.7f, 0.76f, 0.82f);
            Widgets.Label(new Rect(inner.x, y, inner.width * 0.5f, 20f), labelKey.Translate());
            Text.Anchor = TextAnchor.UpperRight;
            if (tint.HasValue) GUI.color = tint.Value;
            Widgets.Label(new Rect(inner.x + inner.width * 0.5f, y, inner.width * 0.5f, 20f), value);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            return y + 20f;
        }

        /// <summary>右栏未选中时的五类结论汇总卡（原来挤在诊断页顶部一行）。</summary>
        private void DrawSummaryCards(Rect area, string hintKey)
        {
            Widgets.DrawBoxSolid(area, new Color(1f, 1f, 1f, 0.06f));
            Rect inner = area.ContractedBy(10f);
            float y = inner.y;
            Text.Font = GameFont.Small;

            string[] names =
            {
                "BM_Verdict_Granted", "BM_Verdict_Waiting", "BM_Verdict_Yielded",
                "BM_Verdict_NoWorker", "BM_Verdict_Forbidden"
            };
            Color[] tints =
            {
                new Color(0.65f, 0.9f, 0.65f), new Color(0.95f, 0.8f, 0.45f), new Color(0.75f, 0.8f, 0.95f),
                new Color(0.95f, 0.6f, 0.6f), new Color(0.7f, 0.7f, 0.7f)
            };
            float h = Mathf.Min(28f, (inner.height - 26f) / names.Length);
            for (int i = 0; i < names.Length; i++)
            {
                GUI.color = tints[i];
                Widgets.Label(new Rect(inner.x, y, inner.width - 56f, h), names[i].Translate().ToString());
                Text.Anchor = TextAnchor.UpperRight;
                Widgets.Label(new Rect(inner.xMax - 56f, y, 56f, h), verdictCounts[i].ToString());
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                y += h;
            }
            if (hintKey != null)
            {
                y += 4f;
                GUI.color = new Color(0.66f, 0.71f, 0.77f);
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(inner.x, y, inner.width, 32f), hintKey.Translate());
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
            }
        }

        private static Texture2D rangeHandleTex;
        private static bool rangeHandleTried;
        private static int rangeDragId;
        private static bool rangeDragMin;
        private static string rangeDragTypeKey;
        private static IntRange rangeDragStart;
        private static bool rangeDragStartValid;

        /// <summary>
        /// 双端范围滑条：外观与操作完全沿用原版（同一张手柄贴图、同样的轨道画法），
        /// 只把「0 - 20」那行居中文本换成轨道两端的独立数字——
        /// 原版的标签位置是写死的，参数只能换文本不能换位置，所以这一段自己画。
        /// </summary>
        private static bool DrawRangeBar(Rect rect, int id, string typeKey, ref IntRange range, int min, int max)
        {
            // 版式：[数字][间隙][手柄][—— 轨道 ——][手柄][间隙][数字]
            // 手柄贴图是从锚点向外伸 16px 的，数字必须留在手柄外侧，否则会被贴图盖住
            const float NumW = 20f;
            const float HandleW = 16f;
            const float Gap = 2f;
            float pad = NumW + Gap + HandleW;

            if (!rangeHandleTried)
            {
                rangeHandleTried = true;
                rangeHandleTex = ContentFinder<Texture2D>.Get("UI/Widgets/RangeSlider", false);
            }

            float trackX = rect.x + pad;
            float trackW = Mathf.Max(40f, rect.width - pad * 2f);
            float trackEnd = trackX + trackW;
            float midY = rect.y + rect.height * 0.5f;

            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(new Rect(rect.x, rect.y, NumW, rect.height), range.min.ToString());
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(trackEnd + HandleW + Gap, rect.y, NumW, rect.height), range.max.ToString());
            Text.Anchor = TextAnchor.UpperLeft;

            float span = Mathf.Max(1f, max - min);
            float px0 = trackX + trackW * (range.min - min) / span;
            float px1 = trackX + trackW * (range.max - min) / span;

            // 以下绘制与 Widgets.IntRange 逐行对应：灰底轨道（2px）+ 白色选中段（4px）+ 原生手柄贴图
            Color oldColor = GUI.color;
            GUI.color = new Color(0.6f, 0.6f, 0.6f);
            GUI.DrawTexture(new Rect(trackX, midY - 1f, trackW, 2f), BaseContent.WhiteTex);
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(px0, midY - 2f, Mathf.Max(1f, px1 - px0), 4f), BaseContent.WhiteTex);
            if (rangeHandleTex != null)
            {
                // 左手柄画在锚点左侧，右手柄用负宽度翻转（与原版一致）
                GUI.DrawTexture(new Rect(px0 - 16f, midY - 8f, 16f, 16f), rangeHandleTex);
                GUI.DrawTexture(new Rect(px1 + 16f, midY - 8f, -16f, 16f), rangeHandleTex);
            }
            GUI.color = oldColor;

            // 命中区按行高放大：贴图只有 16px，手指按不准会以为滑条点不动
            Rect h0 = new Rect(px0 - 18f, rect.y, 22f, rect.height);
            Rect h1 = new Rect(px1 - 2f, rect.y, 22f, rect.height);

            bool changed = false;
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                Vector2 m = Event.current.mousePosition;
                bool onMin = h0.Contains(m);
                bool onMax = h1.Contains(m);
                if (onMin || onMax)
                {
                    rangeDragId = id;
                    rangeDragMin = onMin;      // 两端重合时优先拖下限
                    // 记下这一组的 key 与拖动起点：没松手就关窗（ESC / 切一级菜单）时用它回滚（T-081）
                    rangeDragTypeKey = typeKey;
                    rangeDragStart = range;    // IntRange 是结构体，赋值即拷贝
                    rangeDragStartValid = true;
                    Event.current.Use();
                }
            }
            // 只有真正拖动才改值：MouseDown 负责「命中手柄进入拖拽」，否则点一下就写一次设置
            if (rangeDragId == id && Event.current.type == EventType.MouseDrag)
            {
                float t = Mathf.Clamp01((Event.current.mousePosition.x - trackX) / trackW);
                int v = Mathf.RoundToInt(min + t * span);
                if (rangeDragMin)
                {
                    int nv = Mathf.Min(v, range.max);
                    if (nv != range.min) { range.min = nv; changed = true; }
                }
                else
                {
                    int nv = Mathf.Max(v, range.min);
                    if (nv != range.max) { range.max = nv; changed = true; }
                }
                Event.current.Use();
            }
            // 松手 = 确认：起点作废，PreClose 不再回滚（在窗外松手同样算确认，T-081 口径）
            if (Event.current.rawType == EventType.MouseUp) { rangeDragId = 0; rangeDragStartValid = false; }
            // 兜底：在窗外/别的窗口松手时收不到 MouseUp，残留的 id 会让下一次点击静默改值
            if (!Input.GetMouseButton(0)) { rangeDragId = 0; rangeDragStartValid = false; }

            return changed;
        }

        private static void MoveBuildGroup(List<KeyValuePair<string, List<Thing>>> list, string key, int targetIndex)
        {
            if (list == null || string.IsNullOrEmpty(key)) return;
            var keys = new List<string>(list.Count);
            for (int i = 0; i < list.Count; i++) keys.Add(list[i].Key);
            int from = keys.IndexOf(key);
            if (from < 0) return;
            keys.RemoveAt(from);
            int to = Mathf.Clamp(targetIndex, 0, keys.Count);
            keys.Insert(to, key);
            BuildOrderComp comp = BuildOrderComp.Instance;
            if (comp == null) return;

            // 当前没有待建、但还在 30 日名次保留期内的旧键要原样带回去：
            // SetOrder 是整体替换，漏了它们等于拖动一次就把那些类型的名次清到队尾
            if (comp.order != null && comp.order.Count > 0)
            {
                var seen = new HashSet<string>(keys);
                for (int i = 0; i < comp.order.Count; i++)
                {
                    string stale = comp.order[i];
                    if (string.IsNullOrEmpty(stale) || !seen.Add(stale)) continue;
                    int at = -1;
                    for (int j = i + 1; j < comp.order.Count; j++)
                    {
                        string after = comp.order[j];
                        if (string.IsNullOrEmpty(after)) continue;
                        at = keys.IndexOf(after);
                        if (at >= 0) break;
                    }
                    if (at >= 0) keys.Insert(at, stale);
                    else keys.Add(stale);
                }
            }
            comp.SetOrder(keys);
        }

        /// <summary>列出当前符合「权限 AND 等级范围 AND 允许执行白名单」的小人，用于工具提示。</summary>
        private string EligiblePawnsText(BuildKind groupKind, IntRange range, string typeKey)
        {
            if (pawns.Count == 0) return "BM_None".Translate().ToString();
            var sb = new System.Text.StringBuilder();
            int n = 0;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                int lvl = BuildSkillRangeComp.LevelOf(p);
                bool inRange = lvl >= range.min && lvl <= range.max;
                bool perm = BuildPermsComp.Allowed(p, groupKind);
                bool allowed = BuildWorkerComp.AllowsKey(p, typeKey);
                if (!inRange || !perm || !allowed) continue;
                if (n > 0) sb.Append("BM_ListSeparator".Translate().ToString());
                sb.Append(p.LabelShort).Append("(").Append(lvl).Append(")");
                n++;
            }
            return n == 0 ? "BM_None".Translate().ToString() : sb.ToString();
        }

        /// <summary>
        /// 白名单状态（T-072）：返回名单内「可建造」的人数，并给出逐人说明的 tooltip 文本。
        /// 判定与完工闸门 ShouldBlockFinish 同一套（权限 AND 等级 AND 名单），不另写一套口径；
        /// 名单里可能有没启用建造的人，所以遍历全体殖民者而不是只遍历「建造小人」列表。
        /// </summary>
        private static int WorkersListSummary(Thing rep, string typeKey, BuildWorkerComp wcomp, out string tooltip)
        {
            tooltip = null;
            Map map = Find.CurrentMap;
            if (rep == null || wcomp == null || map == null || map.mapPawns == null) return 0;
            List<Pawn> colonists = map.mapPawns.FreeColonists;
            if (colonists == null) return 0;

            BuildSkillRangeComp comp = BuildSkillRangeComp.Instance;
            IntRange r = comp != null ? comp.Get(typeKey) : new IntRange(0, 20);
            var sb = new System.Text.StringBuilder();
            int can = 0;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn p = colonists[i];
                if (p == null || !wcomp.Contains(typeKey, p.thingIDNumber)) continue;
                int lvl = BuildSkillRangeComp.LevelOf(p);
                string why;
                if (lvl < 0) why = "BM_WorkersSkillDisabled".Translate().ToString();
                else if (!BuildPermsComp.Allowed(p, rep)) why = "BM_WorkersNoPerm".Translate().ToString();
                else if (!BuildSkillRangeComp.Allows(p, rep)) why = "BM_WorkersLevelOut".Translate(r.min, r.max).ToString();
                else { can++; why = "BM_WorkersCanBuild".Translate().ToString(); }
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(p.LabelShort).Append("（建造 ").Append(lvl < 0 ? "—" : lvl.ToString()).Append("）· ").Append(why);
            }
            tooltip = sb.Length == 0 ? null : sb.ToString();
            return can;
        }

        /// <summary>白名单里已选小人的名字，供「允许执行」列的 tooltip 用。</summary>
        private string SelectedWorkersText(string typeKey)
        {
            BuildWorkerComp comp = BuildWorkerComp.Instance;
            if (comp == null || !comp.IsRestricted(typeKey)) return null;
            var sb = new System.Text.StringBuilder();
            int n = 0;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p == null || !comp.Contains(typeKey, p.thingIDNumber)) continue;
                if (n > 0) sb.Append("BM_ListSeparator".Translate().ToString());
                sb.Append(p.LabelShort).Append("(").Append(BuildSkillRangeComp.LevelOf(p)).Append(")");
                n++;
            }
            // 名单里可能有不在「启用建造」列表中的小人（之后被关掉建造但仍留在名单里）
            int total = comp.Count(typeKey);
            if (n < total) sb.Append("BM_WorkersOffList".Translate(total - n));
            return n == 0 && total == 0 ? null : sb.ToString();
        }

        /// <summary>
        /// 行高决定字体档位：22~31 用 Small，≥32 用 Medium（D1）。
        /// 只有两档可选（GameFont 就 Tiny/Small/Medium），所以是阈值而不是连续缩放。
        /// </summary>
        private static GameFont FontForRowHeight(float rowH)
        {
            return rowH >= 32f ? GameFont.Medium : GameFont.Small;
        }

        /// <summary>
        /// 类型组图标：建造页与诊断页共用，保证两页同图同色。
        /// 取图与配色全交给原版 Widgets.DefIcon（per-stuff 贴图 / uiIconMaterial / stuff 色 → 材质白 → uiIconColor）。
        /// </summary>
        private static void DrawGroupIcon(Thing rep, Rect r, ThingDef stuffOverride)
        {
            if (rep == null) return;
            BuildableDef entityDef = BuildUtil.EntityOf(rep);
            Def iconDef = entityDef != null ? (Def)entityDef : rep.def;
            ThingDef iconStuff = null;
            if (entityDef != null && entityDef.MadeFromStuff)
            {
                // 优先用调用方给的「与材料列同源」的那根料，没有就退回这一单实际用的 stuff
                ThingDef cand = stuffOverride;
                if (cand != null && cand.stuffProps != null) iconStuff = cand;
                else iconStuff = BuildUtil.StuffOf(rep);
                // GetColorForStuff 直接读 stuff.stuffProps.color，非 stuff 必须挡掉
                if (iconStuff != null && iconStuff.stuffProps == null) iconStuff = null;
            }
            // scale 用 1f 而非建造菜单的 0.85f：rect 已按行高算成图标格，再乘 0.85 会糊
            Widgets.DefIcon(r, iconDef, iconStuff, 1f, null, false, null, null);
        }

        /// <summary>格宽 = 最长的「名字（等级）」实测 + 内边距，不小于 PawnCellMinW。量宽与绘制同档字体。</summary>
        private static float MeasurePawnCellWidth(List<Pawn> pawns, bool includeAllCell, string allLabel)
        {
            GameFont prev = Text.Font;
            Text.Font = GameFont.Small;
            float cellW = PawnCellMinW;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p == null) continue;
                float w = Text.CalcSize(PawnCellLabel(p)).x + PawnCellPad;
                if (w > cellW) cellW = w;
            }
            if (includeAllCell && !string.IsNullOrEmpty(allLabel))
            {
                float wAll = Text.CalcSize(allLabel).x + PawnCellPad;
                if (wAll > cellW) cellW = wAll;
            }
            Text.Font = prev;
            return cellW;
        }

        /// <summary>
        /// 网格排布：列数由可用宽推，行数由格数推。
        /// 需要内嵌滚动时内容宽要再减 20（竖向滚动条），**列数随之变少、行数随之变多**，
        /// 所以高度必须按收窄后的列数重算一遍——只算一次会让最后一行落在 viewRect 之外被裁掉。
        /// </summary>
        private static void ComputePawnGrid(float avail, int cells, float cellW,
            out float contentW, out int cols, out float gridH, out bool needScroll)
        {
            contentW = avail;
            cols = Mathf.Max(1, Mathf.FloorToInt((avail + PawnCellGap) / (cellW + PawnCellGap)));
            gridH = PawnGridRows(cells, cols) * (PawnCellH + PawnCellGap) - PawnCellGap;
            needScroll = gridH > PawnGridMaxH;
            if (!needScroll) return;

            contentW = avail - 20f;
            cols = Mathf.Max(1, Mathf.FloorToInt((contentW + PawnCellGap) / (cellW + PawnCellGap)));
            gridH = PawnGridRows(cells, cols) * (PawnCellH + PawnCellGap) - PawnCellGap;
        }

        private static int PawnGridRows(int cells, int cols)
        {
            return Mathf.Max(1, Mathf.CeilToInt(cells / (float)cols));
        }

        /// <summary>
        /// 预估网格占用高度。contentH 必须在 AdjustRectsForScrollView 之前定下来，而列数又要宽度——
        /// 所以调用方传「收窄后的保守宽」(table.width − 20)：出滚动条时正好等于实际宽，
        /// 不出时最多保守 20px，结果是行数可能多算一行、高度偏大一格，不会把网格裁掉。
        /// </summary>
        private static float PawnGridHeight(List<Pawn> pawns, float avail, bool includeAllCell, string allLabel)
        {
            float cellW = MeasurePawnCellWidth(pawns, includeAllCell, allLabel);
            int cells = pawns.Count + (includeAllCell ? 1 : 0);
            ComputePawnGrid(avail, cells, cellW, out _, out _, out float gridH, out bool needScroll);
            return (needScroll ? PawnGridMaxH : gridH) + 4f;
        }

        /// <summary>
        /// 小人网格：多选（白名单）或单选（执行者视角）共用，差别只在 isSelected/onToggle 与是否带「全部」格。
        /// 格宽按最长的「名字（等级）」实测，列数按可用宽推，超过 PawnGridMaxH 内嵌滚动。返回占用的高度。
        /// </summary>
        private static float DrawPawnGrid(Rect area, List<Pawn> pawns, Func<Pawn, bool> isSelected,
            Action<Pawn> onToggle, bool includeAllCell, string allLabel, bool allSelected, Action onAll,
            ref Vector2 scroll)
        {
            float avail = area.width;
            if (avail <= 20f || pawns == null) return 0f;

            float cellW = MeasurePawnCellWidth(pawns, includeAllCell, allLabel);
            int cells = pawns.Count + (includeAllCell ? 1 : 0);
            ComputePawnGrid(avail, cells, cellW, out float contentW, out int cols, out float gridH, out bool needScroll);
            float viewH = needScroll ? PawnGridMaxH : gridH;

            Rect outRect = new Rect(area.x, area.y, avail, viewH);
            Rect viewRect = new Rect(0f, 0f, contentW, gridH);

            if (needScroll) Widgets.BeginScrollView(outRect, ref scroll, viewRect);
            else GUI.BeginGroup(new Rect(area.x, area.y, avail, viewH));

            float x0 = 0f, y0 = 0f;
            for (int i = 0; i < cells; i++)
            {
                int col = i % cols;
                int row = i / cols;
                Rect cell = new Rect(x0 + col * (cellW + PawnCellGap), y0 + row * (PawnCellH + PawnCellGap), cellW, PawnCellH);

                string label;
                Pawn p = null;
                bool isAll = includeAllCell && i == 0;
                if (isAll) label = allLabel;
                else { p = pawns[includeAllCell ? i - 1 : i]; label = PawnCellLabel(p); }

                bool sel = isAll ? allSelected : (p != null && isSelected != null && isSelected(p));
                if (sel)
                {
                    // 先铺底色再压原版 RectHighlight 纹理（橙色），文字画在上面才不会被高亮糊掉
                    Widgets.DrawBoxSolid(cell, new Color(1f, 1f, 1f, 0.12f));
                    Widgets.DrawStrongHighlight(cell, ColorLibrary.Orange);
                }
                else
                {
                    Widgets.DrawHighlightIfMouseover(cell);
                }

                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(cell, label.Truncate(cellW - 6f, null));
                Text.Anchor = TextAnchor.UpperLeft;

                if (Widgets.ButtonInvisible(cell))
                {
                    if (isAll) { if (onAll != null) onAll(); }
                    else if (onToggle != null && p != null) onToggle(p);
                }
            }

            if (needScroll) Widgets.EndScrollView();
            else GUI.EndGroup();

            return viewH + 4f;
        }

        private static string PawnCellLabel(Pawn p)
        {
            if (p == null) return "?";
            return p.LabelShort + "(" + BuildSkillRangeComp.LevelOf(p) + ")";
        }

        // ---------------- 诊断页签 ----------------

        /// <summary>
        /// 把「这张单为什么不动」摆出来。数据全部来自 SchedulingDiagnostics（判定时顺手记下的），
        /// 本页签只读不改：这里点什么都不会改变调度结果。
        /// </summary>
        private void DrawDiagTab(Rect area)
        {
            Map map = Find.CurrentMap;
            OrderRegistry registry = map != null ? OrderRegistry.Of(map) : null;
            float rowH = BuildOrderComp.RowHeight;
            PaneSplit split = SplitPane(area, rowH);

            int rows = diagGroups.Count;
            // 搜索框在诊断页同样生效（与建造页一致）：先数出真正要画的行，滚动区高度才不会虚胖
            bool filtering = SearchActive;
            if (filtering)
            {
                int shown = 0;
                for (int i = 0; i < rows; i++)
                {
                    if (MatchesSearch(diagGroups[i].label)) shown++;
                }
                rows = shown;
            }
            // 顶部固定块：提示 26 + 规模 28 + 视角行 26 + 表头 22；选中某个小人时多一行「最该做」；
            // 展开执行者视角网格时再加一段网格高（宽度用保守值估，见 PawnGridHeight 注释）
            string viewerAllShort = "BM_DiagViewerAllShort".Translate().ToString();
            float viewerGridH = viewerExpanded
                ? PawnGridHeight(pawns, split.table.width - 20f, true, viewerAllShort)
                : 0f;
            float headH = 102f + (ViewerPawn != null ? 24f : 0f) + viewerGridH;
            float contentH = headH + rows * rowH + 40f;

            // 与建造页同一套：真实内容高 → AdjustRectsForScrollView → 列坐标基于扣掉滚动条后的宽
            Rect diagOut = split.table;
            Rect view = new Rect(0f, 0f, diagOut.width, contentH);
            Widgets.AdjustRectsForScrollView(split.table, ref diagOut, ref view);
            float w = view.width;
            Widgets.BeginScrollView(diagOut, ref diagScroll, view);

            Text.Font = GameFont.Small;
            float y = 0f;

            GUI.color = new Color(0.75f, 0.8f, 0.85f);
            Widgets.Label(new Rect(0f, y, w, 22f), "BM_DiagHint".Translate());
            GUI.color = Color.white;
            y += 26f;

            if (registry == null)
            {
                Widgets.Label(new Rect(4f, y, w - 8f, 22f), "BM_DiagEmpty".Translate());
                Widgets.EndScrollView();
                return;
            }

            // 五类结论的汇总搬到右栏的汇总卡，顶部只留规模信息
            GUI.color = new Color(0.7f, 0.76f, 0.82f);
            Widgets.Label(new Rect(0f, y, w, 22f),
                "BM_DiagGroupSummary".Translate(rows, registry.orders.Count));
            GUI.color = Color.white;
            y += 28f;

            // 执行者视角：按钮改成「点开列表选」，展开后在下方内联单选网格（不再轮换）
            Pawn viewer = ViewerPawn;
            string viewerName = viewer != null ? viewer.LabelShort : "BM_DiagViewerAll".Translate().ToString();
            Widgets.Label(new Rect(0f, y, 110f, 22f), "BM_DiagViewerLabel".Translate());
            if (Widgets.ButtonText(new Rect(110f, y + 1f, 180f, 20f), viewerName))
            {
                viewerExpanded = !viewerExpanded;
                viewerScroll = Vector2.zero;
            }
            y += 26f;

            if (viewerExpanded)
            {
                y += DrawPawnGrid(new Rect(0f, y, w, PawnGridMaxH), pawns,
                    p => p != null && diagViewer == pawns.IndexOf(p) + 1,
                    p =>
                    {
                        int idx = pawns.IndexOf(p);
                        if (idx < 0) return;
                        diagViewer = idx + 1;
                        RefreshViewerBest();
                    },
                    true, viewerAllShort, diagViewer <= 0,
                    () =>
                    {
                        diagViewer = 0;
                        RefreshViewerBest();
                    },
                    ref viewerScroll);
            }

            if (viewer != null)
            {
                GUI.color = new Color(0.85f, 0.9f, 0.95f);
                string bestLine;
                if (!diagBestComplete) bestLine = "BM_DiagBestIncomplete".Translate().ToString();
                else if (diagBestThing == null) bestLine = "BM_DiagBestNone".Translate(viewer.LabelShort).ToString();
                else bestLine = "BM_DiagBestFor".Translate(viewer.LabelShort,
                        BuildUtil.BaseLabelOf(diagBestThing), diagBestRank).ToString();
                Widgets.Label(new Rect(8f, y, w - 8f, 22f), bestLine);
                GUI.color = Color.white;
                y += 24f;
            }

            // 固定列从右往左排，剩下的宽度按 45/55 分给「类型组」与「原因」——
            // 这两列本来就要装长文本，宽屏加宽的是它们，数字列不会被拉开
            float xJump2 = w - WJump2 - JumpRightPad;
            float xWait = xJump2 - WWait - ColGap;
            float xOrders = xWait - WOrders - ColGap;
            float xState = xOrders - WState - ColGap;
            float xRank = xState - WRank - ColGap;
            float leftW = xRank - ColGap;
            // 类型组列前面让出图标位（与建造页同一位置关系：间距 4 + 图标 + 间距 4，图标随行高定比）
            float xGroup = DiagLeadW(rowH);
            float textArea = Mathf.Max(2f * TextMinW, leftW - xGroup);
            float wGroup = Mathf.Max(TextMinW, textArea * 0.45f);
            float xReason = xGroup + wGroup + ColGap;
            float wReason = Mathf.Max(TextMinW, textArea - wGroup - ColGap);
            float tableW = w;

            Widgets.DrawBoxSolid(new Rect(0f, y, tableW, 20f), new Color(1f, 1f, 1f, 0.1f));
            GUI.color = new Color(0.8f, 0.85f, 0.9f);
            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(new Rect(xGroup, y + 1f, wGroup, 20f), "BM_DiagColGroup".Translate());
            Widgets.Label(new Rect(xRank, y + 1f, WRank, 20f), "BM_DiagColRank".Translate());
            Widgets.Label(new Rect(xState, y + 1f, WState, 20f), "BM_DiagColState".Translate());
            Widgets.Label(new Rect(xReason, y + 1f, wReason, 20f), "BM_DiagColReason".Translate());
            Widgets.Label(new Rect(xOrders, y + 1f, WOrders, 20f), "BM_DiagColCount".Translate());
            Widgets.Label(new Rect(xWait, y + 1f, WWait, 20f), "BM_DiagColWait".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            y += 22f;

            if (rows == 0) Widgets.Label(new Rect(xGroup, y, tableW - 8f, 22f), "BM_DiagEmpty".Translate());

            // 数据行按行高档位取字体；顶部固定块（提示·规模·视角·最该做·表头）保持 Small 不动（CR-134）
            Text.Font = FontForRowHeight(rowH);
            float diagTextH = Text.LineHeight;
            float diagIconSize = IconSize(rowH);   // 与建造页同一比例（24 档 = 18px）
            float diagCtrlH = Mathf.Clamp(diagTextH + 2f, 20f, rowH);   // 跳转按钮高度随字体（T-070，与建造页同一套）

            // 只画可视区内的行；上下沿取 Adjust 之后的真实视口（CR-132），±rowH 是预绘余量
            float viewTop = diagScroll.y + (diagOut.y - split.table.y) - rowH;
            float viewBottom = diagScroll.y + (diagOut.yMax - split.table.y) + rowH;

            int drawn = 0;
            for (int i = 0; i < diagGroups.Count; i++)
            {
                DiagGroup g = diagGroups[i];
                if (filtering && !MatchesSearch(g.label)) continue;
                if (y + rowH < viewTop) { y += rowH; drawn++; continue; }
                if (y > viewBottom) break;

                Rect row = new Rect(0f, y, tableW, rowH);
                // 行高可变：文字与按钮的纵向位置都从 rowH 推，文字格高用当前档位真实行高
                float textY = y + Mathf.Max(0f, (rowH - diagTextH) * 0.5f);
                if (drawn % 2 == 0) Widgets.DrawAltRect(row);
                drawn++;
                if (selGroup == g.key) Widgets.DrawHighlightSelected(row);

                // 类型组图标：与建造页共用 DrawGroupIcon，stuff 只能取这一单实际的材质
                // （诊断页没有 GroupStat，拿不到「组内用量最大的那根料」）
                DrawGroupIcon(g.rep, new Rect(4f, y + (rowH - diagIconSize) * 0.5f, diagIconSize, diagIconSize), null);

                // 类型组内容保持左对齐，其余列内容列内居中
                Widgets.Label(new Rect(xGroup, textY, wGroup, diagTextH), g.label.Truncate(wGroup, null));
                Text.Anchor = TextAnchor.UpperCenter;

                bool aged = g.minEffRank < g.minRank;
                GUI.color = aged ? new Color(1f, 0.85f, 0.5f) : Color.white;
                Widgets.Label(new Rect(xRank, textY, WRank, diagTextH),
                    aged ? g.minEffRank + "↑" : g.minEffRank.ToString());
                GUI.color = Color.white;

                OrderVerdict v;
                SchedulingDiagnostics.TryGet(g.worstOrderId, out v);

                GUI.color = VerdictColor(v);
                Widgets.Label(new Rect(xState, textY, WState, diagTextH), VerdictLabel(v));
                GUI.color = Color.white;

                Widgets.Label(new Rect(xReason, textY, wReason, diagTextH), ReasonText(v).Truncate(wReason, null));

                Widgets.Label(new Rect(xOrders, textY, WOrders, diagTextH), g.count.ToString());
                Widgets.Label(new Rect(xWait, textY, WWait, diagTextH), WaitText(g.maxWait));
                Text.Anchor = TextAnchor.UpperLeft;

                if (Widgets.ButtonText(new Rect(xJump2, y + (rowH - diagCtrlH) * 0.5f, WJump2, diagCtrlH), "BM_Jump".Translate()))
                {
                    JumpTo(g.rep);
                }

                Rect pickRect = new Rect(xGroup, y, wGroup, rowH);
                if (Widgets.ButtonInvisible(pickRect))
                {
                    selGroup = selGroup == g.key ? null : g.key;
                }
                y += rowH;
            }

            Widgets.EndScrollView();
            Text.Font = GameFont.Small;   // 同建造页：出表格滚动区就复位字体，别让 Medium 漏进 tooltip（CR-154）

            DrawDiagRightPane(split.right, registry);
        }

        /// <summary>诊断页右栏：选中组 = 完整判定与算式；未选中 = 五类结论汇总卡。</summary>
        private void DrawDiagRightPane(Rect area, OrderRegistry registry)
        {
            DiagGroup g = null;
            if (selGroup != null) diagGroupMap.TryGetValue(selGroup, out g);
            if (g == null)
            {
                DrawSummaryCards(area, "BM_PanePickHint");
                return;
            }

            OrderVerdict v;
            SchedulingDiagnostics.TryGet(g.worstOrderId, out v);

            Widgets.DrawBoxSolid(area, new Color(1f, 1f, 1f, 0.06f));
            Rect inner = area.ContractedBy(10f);
            float y = inner.y;
            Text.Font = GameFont.Small;

            Widgets.Label(new Rect(inner.x, y, inner.width, 24f), g.label.Truncate(inner.width, null));
            y += 24f;
            if (Widgets.ButtonText(new Rect(inner.x, y, 64f, 20f), "BM_Close".Translate())) selGroup = null;
            if (Widgets.ButtonText(new Rect(inner.xMax - 64f, y, 64f, 20f), "BM_Jump".Translate())) JumpTo(g.rep);
            y += 28f;

            y = DrawKV(inner, y, "BM_DiagColState", VerdictLabel(v), VerdictColor(v));
            y = DrawKV(inner, y, "BM_DiagColJudge", JudgeLabel(v), null);
            y = DrawKV(inner, y, "BM_DiagColRank", g.minRank + " / " + g.minEffRank,
                g.minEffRank < g.minRank ? new Color(1f, 0.85f, 0.5f) : (Color?)null);
            y = DrawKV(inner, y, "BM_DiagColCount", g.count.ToString(), null);
            y = DrawKV(inner, y, "BM_DiagColWait", WaitText(g.maxWait), null);

            if (v != null && v.code == VerdictCode.WaitingMaterial && v.def != null)
            {
                y += 6f;
                GUI.color = new Color(0.7f, 0.76f, 0.82f);
                Widgets.Label(new Rect(inner.x, y, inner.width, 20f), "BM_DiagFormula".Translate());
                GUI.color = Color.white;
                y += 20f;
                y = DrawKV(inner, y, "BM_DiagKvNeed", v.def.label + " ×" + v.need, null);
                y = DrawKV(inner, y, "BM_DiagKvStock", v.stock.ToString(), null);
                y = DrawKV(inner, y, "BM_DiagKvEnroute", v.enroute.ToString(), null);
                y = DrawKV(inner, y, "BM_DiagKvProtect", v.protect.ToString(), new Color(0.95f, 0.8f, 0.45f));
                y = DrawKV(inner, y, "BM_DiagKvAlloc", v.alloc.ToString(), null);
                y = DrawKV(inner, y, "BM_DiagKvAvail", v.avail.ToString(),
                    v.avail < v.need ? new Color(0.95f, 0.6f, 0.6f) : new Color(0.65f, 0.9f, 0.65f));
                y = DrawKV(inner, y, "BM_DiagKvKind",
                    (v.permanent ? "BM_DiagPermanent" : "BM_DiagTemporary").Translate().ToString(), null);
            }
            else if (v != null && v.code == VerdictCode.Yielded)
            {
                y += 6f;
                y = DrawKV(inner, y, "BM_DiagKvYieldTo",
                    v.byThing != null ? BuildUtil.BaseLabelOf(v.byThing) : "?", null);
                y = DrawKV(inner, y, "BM_DiagColRank", v.byRank.ToString(), null);
            }
            else if (v != null && v.code == VerdictCode.NoWorker)
            {
                y += 6f;
                GUI.color = new Color(0.95f, 0.6f, 0.6f);
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(inner.x, y, inner.width, 40f), "BM_DiagReasonNoWorker".Translate());
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
            }
        }

        private static string JudgeLabel(OrderVerdict v)
        {
            if (v == null) return "—";
            switch (v.judge)
            {
                case JudgeKind.Budget: return "BM_Judge_Budget".Translate().ToString();
                case JudgeKind.Gate: return "BM_Judge_Gate".Translate().ToString();
                case JudgeKind.NoWorker: return "BM_Judge_NoWorker".Translate().ToString();
                case JudgeKind.Player: return "BM_Judge_Player".Translate().ToString();
                default: return "—";
            }
        }

        private static string VerdictLabel(OrderVerdict v)
        {
            if (v == null) return "BM_Verdict_Unknown".Translate().ToString();
            switch (v.code)
            {
                case VerdictCode.Granted: return "BM_Verdict_Granted".Translate().ToString();
                case VerdictCode.WaitingMaterial: return "BM_Verdict_Waiting".Translate().ToString();
                case VerdictCode.Yielded: return "BM_Verdict_Yielded".Translate().ToString();
                case VerdictCode.NoWorker: return "BM_Verdict_NoWorker".Translate().ToString();
                default: return "BM_Verdict_Forbidden".Translate().ToString();
            }
        }

        private static Color VerdictColor(OrderVerdict v)
        {
            if (v == null) return new Color(0.6f, 0.6f, 0.6f);
            switch (v.code)
            {
                case VerdictCode.Granted: return new Color(0.65f, 0.9f, 0.65f);
                case VerdictCode.WaitingMaterial: return new Color(0.95f, 0.8f, 0.45f);
                case VerdictCode.Yielded: return new Color(0.75f, 0.8f, 0.95f);
                case VerdictCode.NoWorker: return new Color(0.95f, 0.6f, 0.6f);
                default: return new Color(0.7f, 0.7f, 0.7f);
            }
        }

        private static string ReasonText(OrderVerdict v)
        {
            // 原因列不留「—」：每一格都要能读出一句话（用户 2026-11-05 10:36 定，T-077）
            if (v == null) return "BM_DiagReasonPending".Translate().ToString();
            switch (v.code)
            {
                case VerdictCode.WaitingMaterial:
                    if (v.def == null) return "BM_DiagReasonNoMaterial".Translate().ToString();
                    return "BM_DiagReasonShort".Translate(v.def.label, v.need - v.avail, v.avail).ToString()
                        + (v.permanent
                            ? " ·" + "BM_DiagPermanent".Translate().ToString()
                            : " ·" + "BM_DiagTemporary".Translate().ToString());
                case VerdictCode.Yielded:
                    return "BM_DiagReasonYield".Translate(
                        v.byThing != null ? BuildUtil.BaseLabelOf(v.byThing) : "?", v.byRank).ToString();
                case VerdictCode.NoWorker:
                    return "BM_DiagReasonNoWorker".Translate().ToString();
                case VerdictCode.Forbidden:
                    return "BM_DiagReasonForbidden".Translate().ToString();
                // 放行分两种：已有人被派（原版预约在）与可做但没人被派——都写成句子，不再留空
                case VerdictCode.Granted:
                    return v.reserved
                        ? "BM_DiagReasonTaken".Translate().ToString()
                        : "BM_DiagIdleReason".Translate().ToString();
                default:
                    return "BM_DiagReasonNone".Translate().ToString();
            }
        }

        private static string WaitText(int ticks)
        {
            if (ticks <= 0) return "—";
            if (ticks >= GenDate.TicksPerDay) return (ticks / (float)GenDate.TicksPerDay).ToString("0.0") + "d";
            return (ticks / 60) + "s";
        }

        private static void JumpTo(Thing t)
        {
            if (t == null || t.Map == null) return;
            Find.CameraDriver.JumpToCurrentMapLoc(t.Position);
            Find.Selector.ClearSelection();
            Find.Selector.Select(t, false, true);
        }
    }
}
