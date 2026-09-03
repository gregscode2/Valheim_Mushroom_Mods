using Newtonsoft.Json;
using UnityEngine;

namespace SeparateSpawns
{
    internal static class LayoutSync
    {
        private const string RpcName = "SeparateSpawns.SyncLayout";
        private static bool _registered;
        private static ZRoutedRpc _registeredInstance;

        public static void Register()
        {
            if (ZRoutedRpc.instance == null)
            {
                return;
            }

            if (_registered && ReferenceEquals(_registeredInstance, ZRoutedRpc.instance))
            {
                return;
            }

            ZRoutedRpc.instance.Register<string>(RpcName, OnReceiveLayout);
            ZRoutedRpc.instance.Register<long>("SeparateSpawns.RequestLayout", OnRequestLayout);
            _registered = true;
            _registeredInstance = ZRoutedRpc.instance;
        }

        private static void OnRequestLayout(long sender, long peerId)
        {
            if (!ZNet.instance.IsServer())
            {
                return;
            }

            if (Plugin.LayoutCache.Current == null)
            {
                var worldUid = ZNet.instance.GetWorldUID();
                if (worldUid != 0)
                {
                    var existing = WorldLayoutStore.Load(worldUid);
                    if (existing != null)
                    {
                        Plugin.LayoutCache.Set(existing);
                    }
                }
            }

            if (Plugin.LayoutCache.Current == null)
            {
                ModLog.Warning($"Layout request from peer {sender} ignored; server layout is unavailable.");
                return;
            }

            ModLog.Info($"Sending layout to peer {sender} ({Plugin.LayoutCache.Current.GroupSpawnPositions.Count} spawns).");
            SendToPeer(sender, Plugin.LayoutCache.Current);
        }

        public static void RequestLayoutFromServer()
        {
            if (ZNet.instance == null || ZNet.instance.IsServer())
            {
                return;
            }

            if (!ClientSyncHelper.CanReachServer())
            {
                return;
            }

            ModLog.Info("Requesting world layout from server...");
            DirectPeerSync.RequestFromServer();
            if (ZRoutedRpc.instance != null)
            {
                ZRoutedRpc.instance.InvokeRoutedRPC("SeparateSpawns.RequestLayout", ZNet.GetUID());
            }
        }

        public static void Broadcast(WorldLayoutData layoutData)
        {
            if (!ZNet.instance.IsServer() || layoutData == null)
            {
                return;
            }

            SendToAll(layoutData);
        }

        public static void SendToPeer(long peerId, WorldLayoutData layoutData)
        {
            if (!ZNet.instance.IsServer() || layoutData == null)
            {
                return;
            }

            var payload = JsonConvert.SerializeObject(layoutData, JsonSettings.Compact);
            ZRoutedRpc.instance.InvokeRoutedRPC(peerId, RpcName, payload);
        }

        private static void SendToAll(WorldLayoutData layoutData)
        {
            var payload = JsonConvert.SerializeObject(layoutData, JsonSettings.Compact);
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcName, payload);
        }

        public static void ApplyPayload(string payload, string source)
        {
            if (ZNet.instance != null && ZNet.instance.IsServer() && Plugin.LayoutCache.Current != null)
            {
                return;
            }

            if (string.IsNullOrEmpty(payload))
            {
                ModLog.Warning($"Ignored empty layout payload from {source}.");
                return;
            }

            try
            {
                var layout = JsonConvert.DeserializeObject<WorldLayoutData>(payload, JsonSettings.Compact);
                if (layout != null)
                {
                    Plugin.LayoutCache.Set(layout);
                    ModLog.Info(
                        $"Received world layout from server ({source}, {layout.GroupSpawnPositions.Count} group spawns).");
                }
            }
            catch (JsonException ex)
            {
                ModLog.Warning($"Failed to deserialize layout payload from {source}: {ex.Message}");
            }
        }

        private static void OnReceiveLayout(long sender, string payload)
        {
            ApplyPayload(payload, "routed RPC");
        }
    }
}
