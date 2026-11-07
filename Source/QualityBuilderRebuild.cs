using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace BuildMonitor
{
    /// <summary>
    /// QualityBuilder 的「拆除后重建」只在自己发起拆除（pendingQualityRebuild=true）时生效，
    /// 而且它的补丁在 1.7 下并不总能走到放蓝图那一步。
    /// 这里做兜底：拆除完成后如果确实「应该重建」但没有蓝图，就自己放一个。
    /// </summary>
    [HarmonyPatch(typeof(JobDriver_Deconstruct), "FinishedRemoving")]
    public static class Patch_DeconstructRebuild
    {
        private static Type compType;
        private static PropertyInfo pSkilled, pDesired, pPending, pAttempts;
        private static bool resolved;

        private static void Resolve()
        {
            if (resolved) return;
            resolved = true;

            compType = AccessTools.TypeByName("CompQualityBuilder");
            if (compType == null) return;

            pSkilled = compType.GetProperty("isSkilled", BindingFlags.Public | BindingFlags.Instance);
            pDesired = compType.GetProperty("desiredMinQuality", BindingFlags.Public | BindingFlags.Instance);
            pPending = compType.GetProperty("pendingQualityRebuild", BindingFlags.Public | BindingFlags.Instance);
            pAttempts = compType.GetProperty("qualityRebuildAttempts", BindingFlags.Public | BindingFlags.Instance);
        }

        private static ThingComp FindComp(ThingWithComps twc)
        {
            if (twc?.AllComps == null) return null;
            var comps = twc.AllComps;
            for (int i = 0; i < comps.Count; i++)
            {
                ThingComp c = comps[i];
                if (c != null && compType.IsInstanceOfType(c)) return c;
            }
            return null;
        }

        [HarmonyPostfix]
        public static void Postfix(JobDriver_Deconstruct __instance)
        {
            try
            {
                Resolve();
                if (compType == null) return;

                Job job = __instance?.job;
                Thing thing = job?.targetA.Thing;
                if (thing == null) return;

                ThingWithComps twc = thing as ThingWithComps;
                if (twc == null) return;
                ThingComp comp = FindComp(twc);
                if (comp == null) return;

                bool skilled = pSkilled != null && (bool)pSkilled.GetValue(comp, null);
                if (!skilled) return;

                bool pending = pPending != null && (bool)pPending.GetValue(comp, null);
                if (!pending) return;                 // mod 没打算重建，不插手

                Pawn pawn = __instance.pawn;
                Map map = pawn != null ? pawn.Map : Find.CurrentMap;
                if (map == null) return;

                IntVec3 cell = job.targetA.Cell;
                if (!cell.IsValid || !cell.InBounds(map)) return;

                // 已经有蓝图/框架就不重复放
                var list = cell.GetThingList(map);
                for (int i = 0; i < list.Count; i++)
                {
                    Thing t = list[i];
                    if (t is Blueprint || t is Frame || (t != null && t.def != null && t.def.IsBlueprint)) return;
                }

                ThingDef def = thing.def;
                ThingDef stuff = thing.Stuff;
                if (def == null) return;

                Blueprint bp = GenConstruct.PlaceBlueprintForBuild(def, cell, map, thing.Rotation, Faction.OfPlayer, stuff, null, null, true);
                if (bp == null)
                {
                    Log.Warning("[QBRebuild] 放置蓝图失败：" + def.defName + " @ " + cell);
                    return;
                }

                // 把品质要求抄到新蓝图上
                ThingComp newComp = bp is ThingWithComps btwc ? FindComp(btwc) : null;
                if (newComp != null)
                {
                    object desired = pDesired?.GetValue(comp, null);
                    if (desired != null && pDesired.CanWrite) pDesired.SetValue(newComp, desired, null);
                    if (pSkilled.CanWrite) pSkilled.SetValue(newComp, true, null);

                    int attempts = pAttempts != null ? (int)pAttempts.GetValue(comp, null) : 0;
                    if (pAttempts != null && pAttempts.CanWrite) pAttempts.SetValue(newComp, attempts + 1, null);
                }

                pPending.SetValue(comp, false, null);
                Log.Message("[QBRebuild] 已补放重建蓝图：" + def.defName + " @ " + cell);
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[QBRebuild] rebuild failed: " + e, 0x7B01);
            }
        }
    }
}
