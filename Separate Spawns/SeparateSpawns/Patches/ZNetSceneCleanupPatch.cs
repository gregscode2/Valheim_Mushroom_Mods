using System.Collections.Generic;
using HarmonyLib;

namespace SeparateSpawns.Patches
{
    /// <summary>
    /// Removes stale ZNetView entries (destroyed objects / null ZDO) before sector cleanup runs.
    /// </summary>
    [HarmonyPatch(typeof(ZNetScene), "CreateDestroyObjects")]
    internal static class ZNetSceneCleanupPatch
    {
        private static void Prefix(ZNetScene __instance)
        {
            CleanupStaleInstances(__instance);
        }

        private static void CleanupStaleInstances(ZNetScene scene)
        {
            var field = AccessTools.Field(typeof(ZNetScene), "m_instances");
            if (field?.GetValue(scene) is not Dictionary<ZDO, ZNetView> instances || instances.Count == 0)
            {
                return;
            }

            var stale = new List<ZDO>();
            foreach (var pair in instances)
            {
                var view = pair.Value;
                if (view == null || view.GetZDO() == null)
                {
                    stale.Add(pair.Key);
                }
            }

            if (stale.Count == 0)
            {
                return;
            }

            foreach (var zdo in stale)
            {
                instances.Remove(zdo);
            }

            ModLog.Warning($"Removed {stale.Count} stale ZNetScene instance(s) left by destroyed objects.");
        }
    }
}
