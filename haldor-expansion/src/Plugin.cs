using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace HaldorExpansion
{
    [BepInPlugin(PluginId, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginId = "nicks.haldorexpansion";
        public const string PluginName = "Haldor Expansion";
        public const string PluginVersion = "0.3.0";

        internal static ManualLogSource Log;
        internal static ModConfig Settings;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            Settings = new ModConfig(Config);

            _harmony = new Harmony(PluginId);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            TradeConfigSync.Initialize(_harmony);

            Log.LogInfo(PluginName + " " + PluginVersion + " loaded.");
            Log.LogInfo("Trade table hash: " + TradeTable.Hash
                        + " -- on a server, clients should match this after config sync.");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
