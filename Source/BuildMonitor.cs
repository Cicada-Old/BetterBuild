using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace BuildMonitor
{
    [StaticConstructorOnStartup]
    public static class Bootstrap
    {
        static Bootstrap()
        {
            try
            {
                new Harmony("Cicada.BetterBuild").PatchAll();
                Log.Message("[BM] ready");
            }
            catch (Exception e)
            {
                Log.Error("[BM] init failed: " + e);
            }
        }
    }

    /// <summary>待建造物的分类：有品质 / 无品质。</summary>
    public enum BuildKind
    {
        Quality,
        Plain
    }

    public static class BuildUtil
    {
        public static BuildableDef EntityOf(Thing t)
        {
            if (t == null || t.def == null) return null;
            if (t is Blueprint bp) return bp.def.entityDefToBuild;
            if (t is Frame fr) return fr.def.entityDefToBuild;
            if (t is Blueprint_Install bi) return bi.def.entityDefToBuild;
            return null;
        }

        public static bool IsPendingBuild(Thing t)
        {
            return t is Blueprint || t is Frame;
        }

        /// <summary>成品是否会带品质（有 CompQuality 的 ThingDef）。</summary>
        public static bool HasQuality(BuildableDef b)
        {
            ThingDef td = b as ThingDef;
            if (td == null || td.comps == null) return false;
            for (int i = 0; i < td.comps.Count; i++)
            {
                CompProperties c = td.comps[i];
                if (c != null && c.compClass == typeof(CompQuality)) return true;
            }
            return false;
        }

        public static BuildKind KindOf(Thing t)
        {
            return HasQuality(EntityOf(t)) ? BuildKind.Quality : BuildKind.Plain;
        }

        /// <summary>与「未建造统计」分组一致的键：品质前缀 + 种类 defName。</summary>
        public static string GroupKey(Thing t)
        {
            string prefix = KindOf(t) == BuildKind.Quality ? "[有品质] " : "[无品质] ";
            BuildableDef entity = EntityOf(t);
            string defName = entity != null ? entity.defName : (t != null && t.def != null ? t.def.defName : "?");
            return prefix + defName;
        }

        public static string CategoryLabel(BuildKind k)
        {
            return k == BuildKind.Quality ? "有品质" : "无品质";
        }

        /// <summary>
        /// 建造类型的键：只取 defName（**不含材质**）。
        /// 拆除重建后蓝图/框架的材质可能暂时为空，带材质会让键变化、看起来像"重置了"，
        /// 所以统一按类型存，所有材质共用同一条等级范围。
        /// </summary>
        public static string TypeKey(Thing t)
        {
            BuildableDef b = EntityOf(t);
            string def = b != null ? b.defName : (t != null && t.def != null ? t.def.defName : "?");
            return NormalizeKey(def);
        }

        /// <summary>把旧存档里带材质的键（def|stuff）去掉材质部分。</summary>
        public static string NormalizeKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return key;
            int bar = key.IndexOf('|');
            return bar > 0 ? key.Substring(0, bar) : key;
        }

        /// <summary>
        /// 这一单实际用的建材：Blueprint 自身不带 stuff，要读 EntityToBuildStuff()；
        /// Frame 与成品走 Thing.Stuff（Frame.EntityToBuildStuff 本身就是返回 base.Stuff）。
        /// </summary>
        public static ThingDef StuffOf(Thing t)
        {
            if (t == null) return null;
            Blueprint bp = t as Blueprint;
            if (bp != null) return bp.EntityToBuildStuff();
            return t.Stuff;
        }

        /// <summary>不带材质前缀的「种类」名（用于统计分组，木椅/布椅算同一种）。</summary>
        public static string BaseLabelOf(Thing t)
        {
            BuildableDef b = EntityOf(t);
            if (b != null && !string.IsNullOrEmpty(b.label)) return b.label;
            return t != null && t.def != null ? t.def.label : "?";
        }

        public static string LabelOf(Thing t)
        {
            BuildableDef b = EntityOf(t);
            string baseLabel = b != null && !string.IsNullOrEmpty(b.label) ? b.label : (t.def != null ? t.def.label : "?");
            ThingDef stuff = t.Stuff;
            if (stuff != null && stuff.stuffProps != null)
            {
                string adj = stuff.stuffProps.stuffAdjective;
                if (!string.IsNullOrEmpty(adj)) return adj + baseLabel;
            }
            return baseLabel;
        }
    }

    /// <summary>每个小人的建造权限（按分类）。</summary>
    public class BuildPermsComp : GameComponent
    {
        public const int AllowQuality = 1;
        public const int AllowPlain = 2;

        public Dictionary<int, int> perms = new Dictionary<int, int>();

        // 游戏用 Activator.CreateInstance(type, game) 创建 GameComponent，
        // 必须提供带 Game 参数的构造函数（基类本身没有构造）。
        public BuildPermsComp(Game game)
        {
            Log.Message("[BM] permissions component created");
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref perms, "BuildMonitorPerms", LookMode.Value, LookMode.Value);
            if (perms == null) perms = new Dictionary<int, int>();
        }

        public static BuildPermsComp Instance
        {
            get
            {
                Game g = Current.Game;
                return g != null ? g.GetComponent<BuildPermsComp>() : null;
            }
        }

        public static int GetMask(Pawn p)
        {
            BuildPermsComp comp = Instance;
            if (comp == null || p == null) return AllowQuality | AllowPlain;
            int mask;
            if (!comp.perms.TryGetValue(p.thingIDNumber, out mask)) return AllowQuality | AllowPlain;
            return mask;
        }

        public static void SetAllowed(Pawn p, BuildKind k, bool allowed)
        {
            BuildPermsComp comp = Instance;
            if (comp == null || p == null) return;
            int mask = GetMask(p);
            int bit = k == BuildKind.Quality ? AllowQuality : AllowPlain;
            if (allowed) mask |= bit;
            else mask &= ~bit;
            comp.perms[p.thingIDNumber] = mask;
        }

        public static bool Allowed(Pawn p, Thing t)
        {
            if (t == null) return true;
            return Allowed(p, BuildUtil.KindOf(t));
        }

        public static bool Allowed(Pawn p, BuildKind k)
        {
            int mask = GetMask(p);
            return (mask & (k == BuildKind.Quality ? AllowQuality : AllowPlain)) != 0;
        }
    }

    /// <summary>某种建造类型的等级限制条目（存档用）。</summary>
    public class SkillRangeEntry : IExposable
    {
        public string key;
        public int min;
        public int max = 20;

        public void ExposeData()
        {
            Scribe_Values.Look(ref key, "key");
            Scribe_Values.Look(ref min, "min", 0);
            Scribe_Values.Look(ref max, "max", 20);
        }
    }

    /// <summary>
    /// 每种「建造类型」允许的建造等级范围（0~20）。
    /// 这是在「建造权限」（有品质/无品质）之外再加一层：只有等级在范围内的小人才能自动建造。
    /// 默认 0~20 = 不限制。
    /// </summary>
    public class BuildSkillRangeComp : GameComponent
    {
        public const int MinLevel = 0;
        public const int MaxLevel = 20;

        public List<SkillRangeEntry> entries = new List<SkillRangeEntry>();
        private Dictionary<string, IntRange> cache;

        public BuildSkillRangeComp(Game game)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref entries, "BM_SkillRanges", LookMode.Deep);
            if (entries == null) entries = new List<SkillRangeEntry>();
            cache = null;
        }

        public static BuildSkillRangeComp Instance
        {
            get
            {
                Game g = Current.Game;
                return g != null ? g.GetComponent<BuildSkillRangeComp>() : null;
            }
        }

        private Dictionary<string, IntRange> Cache
        {
            get
            {
                if (cache == null)
                {
                    cache = new Dictionary<string, IntRange>();
                    for (int i = 0; i < entries.Count; i++)
                    {
                        SkillRangeEntry e = entries[i];
                        if (e == null || string.IsNullOrEmpty(e.key)) continue;
                        string k = BuildUtil.NormalizeKey(e.key);
                        cache[k] = new IntRange(e.min, e.max);
                    }
                }
                return cache;
            }
        }

        public IntRange Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return new IntRange(MinLevel, MaxLevel);
            IntRange r;
            if (Cache.TryGetValue(key, out r)) return r;
            return new IntRange(MinLevel, MaxLevel);
        }

        public void Set(string key, IntRange range)
        {
            if (string.IsNullOrEmpty(key)) return;
            range.min = Mathf.Clamp(range.min, MinLevel, MaxLevel);
            range.max = Mathf.Clamp(range.max, MinLevel, MaxLevel);
            if (range.min > range.max) range.max = range.min;

            key = BuildUtil.NormalizeKey(key);

            SkillRangeEntry target = null;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                SkillRangeEntry e = entries[i];
                if (e == null) continue;
                if (BuildUtil.NormalizeKey(e.key) != key) continue;
                if (e.key != key) { entries.RemoveAt(i); continue; }   // 旧的 def|stuff 条目直接丢掉
                target = e;
                break;
            }
            if (target == null)
            {
                target = new SkillRangeEntry { key = key };
                entries.Add(target);
            }
            target.min = range.min;
            target.max = range.max;
            if (cache != null) cache[key] = range;
            // 等级范围改变「谁去做」，调度预演有 45 tick 缓存；不递增版本号就要等缓存自然过期
            BuildOrderComp.Version++;
        }

        public bool IsRestricted(string key)
        {
            IntRange r = Get(key);
            // 只有「0~20」是未限制；例如 20~20 表示只允许满级
            return !(r.min == MinLevel && r.max == MaxLevel);
        }

        public static int LevelOf(Pawn p)
        {
            if (p == null || p.skills == null) return -1;
            SkillRecord sk = p.skills.GetSkill(SkillDefOf.Construction);
            if (sk == null || sk.TotallyDisabled) return -1;
            return sk.Level;
        }

        /// <summary>按 key 判定（地面用地形 defName）。</summary>
        public static bool AllowsKey(Pawn p, string key)
        {
            BuildSkillRangeComp comp = Instance;
            if (comp == null || p == null || string.IsNullOrEmpty(key)) return true;
            if (!comp.IsRestricted(key)) return true;

            int lvl = LevelOf(p);
            if (lvl < 0) return false;
            IntRange r = comp.Get(key);
            return lvl >= r.min && lvl <= r.max;
        }

        /// <summary>等级是否落在该建造类型允许的范围内（未设置=允许）。</summary>
        public static bool Allows(Pawn p, Thing t)
        {
            BuildSkillRangeComp comp = Instance;
            if (comp == null || p == null || t == null) return true;

            string key = BuildUtil.TypeKey(t);
            if (!comp.IsRestricted(key)) return true;

            int lvl = LevelOf(p);
            if (lvl < 0) return false;
            IntRange r = comp.Get(key);
            return lvl >= r.min && lvl <= r.max;
        }
    }

    /// <summary>未建造统计的拖动顺序（键为品质前缀 + defName，越靠前越优先）。</summary>
    public class BuildOrderComp : GameComponent
    {
        public List<string> order = new List<string>();
        public Dictionary<string, int> seenTicks = new Dictionary<string, int>();

        /// <summary>
        /// 材料保护期（游戏日，默认 1）：最高名次组占着的材料，保护这么久之后开放一半给低名次借用；
        /// 0 = 不保护（等于一直开放一半）。改动会立即让预算重建。
        /// </summary>
        public float protectDays = 1f;

        /// <summary>
        /// 等待老化期（游戏日，默认 1）：订单连续这么久没有进料，就把它的调度名次提到最高名次之后一位，
        /// 让它越过后面所有队列。0 = 关闭老化。改动会在下一次注册表刷新（≤60 tick）生效。
        /// </summary>
        public float ageDays = 1f;

        /// <summary>
        /// 建造页类型组表格的行高（GUI 像素，默认 24，范围 16~64）。
        /// 只影响中区那张表：左栏小人、工作台、诊断页的行高是各自固定的。
        /// </summary>
        public int rowHeight = 24;

        public const int RowHeightMin = 22;   // = 文字标签高度；容器不得低于内容
        public const int RowHeightMax = 64;
        public const int RowHeightDefault = 24;

        /// <summary>顺序变化时递增，用来让“当前最高可做建造”的缓存立即失效。</summary>
        public static int Version;

        public BuildOrderComp(Game game)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref order, "BuildMonitorBuildOrder", LookMode.Value);
            if (order == null) order = new List<string>();
            Scribe_Collections.Look(ref seenTicks, "BuildMonitorBuildOrderSeen", LookMode.Value);
            if (seenTicks == null) seenTicks = new Dictionary<string, int>();
            Scribe_Values.Look(ref protectDays, "BM_ProtectDays", 1f);
            if (protectDays < 0f) protectDays = 0f;
            Scribe_Values.Look(ref ageDays, "BM_AgeDays", 1f);
            if (ageDays < 0f) ageDays = 0f;
            Scribe_Values.Look(ref rowHeight, "BM_RowHeight", RowHeightDefault);
            rowHeight = ClampRowHeight(rowHeight);
        }

        public static int ClampRowHeight(int value)
        {
            if (value < RowHeightMin) return RowHeightMin;
            if (value > RowHeightMax) return RowHeightMax;
            return value;
        }

        /// <summary>界面用：当前行高（没有 comp 时退回默认值）。</summary>
        public static float RowHeight
        {
            get
            {
                BuildOrderComp comp = Instance;
                return comp != null ? ClampRowHeight(comp.rowHeight) : RowHeightDefault;
            }
        }

        /// <summary>等待老化期换算成 tick；0 = 关闭。</summary>
        public int AgeTicks
        {
            get
            {
                if (ageDays <= 0f) return 0;
                if (ageDays > 30f) ageDays = 30f;
                double ticks = ageDays * (double)GenDate.TicksPerDay;
                return ticks > int.MaxValue ? int.MaxValue : (int)ticks;
            }
        }

        public static BuildOrderComp Instance
        {
            get
            {
                Game g = Current.Game;
                return g != null ? g.GetComponent<BuildOrderComp>() : null;
            }
        }

        public int Rank(string key)
        {
            if (string.IsNullOrEmpty(key) || order == null) return int.MaxValue;
            int idx = order.IndexOf(key);
            return idx >= 0 ? idx : int.MaxValue;
        }

        public void SetOrder(List<string> keys)
        {
            order = new List<string>();
            if (keys == null) return;
            for (int i = 0; i < keys.Count; i++)
            {
                if (!string.IsNullOrEmpty(keys[i]) && !order.Contains(keys[i])) order.Add(keys[i]);
            }
            Version++;
        }
    }

    /// <summary>
    /// 建造顺序闸门：找出该小人当前能做的最高优先级建造（送料或完工），
    /// 让其它优先级更低的建造任务先让路。这样高优先级的蓝图不会一直等材料。
    /// </summary>
    public static class BuildPriorityGate
    {
        private const int CacheTicks = 45;
        private const int MaxChecksPerScan = 48;

        private struct BestAction
        {
            public int Rank;
            public Thing Thing;
            public bool Finish;
            public bool Complete;
        }

        private static readonly Dictionary<long, BestAction> cachedBestAction = new Dictionary<long, BestAction>();
        private static readonly Dictionary<long, int> cachedBestTick = new Dictionary<long, int>();
        private static int cachedVersion = -1;

        private static WorkGiver_ConstructDeliverResourcesToBlueprints blueprintsGiver;
        private static WorkGiver_ConstructDeliverResourcesToFrames framesGiver;
        private static WorkGiver_ConstructDeliverResourcesToBlueprints haulBlueprintsGiver;
        private static WorkGiver_ConstructDeliverResourcesToFrames haulFramesGiver;
        private static WorkGiver_ConstructFinishFrames finishGiver;

        // 正在替小人“预演”其它建造是否有活，期间不再递归触发本闸门
        private static bool scanning;

        private static WorkGiver_ConstructDeliverResourcesToBlueprints BlueprintsGiver
        {
            get
            {
                if (blueprintsGiver == null)
                {
                    WorkGiverDef def = DefDatabase<WorkGiverDef>.GetNamed("ConstructDeliverResourcesToBlueprints", false);
                    blueprintsGiver = def != null ? def.Worker as WorkGiver_ConstructDeliverResourcesToBlueprints : null;
                }
                return blueprintsGiver;
            }
        }

        private static WorkGiver_ConstructDeliverResourcesToFrames FramesGiver
        {
            get
            {
                if (framesGiver == null)
                {
                    WorkGiverDef def = DefDatabase<WorkGiverDef>.GetNamed("ConstructDeliverResourcesToFrames", false);
                    framesGiver = def != null ? def.Worker as WorkGiver_ConstructDeliverResourcesToFrames : null;
                }
                return framesGiver;
            }
        }

        private static WorkGiver_ConstructDeliverResourcesToBlueprints HaulBlueprintsGiver
        {
            get
            {
                if (haulBlueprintsGiver == null)
                {
                    WorkGiverDef def = DefDatabase<WorkGiverDef>.GetNamed("DeliverResourcesToBlueprints", false);
                    haulBlueprintsGiver = def != null ? def.Worker as WorkGiver_ConstructDeliverResourcesToBlueprints : null;
                }
                return haulBlueprintsGiver;
            }
        }

        private static WorkGiver_ConstructDeliverResourcesToFrames HaulFramesGiver
        {
            get
            {
                if (haulFramesGiver == null)
                {
                    WorkGiverDef def = DefDatabase<WorkGiverDef>.GetNamed("DeliverResourcesToFrames", false);
                    haulFramesGiver = def != null ? def.Worker as WorkGiver_ConstructDeliverResourcesToFrames : null;
                }
                return haulFramesGiver;
            }
        }

        private static WorkGiver_ConstructFinishFrames FinishGiver
        {
            get
            {
                if (finishGiver == null)
                {
                    finishGiver = DefDatabase<WorkGiverDef>.GetNamed("ConstructFinishFrames", false)?.Worker
                        as WorkGiver_ConstructFinishFrames;
                }
                return finishGiver;
            }
        }

        private static bool IsHaulingGiver(WorkGiver_Scanner giver)
        {
            return giver != null && giver.def != null && giver.def.workType == WorkTypeDefOf.Hauling;
        }

        /// <summary>当前目标是否该让路给更高优先级的建造（手动命令永远不让）。</summary>
        public static bool ShouldYield(Pawn pawn, Thing target, bool forced, WorkGiver_Scanner giver)
        {
            if (forced || scanning || pawn == null || target == null) return false;
            BuildOrderComp comp = BuildOrderComp.Instance;
            if (comp == null || comp.order == null || comp.order.Count == 0) return false;
            if (!BuildUtil.IsPendingBuild(target)) return false;

            int targetRank = EffRankOf(comp, target);
            BestAction best = BestActionable(pawn, comp, IsHaulingGiver(giver));
            if (!best.Complete)
            {
                // 预演达到上限、结论不完整：按放行处理，避免性能上限变成饿死
                LogIncompleteScan();
                return false;
            }
            // 名次优先：best 已经是「这个小人现在真能做的最高名次单」，只要它比目标名次高就先做它。
            // 材料冲突不再当否决条件：旧写法 `&& MaterialConflict(...)` 会让「完工」（不消耗新材料）
            // 与「重新安装」（没有材料清单）这两种情形恒得 false → 永不让路，名次第一也压不住低名次的单
            // （让路只按名次判定，与材料冲突无关；T-076）。
            bool yield = best.Rank < targetRank;
            if (yield)
            {
                int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
                SchedulingDiagnostics.RecordYield(SchedulingDiagnostics.OrderIdOf(target), best.Rank, best.Thing, now);
                if (yieldLogs < 80)
                {
                    yieldLogs++;
                    // MaterialConflict 只用来写措辞，区分「抢同一种材料」与「只是名次更高」，不参与判定
                    bool byMaterial = MaterialConflict(best, target);
                    string bestKey = best.Thing != null ? BuildUtil.GroupKey(best.Thing) : "?";
                    Log.Message("[BM] 建造让路：当前=" + BuildUtil.GroupKey(target) + "(" + targetRank +
                                ") → 更高可做=" + bestKey + "(" + best.Rank + ")" +
                                (IsHaulingGiver(giver) ? " [搬运协助]" : " [建造]") +
                                " 原因=" + (byMaterial ? "材料争用" : "名次优先"));
                }
            }
            return yield;
        }

        private static int yieldLogs;

        /// <summary>
        /// 目标当前实际参与调度的名次（含老化提权）。注册表里查得到就用订单的 EffRank，
        /// 查不到才退回类型级名次，避免"改了名次但门控还按旧名次让路"。
        /// </summary>
        private static int EffRankOf(BuildOrderComp comp, Thing t)
        {
            if (t != null)
            {
                OrderRegistry reg = OrderRegistry.Of(t.Map);
                if (reg != null)
                {
                    reg.EnsureFresh();
                    int id = t.thingIDNumber;
                    if (t is Frame frame && OrderLedger.frameToOrder.TryGetValue(id, out int carried)) id = carried;
                    OrderInfo info;
                    if (reg.orders.TryGetValue(id, out info)) return info.EffRank;
                }
            }
            return comp != null ? comp.Rank(BuildUtil.GroupKey(t)) : int.MaxValue;
        }

        private static int lastIncompleteLog = -1000000;

        private static void LogIncompleteScan()
        {
            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            // now 比上次小 = 读了一张更早的档，视为已过期（T-056 同一根因）
            if (now >= lastIncompleteLog && now - lastIncompleteLog < 600) return;
            lastIncompleteLog = now;
            Log.Message("[BM] 预演不完整：候选超过上限，本次按放行处理");
        }

        private static BestAction BestActionable(Pawn pawn, BuildOrderComp comp, bool hauling)
        {
            int now = Find.TickManager.TicksGame;
            if (cachedVersion != BuildOrderComp.Version)
            {
                cachedVersion = BuildOrderComp.Version;
                cachedBestAction.Clear();
                cachedBestTick.Clear();
            }

            // 同一小人要分别记住「建造工作」和「搬运协助」两种口径
            long key = ((long)pawn.thingIDNumber << 1) | (hauling ? 1L : 0L);
            BestAction cached;
            int cachedAt;
            if (cachedBestAction.TryGetValue(key, out cached)
                && cachedBestTick.TryGetValue(key, out cachedAt)
                && now >= cachedAt && now - cachedAt < CacheTicks)
            {
                return cached;
            }

            scanning = true;
            BestAction best = new BestAction { Rank = int.MaxValue };
            try
            {
                best = ComputeBestActionable(pawn, comp, hauling);
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[BM] build priority scan failed: " + e, 0x5B02);
            }
            finally
            {
                scanning = false;
            }

            cachedBestAction[key] = best;
            cachedBestTick[key] = now;
            return best;
        }

        private static readonly List<KeyValuePair<long, OrderInfo>> sliceBuf = new List<KeyValuePair<long, OrderInfo>>();

        /// <summary>
        /// 按「名次 → 该小人距离 → 订单号」的顺序找第一个可执行的更高优先级订单。
        /// 达到检查上限时返回 Incomplete（Complete=false），不把“没查完”当成“没有”。
        /// </summary>
        private static BestAction ComputeBestActionable(Pawn pawn, BuildOrderComp comp, bool hauling)
        {
            OrderRegistry registry = OrderRegistry.Of(pawn.Map);
            if (registry == null) return new BestAction { Rank = int.MaxValue, Complete = true };
            registry.EnsureFresh();
            List<OrderInfo> all = registry.byRank;
            int n = all.Count;
            if (n == 0) return new BestAction { Rank = int.MaxValue, Complete = true };

            IntVec3 pos = pawn.Position;
            int checks = 0;
            int i = 0;
            while (i < n)
            {
                int rank = all[i].EffRank;
                int j = i;
                while (j < n && all[j].EffRank == rank) j++;

                // 同批（同一名次）内按（离该小人的距离, 订单号）排序；低成本预筛掉失效与禁用目标
                sliceBuf.Clear();
                for (int k = i; k < j; k++)
                {
                    OrderInfo info = all[k];
                    Thing t = info.Thing;
                    if (t == null || t.Destroyed || !t.Spawned) continue;
                    if (t.IsForbidden(pawn)) continue;
                    long dist = (long)(pos - t.PositionHeld).LengthHorizontalSquared;
                    sliceBuf.Add(new KeyValuePair<long, OrderInfo>((dist << 32) | (uint)info.Id, info));
                }
                sliceBuf.Sort((a, b) => a.Key.CompareTo(b.Key));

                for (int k = 0; k < sliceBuf.Count; k++)
                {
                    OrderInfo info = sliceBuf[k].Value;
                    Thing t = info.Thing;
                    if (++checks > MaxChecksPerScan) return new BestAction { Rank = int.MaxValue, Complete = false };
                    if (!Actionable(pawn, t, hauling)) continue;
                    Frame frame = t as Frame;
                    return new BestAction
                    {
                        Rank = rank,
                        Thing = t,
                        Finish = frame != null && frame.IsCompleted(),
                        Complete = true
                    };
                }
                i = j;
            }
            return new BestAction { Rank = int.MaxValue, Complete = true };
        }

        /// <summary>
        /// 更高优先级的那项工作是否和当前目标抢同一种材料。
        /// T-076 起**不再参与让路判定**（判定只看名次，见 ShouldYield），只用于日志措辞与诊断分类：
        /// 区分「因为更高名次在抢同一种材料而让路」和「只是名次本身更高而让路」。
        /// 留着它还有个副作用要防：下面三条提前返回让完工与安装恒为「不冲突」，
        /// 旧版把它当否决条件正是「名次第一却不优先」的根因，别再把它接回判定里。
        /// </summary>
        private static bool MaterialConflict(BestAction best, Thing target)
        {
            if (best.Thing == null || target == null) return true;
            if (best.Finish) return false;                       // 完工不消耗新材料
            if (best.Thing is Blueprint_Install) return false;   // 重新安装没有材料清单
            if (target is Blueprint_Install) return false;

            IConstructible targetC = target as IConstructible;
            IConstructible bestC = best.Thing as IConstructible;
            if (targetC == null || bestC == null) return false;

            List<ThingDefCountClass> targetCosts = targetC.TotalMaterialCost();
            if (targetCosts == null || targetCosts.Count == 0) return false;
            List<ThingDefCountClass> bestCosts = bestC.TotalMaterialCost();
            if (bestCosts == null || bestCosts.Count == 0) return false;

            for (int i = 0; i < targetCosts.Count; i++)
            {
                ThingDefCountClass tc = targetCosts[i];
                if (tc == null || tc.thingDef == null) continue;
                if (targetC.ThingCountNeeded(tc.thingDef) <= 0) continue;
                for (int j = 0; j < bestCosts.Count; j++)
                {
                    ThingDefCountClass bc = bestCosts[j];
                    if (bc == null || bc.thingDef != tc.thingDef) continue;
                    if (bestC.ThingCountNeeded(tc.thingDef) > 0) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 材料预算查询：低名次订单须拿到全部所需材料的额度才放行；
        /// 最高名次组自由竞争、无材料清单与重新安装订单不受限。手动命令不经过这里。
        /// </summary>
        public static bool ShouldWaitForMaterials(Map map, Thing target)
        {
            return MaterialBudget.ShouldWaitForMaterials(map, target);
        }

        /// <summary>
        /// 诊断用：该小人当前可做的最高优先级订单。
        /// **复用同一套三态预演**（含 45 tick 缓存），不另写排序 —— 否则就成了第二套真相。
        /// </summary>
        public static Thing BestFor(Pawn pawn, bool hauling, out int rank, out bool complete)
        {
            rank = int.MaxValue;
            complete = true;
            BuildOrderComp comp = BuildOrderComp.Instance;
            if (pawn == null || comp == null) return null;
            BestAction best = BestActionable(pawn, comp, hauling);
            complete = best.Complete;
            if (!best.Complete) return null;
            rank = best.Rank;
            return best.Thing;
        }

        /// <summary>诊断用：这张订单这个小人能不能做（与调度同一条 Actionable 判定）。</summary>
        public static bool CanDo(Pawn pawn, Thing t, bool hauling)
        {
            if (pawn == null || t == null) return false;
            return Actionable(pawn, t, hauling);
        }

        private static bool Actionable(Pawn pawn, Thing t, bool hauling)
        {
            Blueprint bp = t as Blueprint;
            if (bp != null)
            {
                if (!MaterialBudget.Granted(pawn.Map, t)) return false;
                WorkGiver_ConstructDeliverResourcesToBlueprints giver =
                    hauling ? HaulBlueprintsGiver : BlueprintsGiver;
                return giver != null && giver.HasJobOnThing(pawn, t, false);
            }

            Frame frame = t as Frame;
            if (frame == null) return false;

            if (frame.IsCompleted())
            {
                // 完工属于建造工作；搬运协助只负责送料；完工不消耗新材料，不受预算约束
                if (hauling) return false;
                WorkGiver_ConstructFinishFrames giver = FinishGiver;
                return giver != null && giver.JobOnThing(pawn, t, false) != null;
            }

            if (!MaterialBudget.Granted(pawn.Map, t)) return false;
            WorkGiver_ConstructDeliverResourcesToFrames frames =
                hauling ? HaulFramesGiver : FramesGiver;
            return frames != null && frames.HasJobOnThing(pawn, t, false);
        }
    }

    /// <summary>
    /// 旧档兼容墓碑：工作台优先级已拆到姊妹模组「更好的制造」，
    /// 旧档里仍是本类型名。留一个空组件把数据读掉即弃，避免读档报「找不到类型」。
    /// 待「旧档 + 新 mod」实测确认原版是静默跳过后再删掉。
    /// </summary>
    public class BenchPriorityComp : GameComponent
    {
        public BenchPriorityComp(Game game)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            // 旧数据不读：工作台排序按决策 2 直接丢弃，玩家重拖一次即可
        }
    }
    public static class PatchHelper
    {
        public static bool ShouldBlock(Pawn pawn, Thing t, bool forced)
        {
            if (forced) return false;                       // 手动命令始终允许
            if (pawn == null || t == null) return false;
            if (!BuildUtil.IsPendingBuild(t)) return false;
            return !BuildPermsComp.Allowed(pawn, t);
        }

        private static int finishLogs;

        /// <summary>真正的建造动作：在建造权限之外再检查等级范围与「允许执行的小人」白名单。</summary>
        public static bool ShouldBlockFinish(Pawn pawn, Thing t, bool forced)
        {
            if (ShouldBlock(pawn, t, forced))
            {
                LogOnce(pawn, t, "建造权限拦截");
                return true;
            }
            if (forced) return false;
            if (pawn == null || t == null) return false;
            if (!BuildUtil.IsPendingBuild(t)) return false;

            bool allowed = BuildSkillRangeComp.Allows(pawn, t);
            // 白名单只加在这一层：名单外的人照样能送料，只是不能完工（与等级范围同口径）
            if (allowed) allowed = BuildWorkerComp.Allows(pawn, t);
            LogGate(pawn, t, allowed, true);     // 框架完工路径要读等级范围（安装路径不读，见 ShouldBlockInstall）
            return !allowed;
        }

        /// <summary>
        /// 安装/重新安装蓝图：搬运与安装是同一个 HaulToContainer job（原版 InstallJob 里
        /// targetA=家具本体、targetB=安装蓝图），没有独立的完工出口，所以「谁去装」只能在这一层一次判完。
        /// 这里拦白名单的含义与普通蓝图不同：名单外的人连这件家具都不会去搬——这是想要的行为，
        /// 否则他把家具搬到工地却装不了，反而把蓝图卡死（勿当 bug 改回去）。
        /// 普通蓝图的「HasJobOnThing=只送料」仍走 ShouldBlock，不查等级与白名单。
        /// **不查等级范围**（T-071）：原版给安装蓝图生成 def 时只在「不是安装蓝图」的分支里才继承
        /// constructionSkillPrerequisite（ThingDefGenerator_Buildings.cs L103-108），安装蓝图这个字段恒为默认 0，
        /// 而原版判定只比它（GenConstruct.CanConstruct L291-296）→ 任何等级（含 0 级）都满足。
        /// 安装属于搬运，不是建造；这里再加等级就是把原版没有的门槛自己造出来（实测现象的根因）。
        /// </summary>
        public static bool ShouldBlockInstall(Pawn pawn, Thing t, bool forced)
        {
            if (ShouldBlock(pawn, t, forced)) return true;       // 建造权限（已有）
            if (forced) return false;
            if (pawn == null || t == null) return false;
            if (!(t is Blueprint_Install)) return false;         // 普通蓝图不在此拦：送料不限

            bool allowed = BuildWorkerComp.Allows(pawn, t);      // 只有白名单，不查等级范围
            LogGate(pawn, t, allowed, false);
            return !allowed;
        }

        private static readonly Dictionary<string, int> lastGateLog = new Dictionary<string, int>();
        private static readonly Dictionary<string, bool> lastGateDecision = new Dictionary<string, bool>();
        private static int gateLogs;

        /// <summary>
        /// 只在「判定结果发生变化」时记一条，避免刷屏。
        /// checkRange=false 用于安装蓝图：那条路径根本不查等级（T-071），
        /// 若这里照旧读等级范围，就会打出「范围=19~20 等级=8 允许」这种自相矛盾、事后误导排查的日志。
        /// </summary>
        private static void LogGate(Pawn pawn, Thing t, bool allowed, bool checkRange)
        {
            if (pawn == null || t == null) return;
            BuildSkillRangeComp comp = BuildSkillRangeComp.Instance;
            BuildWorkerComp wcomp = BuildWorkerComp.Instance;

            string key = BuildUtil.TypeKey(t);
            // 守卫是「等级**或**白名单任一受限」：只设白名单、不设等级范围时原来直接 return，
            // T-067 的验收证据（名单外被拦）就根本出不了日志（CR-162）
            bool rangeRestricted = checkRange && comp != null && comp.IsRestricted(key);
            bool workerRestricted = wcomp != null && wcomp.IsRestricted(key);
            if (!rangeRestricted && !workerRestricted) return;

            // tag 要分开安装蓝图与普通蓝图：两者同键同类型组，不分开的话同一小人先被普通蓝图拦过，
            // 之后被安装蓝图拦下就再也不记，「目标=…安装蓝图」这条可能永远不出现（CR-162）
            string tag = pawn.thingIDNumber + "|" + key + (t is Blueprint_Install ? "|inst" : "|norm");
            bool prev;
            bool changed = !lastGateDecision.TryGetValue(tag, out prev) || prev != allowed;
            lastGateDecision[tag] = allowed;
            if (!changed) return;

            if (gateLogs >= 100) return;
            gateLogs++;

            // 前缀由「等级判定」改「建造判定」：现在可能只有白名单生效，叫等级判定是错的（CR-162）
            IntRange r = comp != null ? comp.Get(key) : new IntRange(0, 20);
            Log.Message("[BM] 建造判定 " + (allowed ? "允许" : "阻止") +
                        "：小人=" + (pawn.LabelShort ?? "?") +
                        " 等级=" + BuildSkillRangeComp.LevelOf(pawn) +
                        " 目标=" + (t.LabelShort ?? t.def.defName) +
                        " key=" + key +
                        (rangeRestricted ? " 范围=" + r.min + "~" + r.max : "") +
                        (workerRestricted ? " 白名单=" + (wcomp.Contains(key, pawn.thingIDNumber) ? "在列" : "不在列") : ""));
        }

        /// <summary>铺地板/地面类工作：按地形 defName 判定权限 + 等级范围。</summary>
        public static bool ShouldBlockCell(Pawn pawn, IntVec3 c, bool forced)
        {
            if (forced) return false;
            if (pawn == null || pawn.Map == null || !c.InBounds(pawn.Map)) return false;

            TerrainDef td = c.GetTerrain(pawn.Map);
            if (td == null) return false;

            if (!BuildPermsComp.Allowed(pawn, BuildKind.Plain)) return true;

            bool allowed = BuildSkillRangeComp.AllowsKey(pawn, td.defName);
            if (!allowed)
            {
                BuildSkillRangeComp comp = BuildSkillRangeComp.Instance;
                IntRange r = comp != null ? comp.Get(td.defName) : new IntRange(0, 20);
                string tag = pawn.thingIDNumber + "|" + td.defName;
                int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
                int last;
                if (!lastGateLog.TryGetValue(tag, out last) || now - last >= 600)
                {
                    lastGateLog[tag] = now;
                    Log.Message("[BM] 等级判定 阻止(地面)：小人=" + (pawn.LabelShort ?? "?") +
                                " 等级=" + BuildSkillRangeComp.LevelOf(pawn) +
                                " 地形=" + td.defName + " 范围=" + r.min + "~" + r.max);
                }
            }
            return !allowed;
        }

        private static void LogOnce(Pawn pawn, Thing t, string why)
        {
            if (finishLogs >= 60) return;
            finishLogs++;
            Log.Message("[BM] " + why + "：小人=" + (pawn != null ? pawn.LabelShort : "?") +
                        " 目标=" + (t != null ? (t.LabelShort ?? t.def.defName) : "?") +
                        " key=" + (t != null ? BuildUtil.TypeKey(t) : "?"));
        }
    }

    [HarmonyPatch(typeof(WorkGiver_ConstructFinishFrames), "JobOnThing")]
    public static class Patch_FinishFrames
    {
        private static bool Prefix(Pawn pawn, Thing t, bool forced, WorkGiver_Scanner __instance)
        {
            if (BuildPriorityGate.ShouldYield(pawn, t, forced, __instance)) return false;
            return !PatchHelper.ShouldBlockFinish(pawn, t, forced);
        }
    }

    [HarmonyPatch(typeof(WorkGiver_ConstructDeliverResourcesToBlueprints), "JobOnThing")]
    public static class Patch_DeliverBlueprints
    {
        private static bool Prefix(Pawn pawn, Thing t, bool forced, WorkGiver_Scanner __instance)
        {
            if (BuildPriorityGate.ShouldYield(pawn, t, forced, __instance)) return false;
            if (!forced && BuildPriorityGate.ShouldWaitForMaterials(pawn.Map, t)) return false;
            return !PatchHelper.ShouldBlockInstall(pawn, t, forced);
        }
    }

    [HarmonyPatch(typeof(WorkGiver_ConstructDeliverResourcesToBlueprints), "HasJobOnThing")]
    public static class Patch_DeliverBlueprintsHas
    {
        private static bool Prefix(Pawn pawn, Thing t, bool forced, WorkGiver_Scanner __instance)
        {
            if (BuildPriorityGate.ShouldYield(pawn, t, forced, __instance)) return false;
            if (!forced && BuildPriorityGate.ShouldWaitForMaterials(pawn.Map, t)) return false;
            return !PatchHelper.ShouldBlockInstall(pawn, t, forced);
        }
    }

    [HarmonyPatch(typeof(WorkGiver_ConstructDeliverResourcesToFrames), "JobOnThing")]
    public static class Patch_DeliverFrames
    {
        private static bool Prefix(Pawn pawn, Thing t, bool forced, WorkGiver_Scanner __instance)
        {
            if (BuildPriorityGate.ShouldYield(pawn, t, forced, __instance)) return false;
            if (!forced && BuildPriorityGate.ShouldWaitForMaterials(pawn.Map, t)) return false;
            return !PatchHelper.ShouldBlock(pawn, t, forced);
        }
    }

    [HarmonyPatch(typeof(WorkGiver_ConstructDeliverResourcesToFrames), "HasJobOnThing")]
    public static class Patch_DeliverFramesHas
    {
        private static bool Prefix(Pawn pawn, Thing t, bool forced, WorkGiver_Scanner __instance)
        {
            if (BuildPriorityGate.ShouldYield(pawn, t, forced, __instance)) return false;
            if (!forced && BuildPriorityGate.ShouldWaitForMaterials(pawn.Map, t)) return false;
            return !PatchHelper.ShouldBlock(pawn, t, forced);
        }
    }

    /// <summary>
    /// 原版送料会把材料顺路送给附近 8 格内的其它需要材料的蓝图/框架。
    /// 有建造顺序时关掉这个顺路合并，否则小人明明选了高优先级目标，材料却被送到旁边的低优先级工地。
    /// </summary>
    [HarmonyPatch(typeof(WorkGiver_ConstructDeliverResources), "FindNearbyNeeders")]
    public static class Patch_NoNearbyNeedersWhenOrdered
    {
        private static bool Prefix(IConstructible c, int resNeeded,
            ref HashSet<Thing> __result, out int neededTotal, out Job jobToMakeNeederAvailable)
        {
            neededTotal = resNeeded;
            jobToMakeNeederAvailable = null;

            BuildOrderComp comp = BuildOrderComp.Instance;
            if (comp == null || comp.order == null || comp.order.Count == 0) return true;

            Thing self = c as Thing;
            if (self == null) return true;
            if (comp.Rank(BuildUtil.GroupKey(self)) == int.MaxValue) return true;

            __result = new HashSet<Thing>();
            return false;
        }
    }

    /// <summary>
    /// QualityBuilder（品质建造）会在它自己的 Postfix 里把「不够格」的小人的 FinishFrame 任务置空，
    /// 导致我们放行的小人（例如 0~1 级）也拿不到活。
    /// 这里用 Priority.Last 的 Postfix 在它之后补回任务：只要本 mod 的等级范围明确允许该小人，就恢复。
    /// </summary>
    [HarmonyPatch(typeof(WorkGiver_ConstructFinishFrames), "JobOnThing")]
    [HarmonyPriority(Priority.Last)]
    public static class Patch_FinishFrames_RestoreAfterQualityBuilder
    {
        private static int restored;

        private static void Postfix(Pawn pawn, Thing t, bool forced, ref Job __result, WorkGiver_Scanner __instance)
        {
            try
            {
                if (__result != null || forced) return;
                if (pawn == null || t == null) return;
                if (!(t is Frame frame)) return;
                if (BuildPriorityGate.ShouldYield(pawn, t, false, __instance)) return;

                BuildSkillRangeComp comp = BuildSkillRangeComp.Instance;
                if (comp == null) return;

                string key = BuildUtil.TypeKey(t);
                if (!comp.IsRestricted(key)) return;      // 没设范围就不插手
                if (!BuildSkillRangeComp.Allows(pawn, t)) return;

                // 按原版 WorkGiver_ConstructFinishFrames.JobOnThing 的前置条件复核一遍
                if (frame.Faction != pawn.Faction) return;
                if (!frame.IsCompleted()) return;
                if (!GenConstruct.CanTouchTargetFromValidCell(frame, pawn)) return;
                if (GenConstruct.FirstBlockingThing(frame, pawn) != null) return;
                if (!GenConstruct.CanConstruct(frame, pawn, true, forced)) return;
                if (!pawn.CanReserve(frame)) return;

                __result = JobMaker.MakeJob(JobDefOf.FinishFrame, frame);

                if (restored < 40)
                {
                    restored++;
                    Log.Message("[BM] 已覆盖品质建造拦截：小人=" + (pawn.LabelShort ?? "?") +
                                " 等级=" + BuildSkillRangeComp.LevelOf(pawn) +
                                " 目标=" + (frame.LabelShort ?? frame.def.defName) + " 范围=允许");
                }
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[BM] restore job failed: " + e, 0x5B01);
            }
        }
    }

    /// <summary>铺地板走的是 JobOnCell 路径，也要受等级范围限制。</summary>
    [HarmonyPatch(typeof(WorkGiver_ConstructAffectFloor), "HasJobOnCell")]
    public static class Patch_FloorHasJobOnCell
    {
        private static bool Prefix(Pawn pawn, IntVec3 c, bool forced)
        {
            return !PatchHelper.ShouldBlockCell(pawn, c, forced);
        }
    }


    /// <summary>建造类工作参与排序：只在有排序记录时才接管。</summary>
    [HarmonyPatch(typeof(WorkGiver_Scanner), "get_Prioritized")]
    public static class Patch_ScannerPrioritized
    {
        private static void Postfix(WorkGiver_Scanner __instance, ref bool __result)
        {
            if (__instance is WorkGiver_ConstructFinishFrames
                || __instance is WorkGiver_ConstructDeliverResourcesToBlueprints
                || __instance is WorkGiver_ConstructDeliverResourcesToFrames)
            {
                BuildOrderComp comp = BuildOrderComp.Instance;
                if (comp != null && comp.order != null && comp.order.Count > 0) __result = true;
            }
        }
    }

    /// <summary>建造优先级数值：按「未建造统计」的拖动顺序。</summary>
    [HarmonyPatch(typeof(WorkGiver_Scanner), "GetPriority", new[] { typeof(Pawn), typeof(TargetInfo) })]
    public static class Patch_ScannerPriority
    {
        private static void Postfix(WorkGiver_Scanner __instance, Pawn pawn, TargetInfo t, ref float __result)
        {
            if (!(__instance is WorkGiver_ConstructFinishFrames
                || __instance is WorkGiver_ConstructDeliverResourcesToBlueprints
                || __instance is WorkGiver_ConstructDeliverResourcesToFrames))
            {
                return;
            }

            Thing thing = t.Thing;
            if (thing == null || !BuildUtil.IsPendingBuild(thing)) return;
            BuildOrderComp orderComp = BuildOrderComp.Instance;
            if (orderComp == null) return;
            int buildRank = orderComp.Rank(BuildUtil.GroupKey(thing));
            __result = buildRank == int.MaxValue ? -1000f : 1000f - buildRank;
        }
    }
}
