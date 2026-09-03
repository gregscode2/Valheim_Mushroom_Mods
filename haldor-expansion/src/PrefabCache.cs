using System.Collections.Generic;

namespace HaldorExpansion
{
    /// <summary>
    /// Resolves prefab names against the live ObjectDB rather than trusting hardcoded
    /// assumptions about what exists in this game version. A miss is logged loudly once
    /// and the row is skipped -- the item silently vanishing from the shop with no
    /// explanation in the log is the failure mode worth spending code to avoid.
    /// </summary>
    internal static class PrefabCache
    {
        private static readonly Dictionary<string, ItemDrop> Cache = new Dictionary<string, ItemDrop>();
        private static readonly HashSet<string> Reported = new HashSet<string>();

        public static ItemDrop Resolve(string prefabName)
        {
            if (Cache.TryGetValue(prefabName, out var cached) && cached != null) return cached;

            var odb = ObjectDB.instance;
            if (odb == null) return null;

            var go = odb.GetItemPrefab(prefabName);
            if (go == null)
            {
                Warn(prefabName, "not found in ObjectDB");
                return null;
            }

            var itemDrop = go.GetComponent<ItemDrop>();
            if (itemDrop == null)
            {
                Warn(prefabName, "found in ObjectDB but has no ItemDrop component");
                return null;
            }

            Cache[prefabName] = itemDrop;
            return itemDrop;
        }

        public static void Reset()
        {
            Cache.Clear();
            Reported.Clear();
        }

        private static void Warn(string prefabName, string reason)
        {
            if (!Reported.Add(prefabName)) return;
            Plugin.Log.LogError("Prefab '" + prefabName + "' " + reason
                                + ". This row will not appear in the shop. "
                                + "Check the ITEM CANDIDATES section of the diagnostics dump "
                                + "for the correct spelling.");
        }
    }
}
