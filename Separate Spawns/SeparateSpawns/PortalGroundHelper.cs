using System.Collections.Generic;
using UnityEngine;

namespace SeparateSpawns
{
    internal static class PortalGroundHelper
    {
        private static float? _cachedBaseOffset;

        public static float GetPrefabBaseToPivotOffset(GameObject prefab)
        {
            if (_cachedBaseOffset.HasValue)
            {
                return _cachedBaseOffset.Value;
            }

            if (prefab == null)
            {
                return 0f;
            }

            _cachedBaseOffset = MeasureBaseToPivotOffset(prefab);
            return _cachedBaseOffset.Value;
        }

        private static float MeasureBaseToPivotOffset(GameObject prefab)
        {
            var wasForceDisable = ZNetView.m_forceDisableInit;
            GameObject temp = null;
            try
            {
                // Never register measurement instances with ZDOMan / ZNetScene.
                ZNetView.m_forceDisableInit = true;
                temp = Object.Instantiate(prefab, new Vector3(0f, 10000f, 0f), Quaternion.identity);
                temp.SetActive(false);
                return MeasureBaseToPivotOffset(temp.transform);
            }
            catch (System.Exception ex)
            {
                ModLog.Warning($"Failed to measure portal prefab bounds: {ex.Message}");
                return 0f;
            }
            finally
            {
                ZNetView.m_forceDisableInit = wasForceDisable;
                if (temp != null)
                {
                    Object.Destroy(temp);
                }
            }
        }

        public static float ResolveGroundY(Vector3 xzPosition, float? preferredY)
        {
            if (preferredY.HasValue)
            {
                return preferredY.Value;
            }

            if (WorldGenerator.instance != null)
            {
                return WorldGenerator.instance.GetHeight(xzPosition.x, xzPosition.z);
            }

            if (ZoneSystem.instance != null && ZoneSystem.instance.GetGroundHeight(xzPosition, out var height))
            {
                return height;
            }

            return xzPosition.y;
        }

        public static Vector3 PivotForGround(GameObject prefab, Vector3 xzPosition, float groundY)
        {
            var baseOffset = GetPrefabBaseToPivotOffset(prefab);
            return new Vector3(xzPosition.x, groundY + baseOffset, xzPosition.z);
        }

        public static void AlignInstanceToGround(GameObject portal, GameObject prefab, float groundY)
        {
            if (portal == null)
            {
                return;
            }

            var targetPivot = PivotForGround(prefab ?? portal, portal.transform.position, groundY);
            portal.transform.position = targetPivot;

            var nview = portal.GetComponent<ZNetView>();
            var zdo = nview?.GetZDO();
            if (zdo != null)
            {
                zdo.SetPosition(targetPivot);
            }
        }

        public static void AlignZdoToGround(ZDO zdo, GameObject prefab, float groundY)
        {
            if (zdo == null)
            {
                return;
            }

            var position = zdo.GetPosition();
            var targetPivot = PivotForGround(prefab, position, groundY);
            zdo.SetPosition(targetPivot);
        }

        public static float MeasureGroundAt(Vector3 xzPosition, float fallbackGroundY)
        {
            if (ZoneSystem.instance != null &&
                Heightmap.FindHeightmap(xzPosition) != null &&
                ZoneSystem.instance.GetGroundHeight(xzPosition, out var height))
            {
                return height;
            }

            return fallbackGroundY;
        }

        private static float MeasureBaseToPivotOffset(Transform root)
        {
            var bounds = CalculateBounds(root.gameObject);
            if (bounds.size.sqrMagnitude <= 0.001f)
            {
                return 0f;
            }

            return root.position.y - bounds.min.y;
        }

        private static Bounds CalculateBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                return new Bounds(go.transform.position, Vector3.zero);
            }

            var result = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                result.Encapsulate(renderers[i].bounds);
            }

            return result;
        }
    }
}
