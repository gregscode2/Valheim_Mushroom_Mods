using HarmonyLib;

namespace HornOfCalling
{
    /// <summary>
    /// ObjectDB is populated more than once - once for the stripped-down main-menu
    /// copy, then again when the world database is merged over it - so both entry
    /// points register, and registration is idempotent.
    /// </summary>
    [HarmonyPatch(typeof(ObjectDB))]
    internal static class ObjectDbPatches
    {
        [HarmonyPostfix]
        [HarmonyPatch(nameof(ObjectDB.Awake))]
        internal static void AwakePostfix(ObjectDB __instance)
        {
            try
            {
                FrostAxeItem.EnsureRegistered(__instance);
                FrostAxeItem.EnsureRecipeRegistered(__instance);
            }
            catch (System.Exception e) { Plugin.Log.LogError("ObjectDB.Awake registration failed: " + e); }
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(ObjectDB.CopyOtherDB))]
        internal static void CopyOtherDbPostfix(ObjectDB __instance)
        {
            try
            {
                FrostAxeItem.EnsureRegistered(__instance);
                FrostAxeItem.EnsureRecipeRegistered(__instance);
            }
            catch (System.Exception e) { Plugin.Log.LogError("ObjectDB.CopyOtherDB registration failed: " + e); }
        }
    }

    /// <summary>
    /// The point where both the item and its crafting station are certain to exist.
    /// ObjectDB.Awake can run before the station prefabs are loaded, which leaves the
    /// recipe unregistered - this is where that gets picked up.
    /// </summary>
    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class ZNetSceneAwakePatch
    {
        [HarmonyPostfix]
        internal static void Postfix(ZNetScene __instance)
        {
            try
            {
                // ZNetScene.Awake can run before ObjectDB has been populated, so make
                // sure the item exists before registering it for networking.
                FrostAxeItem.EnsureRegistered(ObjectDB.instance);
                FrostAxeItem.EnsureRecipeRegistered(ObjectDB.instance);
                FrostAxeItem.EnsureNetworkRegistered(__instance);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError("Failed to register " + FrostAxeItem.PrefabName + " with ZNetScene: " + e);
            }
        }
    }
}
