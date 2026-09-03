using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace SeparateSpawns
{
    internal static class PortalObstacleClearer
    {
        private const float ClearRadius = 2f;
        private static readonly int ObstacleMask = LayerMask.GetMask("Default", "static_solid", "Default_small");

        public static int ClearAt(Vector3 position)
        {
            if (!ZNet.instance.IsServer())
            {
                return 0;
            }

            var colliders = OverlapSphere(position, ClearRadius, ObstacleMask);
            if (colliders.Count == 0)
            {
                return 0;
            }

            var destroyed = new HashSet<int>();
            var count = 0;

            foreach (var colliderObject in colliders)
            {
                if (colliderObject == null)
                {
                    continue;
                }

                var target = FindClearTarget(colliderObject);
                if (target == null || destroyed.Contains(target.GetInstanceID()))
                {
                    continue;
                }

                if (IsProtectedObject(target))
                {
                    continue;
                }

                if (DestroyObstacle(target))
                {
                    destroyed.Add(target.GetInstanceID());
                    count++;
                }
            }

            if (count > 0)
            {
                ModLog.Info(
                    $"Cleared {count} tree/rock obstacle(s) within {ClearRadius:F0}m of portal at ({position.x:F0}, {position.y:F1}, {position.z:F0}).");
            }

            return count;
        }

        private static GameObject FindClearTarget(GameObject hit)
        {
            var tree = hit.GetComponentInParent<TreeBase>();
            if (tree != null)
            {
                return tree.gameObject;
            }

            var log = hit.GetComponentInParent<TreeLog>();
            if (log != null)
            {
                return log.gameObject;
            }

            var rock = hit.GetComponentInParent<MineRock>();
            if (rock != null)
            {
                return rock.gameObject;
            }

            var rock5 = hit.GetComponentInParent<MineRock5>();
            if (rock5 != null)
            {
                return rock5.gameObject;
            }

            return null;
        }

        private static bool IsProtectedObject(GameObject target)
        {
            if (target.GetComponent<GroupPortalMarker>() != null ||
                target.GetComponentInParent<GroupPortalMarker>() != null)
            {
                return true;
            }

            if (target.GetComponent<TeleportWorld>() != null ||
                target.GetComponentInParent<TeleportWorld>() != null)
            {
                return true;
            }

            if (target.GetComponent<Piece>() != null ||
                target.GetComponentInParent<Piece>() != null)
            {
                return true;
            }

            return false;
        }

        private static bool DestroyObstacle(GameObject target)
        {
            var nview = target.GetComponent<ZNetView>();
            if (nview != null && nview.IsValid())
            {
                if (!nview.IsOwner())
                {
                    nview.ClaimOwnership();
                }

                nview.Destroy();
                return true;
            }

            if (ZNetScene.instance != null)
            {
                ZNetScene.instance.Destroy(target);
                return true;
            }

            var nviewOnParent = target.GetComponentInParent<ZNetView>();
            if (nviewOnParent != null && nviewOnParent.IsValid())
            {
                nviewOnParent.Destroy();
                return true;
            }

            Object.Destroy(target);
            return true;
        }

        private static System.Collections.Generic.List<GameObject> OverlapSphere(Vector3 position, float radius, int layerMask)
        {
            var results = new List<GameObject>();
            var physicsType = typeof(Object).Assembly.GetType("UnityEngine.Physics");
            if (physicsType == null)
            {
                foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    physicsType = assembly.GetType("UnityEngine.Physics");
                    if (physicsType != null)
                    {
                        break;
                    }
                }
            }

            if (physicsType == null)
            {
                return results;
            }

            var method = physicsType.GetMethod(
                "OverlapSphere",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(Vector3), typeof(float), typeof(int) },
                null);

            if (!(method?.Invoke(null, new object[] { position, radius, layerMask }) is System.Array colliders))
            {
                return results;
            }

            foreach (var collider in colliders)
            {
                if (collider == null)
                {
                    continue;
                }

                var gameObject = collider.GetType().GetProperty("gameObject")?.GetValue(collider, null) as GameObject;
                if (gameObject != null)
                {
                    results.Add(gameObject);
                }
            }

            return results;
        }
    }
}
