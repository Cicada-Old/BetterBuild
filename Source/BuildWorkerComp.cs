using System.Collections.Generic;
using Verse;

namespace BuildMonitor
{
    public class WorkerEntry : IExposable
    {
        public string key;
        public List<int> pawnIds = new List<int>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref key, "key");
            Scribe_Collections.Look(ref pawnIds, "pawnIds", LookMode.Value);
            if (pawnIds == null) pawnIds = new List<int>();
        }
    }

    /// <summary>
    /// 每种建造类型「允许执行的小人」白名单——在建造权限、等级范围之外的第三层过滤。
    /// 名单为空 = 不限制（回退到「权限 AND 等级」）；非空 = 只有名单内的小人能完工。
    /// 只拦完工、不拦送料，与等级范围的口径保持一致。
    /// </summary>
    public class BuildWorkerComp : GameComponent
    {
        public List<WorkerEntry> entries = new List<WorkerEntry>();
        private Dictionary<string, List<int>> cache;

        public BuildWorkerComp(Game game)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref entries, "BM_WorkerWhitelists", LookMode.Deep);
            if (entries == null) entries = new List<WorkerEntry>();
            cache = null;
        }

        public static BuildWorkerComp Instance
        {
            get
            {
                Game g = Current.Game;
                return g != null ? g.GetComponent<BuildWorkerComp>() : null;
            }
        }

        private Dictionary<string, List<int>> Cache
        {
            get
            {
                if (cache == null)
                {
                    cache = new Dictionary<string, List<int>>();
                    for (int i = 0; i < entries.Count; i++)
                    {
                        WorkerEntry e = entries[i];
                        if (e == null || string.IsNullOrEmpty(e.key)) continue;
                        string k = BuildUtil.NormalizeKey(e.key);
                        if (e.pawnIds == null) e.pawnIds = new List<int>();
                        cache[k] = e.pawnIds;
                    }
                }
                return cache;
            }
        }

        /// <summary>这一类型是否设了白名单（没设或名单为空都算不限制）。</summary>
        public bool IsRestricted(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            List<int> ids;
            return Cache.TryGetValue(BuildUtil.NormalizeKey(key), out ids) && ids != null && ids.Count > 0;
        }

        public bool Contains(string key, int pawnId)
        {
            if (string.IsNullOrEmpty(key)) return false;
            List<int> ids;
            return Cache.TryGetValue(BuildUtil.NormalizeKey(key), out ids) && ids != null && ids.Contains(pawnId);
        }

        /// <summary>名单内人数，供「允许执行」列显示；0 = 不限制。</summary>
        public int Count(string key)
        {
            if (string.IsNullOrEmpty(key)) return 0;
            List<int> ids;
            if (!Cache.TryGetValue(BuildUtil.NormalizeKey(key), out ids) || ids == null) return 0;
            return ids.Count;
        }

        public void Toggle(string key, int pawnId)
        {
            if (string.IsNullOrEmpty(key)) return;
            key = BuildUtil.NormalizeKey(key);

            WorkerEntry target = FindEntry(key);
            if (target == null)
            {
                target = new WorkerEntry { key = key };
                entries.Add(target);
                Cache[key] = target.pawnIds;
            }
            if (target.pawnIds.Contains(pawnId)) target.pawnIds.Remove(pawnId);
            else target.pawnIds.Add(pawnId);

            if (target.pawnIds.Count == 0) RemoveEntry(key);
            Invalidate();
        }

        /// <summary>整组设为「这些人全能做」或「清空＝不限制」。ids 由界面按当前建造小人给出。</summary>
        public void SetAll(string key, List<int> ids)
        {
            if (string.IsNullOrEmpty(key)) return;
            key = BuildUtil.NormalizeKey(key);

            if (ids == null || ids.Count == 0)
            {
                RemoveEntry(key);
                Invalidate();
                return;
            }
            WorkerEntry target = FindEntry(key);
            if (target == null)
            {
                target = new WorkerEntry { key = key };
                entries.Add(target);
            }
            target.pawnIds = new List<int>(ids);
            Cache[key] = target.pawnIds;
            Invalidate();
        }

        private WorkerEntry FindEntry(string normalizedKey)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                WorkerEntry e = entries[i];
                if (e != null && !string.IsNullOrEmpty(e.key) && BuildUtil.NormalizeKey(e.key) == normalizedKey) return e;
            }
            return null;
        }

        private void RemoveEntry(string normalizedKey)
        {
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                WorkerEntry e = entries[i];
                if (e != null && !string.IsNullOrEmpty(e.key) && BuildUtil.NormalizeKey(e.key) == normalizedKey) entries.RemoveAt(i);
            }
            if (cache != null) cache.Remove(normalizedKey);
        }

        /// <summary>白名单影响「谁去做」，改动后必须让 45 tick 的调度预演缓存作废，否则最多 45 tick 看不到效果。</summary>
        private static void Invalidate()
        {
            BuildOrderComp.Version++;
        }

        /// <summary>清掉已经不在队里的小人 id：名单里躺着不存在的人，会让「N 人」与实际不符。</summary>
        public void PruneInvalidIds(Map map)
        {
            if (map == null || entries.Count == 0) return;
            List<Pawn> colonists = map.mapPawns.FreeColonists;
            var valid = new HashSet<int>(colonists.Count);
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn p = colonists[i];
                if (p != null) valid.Add(p.thingIDNumber);
            }

            bool changed = false;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                WorkerEntry e = entries[i];
                if (e == null) { entries.RemoveAt(i); changed = true; continue; }
                if (e.pawnIds == null) { e.pawnIds = new List<int>(); }
                for (int j = e.pawnIds.Count - 1; j >= 0; j--)
                {
                    if (!valid.Contains(e.pawnIds[j])) { e.pawnIds.RemoveAt(j); changed = true; }
                }
                if (e.pawnIds.Count == 0) entries.RemoveAt(i);
            }
            if (changed) cache = null;
        }

        /// <summary>权限 AND 等级都过了，再看白名单（未设名单 = 允许）。</summary>
        public static bool Allows(Pawn p, Thing t)
        {
            BuildWorkerComp comp = Instance;
            if (comp == null || p == null || t == null) return true;
            string key = BuildUtil.TypeKey(t);
            if (!comp.IsRestricted(key)) return true;
            return comp.Contains(key, p.thingIDNumber);
        }

        /// <summary>按 key 判定（供工具提示与列内容用，地面类不走白名单）。</summary>
        public static bool AllowsKey(Pawn p, string key)
        {
            BuildWorkerComp comp = Instance;
            if (comp == null || p == null || string.IsNullOrEmpty(key)) return true;
            if (!comp.IsRestricted(key)) return true;
            return comp.Contains(key, p.thingIDNumber);
        }
    }
}
