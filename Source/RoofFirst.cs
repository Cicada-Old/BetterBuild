using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace RoofFirstDeconstruct
{
    /// <summary>
    /// 启动时做三件事：
    /// 1) 按 JobDriver 类别（而不是写死 defName）算出「会把建筑拆掉」的 JobDef 集合，
    ///    mod 自定义的拆除 JobDef 只要复用原版驱动就会被一起管住；
    /// 2) 给**所有** WorkGiver 的 JobOnThing 挂后置补丁。原版有两条独立通路：
    ///    WorkGiver_RemoveBuilding 系（拆除/卸载/填埋）和 WorkGiver_DeconstructForBlueprint
    ///    （往已有建筑上放蓝图时的自动拆除，它直接继承 WorkGiver_Scanner，不打补丁就漏），
    ///    逐个类型扫一遍就都覆盖到了，以后加的 mod 分配器也自动生效；
    /// 3) 给 Building.Destroy 挂前置补丁做兜底：真到了拆除销毁那一刻屋顶还在，
    ///    就直接把会塌的普通屋顶清掉（不生成塌方落石），保证「墙拆了屋顶一定不砸」。
    /// </summary>
    [StaticConstructorOnStartup]
    public static class Bootstrap
    {
        public static readonly HashSet<JobDef> RemovalJobs = new HashSet<JobDef>();

        private static Harmony harmony;

        static Bootstrap()
        {
            try
            {
                harmony = new Harmony("Cicada.BetterBuild");
                int jobCount = CollectRemovalJobs();
                int giverCount = InstallWorkGiverPatches();
                bool guard = InstallDestroyGuard();
                Log.Message("[BB] ready：拆除/开挖任务 " + jobCount + " 种，工作分配器 " + giverCount +
                    " 个，销毁兜底=" + (guard ? "on" : "off"));
            }
            catch (Exception e)
            {
                Log.Error("[BB] init failed: " + e);
            }
        }

        private static int CollectRemovalJobs()
        {
            foreach (JobDef def in DefDatabase<JobDef>.AllDefsListForReading)
            {
                Type driver = def.driverClass;
                if (driver == null)
                {
                    continue;
                }
                if (typeof(JobDriver_RemoveBuilding).IsAssignableFrom(driver)
                    || typeof(JobDriver_Mine).IsAssignableFrom(driver))
                {
                    RemovalJobs.Add(def);
                }
            }
            return RemovalJobs.Count;
        }

        private static int InstallWorkGiverPatches()
        {
            Type[] paramTypes = { typeof(Pawn), typeof(Thing), typeof(bool) };
            HarmonyMethod postfix = new HarmonyMethod(typeof(WorkGiverGate), nameof(WorkGiverGate.Postfix));
            int count = 0;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types;
                }
                catch
                {
                    continue;
                }
                if (types == null)
                {
                    continue;
                }

                for (int i = 0; i < types.Length; i++)
                {
                    Type type = types[i];
                    if (type == null || !typeof(WorkGiver).IsAssignableFrom(type))
                    {
                        continue;
                    }

                    MethodInfo method;
                    try
                    {
                        method = type.GetMethod("JobOnThing", BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
                            null, paramTypes, null);
                    }
                    catch
                    {
                        continue;
                    }
                    if (method == null || method.IsAbstract)
                    {
                        continue;
                    }

                    try
                    {
                        harmony.Patch(method, postfix: postfix);
                        count++;
                    }
                    catch (Exception e)
                    {
                        Log.Warning("[BB] 跳过 " + type.FullName + "：" + e.Message);
                    }
                }
            }
            return count;
        }

        private static bool InstallDestroyGuard()
        {
            MethodInfo method = AccessTools.Method(typeof(Building), "Destroy", new[] { typeof(DestroyMode) });
            if (method == null)
            {
                return false;
            }
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(BuildingDestroyGuard), nameof(BuildingDestroyGuard.Prefix)));
            return true;
        }
    }

    /// <summary>
    /// 所有 WorkGiver.JobOnThing 的统一后置补丁：只关心「产出的是不是拆建筑任务」。
    /// 用 __0 / __2 按位置取参数（pawn、forced），不写死参数名，兼容 mod 里各种命名。
    /// </summary>
    internal static class WorkGiverGate
    {
        public static void Postfix(Pawn __0, bool __2, Job __result)
        {
            try
            {
                if (__result == null || __result.def == null)
                {
                    return;
                }
                if (!Bootstrap.RemovalJobs.Contains(__result.def))
                {
                    return;
                }

                Thing target = __result.targetA.Thing;
                if (target == null)
                {
                    return;
                }

                string action = __result.def == JobDefOf.Mine
                    ? "RF_ActionMine".Translate().ToString()
                    : "RF_ActionDeconstruct".Translate().ToString();
                RoofGating.TryGate(__0, target, ref __result, action, __2 || __result.playerForced);
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[BB] gate failed: " + e, 0x9F01);
            }
        }
    }

    /// <summary>
    /// 拆建筑真的走到「销毁」这一步时的兜底：如果上方还有会导致塌方的普通屋顶，
    /// 直接清掉（而不是让它塌下来砸人砸物）。厚岩顶拆不掉，交给原版处理。
    /// </summary>
    internal static class BuildingDestroyGuard
    {
        public static bool Prefix(Building __instance, DestroyMode mode)
        {
            if (mode != DestroyMode.Deconstruct)
            {
                return true;
            }
            try
            {
                if (Current.ProgramState != ProgramState.Playing)
                {
                    return true;
                }

                Building b = __instance;
                if (b == null || b.def == null || !b.def.holdsRoof)
                {
                    return true;
                }

                Map map = b.Map;
                if (map == null || map.roofGrid == null)
                {
                    return true;
                }

                List<IntVec3> cells = RoofPrediction.Predict(map, b);
                if (cells.Count == 0)
                {
                    return true;
                }

                int cleared = 0;
                for (int i = 0; i < cells.Count; i++)
                {
                    RoofDef roof = cells[i].GetRoof(map);
                    if (roof == null || roof.isThickRoof)
                    {
                        continue;
                    }
                    map.roofGrid.SetRoof(cells[i], null);
                    cleared++;
                }
                if (cleared > 0)
                {
                    RoofGating.Diag("兜底清理：" + (b.LabelShort ?? b.def.defName) + " @ " + b.Position +
                        " 拆除前清掉 " + cleared + " 格屋顶（该拆除通路没被工作分配器拦住）");
                }
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[BB] destroy guard failed: " + e, 0x9F03);
            }
            return true;
        }
    }

    /// <summary>
    /// 拆/挖承重物之前，先把会塌的屋顶清掉。
    /// - 可拆的屋顶：标记「拆除屋顶」区域并**取消本次任务**，小人会先去拆屋顶；
    ///   屋顶拆完后，下一次分配会自动放行。
    /// - 含厚岩顶（拆不掉）：不标记、不放行，提示玩家右键强制。
    /// </summary>
    internal static class RoofGating
    {
        private static int diagCount;

        public static void TryGate(Pawn pawn, Thing t, ref Job result, string action, bool forced)
        {
            try
            {
                if (result == null || pawn == null || t == null)
                {
                    return;
                }

                Building b = t.GetInnerIfMinified() as Building;
                if (b == null || b.def == null || !b.def.holdsRoof)
                {
                    return;
                }

                Map map = b.Map;
                if (map == null || map.roofGrid == null || map.areaManager?.NoRoof == null)
                {
                    return;
                }

                // 已经为它标记过屋顶：屋顶没拆完就继续拦（顺便补回被玩家擦掉的标记），
                // 拆完了就忘掉记录、走正常流程放行。
                if (RoofMarkTracker.StillRoofed(b, map))
                {
                    RoofMarkTracker.Remark(b, map);
                    result = null;
                    return;
                }
                RoofMarkTracker.Forget(b);

                // 用原版塌方函数预测：它会把它消失后要塌的格子写进 buffer，我们读完立刻还原
                List<IntVec3> predicted = RoofPrediction.Predict(map, b);
                if (predicted.Count == 0)
                {
                    DiagOnce(b, "预测不塌，" + action + "放行：" + (b.LabelShort ?? b.def.defName) + " @ " + b.Position);
                    return;
                }

                List<IntVec3> removable = new List<IntVec3>();
                bool thickRisk = false;
                for (int i = 0; i < predicted.Count; i++)
                {
                    RoofDef roof = predicted[i].GetRoof(map);
                    if (roof == null)
                    {
                        continue;
                    }
                    if (roof.isThickRoof)
                    {
                        thickRisk = true;
                    }
                    else
                    {
                        removable.Add(predicted[i]);
                    }
                }

                string name = b.LabelShort ?? b.def.defName;
                string where = " @ " + b.Position;

                // 只剩拆不掉的屋顶（厚岩顶 / 不可塌屋顶）
                if (removable.Count == 0)
                {
                    RoofRiskRegistry.Register(b, predicted.Count);
                    if (forced)
                    {
                        Diag("放行（强制）" + action + "：" + name + where);
                        return;
                    }
                    NotifyOnce(b, thickRisk
                        ? "RF_BlockedThick".Translate(action, name, predicted.Count).ToString()
                        : "RF_BlockedUnremovable".Translate(action, name, predicted.Count).ToString());
                    Diag("阻止（不可拆屋顶）" + action + "：" + name + where + "，预测 " + predicted.Count + " 格，厚岩顶=" + thickRisk);
                    result = null;
                    return;
                }

                // 先标记能拆的那部分（厚岩顶的情况也标，让玩家至少能先清掉普通屋顶）
                for (int i = 0; i < removable.Count; i++)
                {
                    if (map.areaManager.NoRoof[removable[i]] != true)
                    {
                        map.areaManager.NoRoof[removable[i]] = true;
                    }
                }
                RoofMarkTracker.Track(b, map, removable);

                // 一格都够不着：拦着也没用
                bool anyReachable = false;
                for (int i = 0; i < removable.Count; i++)
                {
                    if (pawn.CanReach(removable[i], PathEndMode.Touch, Danger.Deadly))
                    {
                        anyReachable = true;
                        break;
                    }
                }
                if (!anyReachable)
                {
                    RoofRiskRegistry.Register(b, predicted.Count);
                    NotifyOnce(b, "RF_BlockedUnreachable".Translate(action, name, removable.Count).ToString());
                    Diag("阻止（屋顶不可达）" + action + "：" + name + where);
                    result = null;
                    return;
                }

                // 普通屋顶之外还有厚岩顶：普通部分先拆，但**不放行**，否则拆完照样砸
                if (thickRisk && !forced)
                {
                    RoofRiskRegistry.Register(b, predicted.Count);
                    NotifyOnce(b, "RF_PausedThick".Translate(action, name, removable.Count).ToString());
                    Diag("暂停（厚岩顶残留）" + action + "：" + name + where + "，可拆 " + removable.Count + " 格");
                    result = null;
                    return;
                }

                // ★ 取消本次任务，先让小人去拆屋顶
                NotifyOnce(b, "RF_PausedRoofFirst".Translate(action, name, removable.Count).ToString());
                Diag("先拆屋顶再" + action + "：" + name + where + "，需拆 " + removable.Count + " 格");
                result = null;
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[BB] gate failed: " + e, 0x9F01);
            }
        }

        public static void Diag(string msg)
        {
            if (diagCount >= 200)
            {
                return;
            }
            diagCount++;
            Log.Message("[BB] " + msg);
        }

        private static readonly HashSet<int> diagOnce = new HashSet<int>();

        /// <summary>同一目标只写一次诊断，避免日志被刷爆。</summary>
        private static void DiagOnce(Thing thing, string msg)
        {
            if (thing == null || !diagOnce.Add(thing.thingIDNumber))
            {
                return;
            }
            Diag(msg);
        }

        private static readonly HashSet<int> notified = new HashSet<int>();

        /// <summary>同一个目标只提示一次，避免刷屏。</summary>
        private static void NotifyOnce(Thing thing, string text)
        {
            if (thing == null)
            {
                return;
            }
            if (!notified.Add(thing.thingIDNumber))
            {
                return;
            }
            Messages.Message(text, new LookTargets(thing), MessageTypeDefOf.NeutralEvent, false);
        }

        /// <summary>目标消失后清掉提示记录，下次同一位置还能再提示。</summary>
        public static void ForgetNotice(int thingId)
        {
            notified.Remove(thingId);
        }
    }

    /// <summary>
    /// 直接调用游戏的塌方判定：RoofCollapseCellsFinder.ProcessRoofHolderDespawned。
    /// 会把「这个物体消失后要塌的格子」写进 map.roofCollapseBuffer；读完立刻还原 buffer，避免真塌。
    /// </summary>
    internal static class RoofPrediction
    {
        private static ThingDef fakeDef;

        public static List<IntVec3> Predict(Map map, Building b)
        {
            List<IntVec3> result = new List<IntVec3>();
            RoofCollapseBuffer buf = map.roofCollapseBuffer;
            if (buf == null)
            {
                return result;
            }

            CellRect rect = b.OccupiedRect();
            IntVec3 pos = b.Position;

            List<IntVec3> backup = new List<IntVec3>(buf.CellsMarkedToCollapse);
            ThingDef originalDef = b.def;

            try
            {
                // 只让「这一个实例」暂时不承重：改实例字段，不动共享 ThingDef
                if (fakeDef == null)
                {
                    fakeDef = ThingDefOf.Steel;
                }
                b.def = fakeDef;

                RoofCollapseCellsFinder.ProcessRoofHolderDespawned(rect, pos, map);

                for (int i = 0; i < buf.CellsMarkedToCollapse.Count; i++)
                {
                    IntVec3 c = buf.CellsMarkedToCollapse[i];
                    if (!backup.Contains(c))
                    {
                        result.Add(c);
                    }
                }
            }
            finally
            {
                b.def = originalDef;
                buf.Clear();
                for (int i = 0; i < backup.Count; i++)
                {
                    buf.MarkToCollapse(backup[i]);
                }
            }

            return result;
        }
    }

    /// <summary>
    /// 记录「为拆某个建筑而标记的无屋顶格子」：建筑消失、或玩家取消拆/挖指令后才取消标记。
    /// （提前取消会让封闭房间立刻重新长屋顶，导致反复拆建）
    /// </summary>
    internal static class RoofMarkTracker
    {
        private sealed class Entry
        {
            public Thing thing;
            public Map map;
            public readonly List<IntVec3> cells = new List<IntVec3>();
        }

        private static readonly Dictionary<int, Entry> tracked = new Dictionary<int, Entry>();

        public static void Track(Building b, Map map, List<IntVec3> cells)
        {
            if (b == null || map == null || cells == null || cells.Count == 0)
            {
                return;
            }

            Entry entry;
            if (!tracked.TryGetValue(b.thingIDNumber, out entry))
            {
                entry = new Entry();
                tracked[b.thingIDNumber] = entry;
            }
            entry.thing = b;
            entry.map = map;

            for (int i = 0; i < cells.Count; i++)
            {
                if (!entry.cells.Contains(cells[i]))
                {
                    entry.cells.Add(cells[i]);
                }
            }
        }

        /// <summary>该目标还在跟踪中，且标记的格子里仍有屋顶（说明屋顶没拆完）。</summary>
        public static bool StillRoofed(Thing t, Map map)
        {
            Entry entry;
            if (t == null || map == null || !tracked.TryGetValue(t.thingIDNumber, out entry))
            {
                return false;
            }
            entry.thing = t;
            entry.map = map;
            for (int i = 0; i < entry.cells.Count; i++)
            {
                IntVec3 c = entry.cells[i];
                if (c.InBounds(map) && c.Roofed(map))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>玩家可能擦掉了「拆除屋顶」区域，这里补回来，避免卡死。</summary>
        public static void Remark(Thing t, Map map)
        {
            Entry entry;
            if (t == null || map?.areaManager?.NoRoof == null)
            {
                return;
            }
            if (!tracked.TryGetValue(t.thingIDNumber, out entry))
            {
                return;
            }

            for (int i = 0; i < entry.cells.Count; i++)
            {
                IntVec3 c = entry.cells[i];
                if (c.InBounds(map) && c.Roofed(map) && map.areaManager.NoRoof[c] != true)
                {
                    map.areaManager.NoRoof[c] = true;
                }
            }
        }

        public static void Forget(Thing t)
        {
            if (t == null)
            {
                return;
            }
            tracked.Remove(t.thingIDNumber);
        }

        public static void Cleanup()
        {
            if (tracked.Count == 0)
            {
                return;
            }

            List<int> done = null;
            foreach (KeyValuePair<int, Entry> kv in tracked)
            {
                Entry entry = kv.Value;
                Thing thing = entry.thing;
                Map map = entry.map;

                bool keep = false;
                if (thing != null && map != null && !thing.Destroyed && thing.Spawned)
                {
                    // 建筑还在：只有拆/挖指令还在时才保留这些无屋顶标记
                    DesignationManager dm = map.designationManager;
                    keep = dm != null
                        && (dm.DesignationOn(thing, DesignationDefOf.Deconstruct) != null
                            || dm.DesignationOn(thing, DesignationDefOf.Mine) != null
                            || dm.DesignationOn(thing, DesignationDefOf.MineVein) != null);
                }

                if (keep)
                {
                    continue;
                }

                Area_NoRoof noRoof = map?.areaManager?.NoRoof;
                if (noRoof != null)
                {
                    for (int i = 0; i < entry.cells.Count; i++)
                    {
                        IntVec3 c = entry.cells[i];
                        if (c.InBounds(map) && !c.Roofed(map))
                        {
                            noRoof[c] = false;
                        }
                    }
                }
                if (thing != null)
                {
                    RoofGating.ForgetNotice(thing.thingIDNumber);
                }
                (done ?? (done = new List<int>())).Add(kv.Key);
            }

            if (done == null)
            {
                return;
            }
            for (int i = 0; i < done.Count; i++)
            {
                tracked.Remove(done[i]);
            }
        }
    }

    /// <summary>「厚岩顶会塌但拆不掉」的位置登记，供警报跳转。</summary>
    internal static class RoofRiskRegistry
    {
        private static readonly Dictionary<int, Thing> risky = new Dictionary<int, Thing>();
        private static readonly HashSet<int> notified = new HashSet<int>();

        public static void Register(Thing thing, int cells)
        {
            if (thing == null)
            {
                return;
            }
            risky[thing.thingIDNumber] = thing;

            if (notified.Add(thing.thingIDNumber))
            {
                Messages.Message(
                    "RF_WarningMessage".Translate(thing.LabelShort ?? thing.def?.defName, cells).ToString(),
                    new LookTargets(thing),
                    MessageTypeDefOf.NegativeEvent,
                    false);
            }
        }

        public static List<Thing> Things()
        {
            List<Thing> list = new List<Thing>();
            List<int> dead = null;
            foreach (KeyValuePair<int, Thing> kv in risky)
            {
                Thing v = kv.Value;
                if (v == null || (v.Destroyed && !v.Spawned))
                {
                    (dead ?? (dead = new List<int>())).Add(kv.Key);
                    continue;
                }
                list.Add(v);
            }
            if (dead != null)
            {
                for (int i = 0; i < dead.Count; i++)
                {
                    risky.Remove(dead[i]);
                    notified.Remove(dead[i]);
                    RoofGating.ForgetNotice(dead[i]);
                }
            }
            return list;
        }
    }

    public class Alert_RoofCollapseRisk : Alert
    {
        public Alert_RoofCollapseRisk()
        {
            defaultPriority = AlertPriority.Medium;
            defaultLabel = "RF_AlertLabel".Translate().ToString();
            defaultExplanation = "RF_AlertExplanation".Translate().ToString();
        }

        public override AlertReport GetReport()
        {
            List<Thing> list = RoofRiskRegistry.Things();
            if (list.Count == 0)
            {
                return AlertReport.Inactive;
            }
            return AlertReport.CulpritsAre(list);
        }
    }

    /// <summary>每 500 tick 清理一次已失效的屋顶标记。</summary>
    public class RoofMarkCleanup : GameComponent
    {
        public RoofMarkCleanup(Game game)
        {
        }

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % 500 != 0)
            {
                return;
            }
            try
            {
                RoofMarkTracker.Cleanup();
                RoofRiskRegistry.Things();
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[BB] cleanup failed: " + e, 0x9F02);
            }
        }
    }
}
