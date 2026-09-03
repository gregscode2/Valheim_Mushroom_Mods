using HarmonyLib;
using UnityEngine;

namespace SeparateSpawns.Patches
{
    [HarmonyPatch(typeof(TeleportWorld), "Awake")]
    internal static class TeleportWorldAwakePatch
    {
        private static void Postfix(TeleportWorld __instance)
        {
            GroupPortalMarker.AttachFromZdoIfNeeded(__instance.gameObject);
        }
    }

    [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.Interact))]
    internal static class TeleportWorldInteractPatch
    {
        private static bool Prefix(TeleportWorld __instance, Humanoid human, bool hold, ref bool __result)
        {
            if (hold)
            {
                return true;
            }

            var marker = GroupPortalMarker.AttachFromZdoIfNeeded(__instance.gameObject);
            if (marker == null)
            {
                return true;
            }

            // Group portals never open the vanilla tag editor.
            if (marker.IsSpawnEnd && !marker.Activated)
            {
                __result = PortalManager.TryActivatePortal(marker, human);
                return false;
            }

            if (!marker.Activated)
            {
                __result = false;
                human.Message(MessageHud.MessageType.Center, "This group portal is inactive.");
                return false;
            }

            __result = false;
            human.Message(MessageHud.MessageType.Center, "This portal's tag is fixed.");
            return false;
        }
    }

    [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.GetHoverText))]
    internal static class TeleportWorldGetHoverTextPatch
    {
        private static void Postfix(TeleportWorld __instance, ref string __result)
        {
            var marker = GroupPortalMarker.AttachFromZdoIfNeeded(__instance.gameObject);
            if (marker == null)
            {
                return;
            }

            var status = marker.Activated ? "active" : "inactive";
            var end = marker.IsSpawnEnd ? "spawn" : "stones";
            var action = marker.IsSpawnEnd && !marker.Activated
                ? "\n[<color=yellow><b>Use</b></color>] Activate with surtling cores"
                : "\nTag is fixed";
            __result = $"Portal tag:\"{marker.GroupName}\" ({end}, {status}){action}";
        }
    }

    [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.SetText))]
    internal static class TeleportWorldSetTextPatch
    {
        private static bool Prefix(TeleportWorld __instance)
        {
            var marker = GroupPortalMarker.AttachFromZdoIfNeeded(__instance.gameObject);
            if (marker == null)
            {
                return true;
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(TeleportWorld), "RPC_SetTag")]
    internal static class TeleportWorldRpcSetTagPatch
    {
        private static bool Prefix(TeleportWorld __instance)
        {
            var marker = GroupPortalMarker.AttachFromZdoIfNeeded(__instance.gameObject);
            if (marker == null)
            {
                return true;
            }

            // Keep the fixed group tag if something tries to overwrite it.
            var zdo = __instance.GetComponent<ZNetView>()?.GetZDO();
            if (zdo != null)
            {
                PortalManager.ApplyGroupTag(zdo, marker.GroupName);
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.Teleport))]
    internal static class TeleportWorldTeleportPatch
    {
        private static bool Prefix(TeleportWorld __instance, Player player)
        {
            var marker = GroupPortalMarker.AttachFromZdoIfNeeded(__instance.gameObject);
            if (marker == null)
            {
                return true;
            }

            if (!marker.Activated)
            {
                player.Message(MessageHud.MessageType.Center, "This group portal is inactive.");
                return false;
            }

            if (!PortalManager.CanUsePortal(marker, player))
            {
                player.Message(MessageHud.MessageType.Center, "This portal belongs to another group.");
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(WearNTear), "ApplyDamage")]
    internal static class WearNTearApplyDamagePatch
    {
        private static bool Prefix(WearNTear __instance, ref bool __result)
        {
            if (__instance.GetComponent<GroupPortalMarker>() != null ||
                GroupPortalMarker.AttachFromZdoIfNeeded(__instance.gameObject) != null)
            {
                __result = false;
                return false;
            }

            return true;
        }
    }
}
