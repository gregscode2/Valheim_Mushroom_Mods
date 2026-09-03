using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Configuration;
using UnityEngine;

namespace CombatAdjustments.ShieldRework;

/// <summary>
/// Client installs use <c>BepInEx/config/</c>. Dedicated servers use
/// <c>config/bepinex/</c> (lowercase). Clients may overlay the server layout
/// when both files exist.
/// </summary>
internal static class ConfigPaths
{
    /// <summary>Serialized values from the overlay file, keyed section+key.</summary>
    internal static readonly Dictionary<string, string> OverlayDefaults =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary><c>Valheim/BepInEx/config/{guid}.cfg</c> — client layout.</summary>
    internal static string ClientPath(string pluginGuid) =>
        Path.Combine(BepInEx.Paths.ConfigPath, pluginGuid + ".cfg");

    /// <summary><c>Valheim/config/bepinex/{guid}.cfg</c> — dedicated-server layout.</summary>
    internal static string DedicatedPath(string pluginGuid) =>
        Path.Combine(BepInEx.Paths.GameRootPath, "config", "bepinex", pluginGuid + ".cfg");

    internal static bool UseDedicatedLayout => Application.isBatchMode;

    /// <summary>
    /// Dedicated server: load/save <c>config/bepinex/</c>.
    /// Client: load/save <c>BepInEx/config/</c>, overlay <c>config/bepinex/</c> when both exist.
    /// </summary>
    internal static ConfigFile CreateMergedConfig(string pluginGuid)
    {
        OverlayDefaults.Clear();

        string client = ClientPath(pluginGuid);
        string dedicated = DedicatedPath(pluginGuid);
        bool clientExists = File.Exists(client);
        bool dedicatedExists = File.Exists(dedicated);

        if (UseDedicatedLayout)
            return CreateDedicatedConfig(pluginGuid, client, dedicated, clientExists, dedicatedExists);

        if (dedicatedExists)
            LoadOverlayDefaults(dedicated);

        string activePath = clientExists ? client : dedicated;
        var config = new ConfigFile(activePath, true);

        if (clientExists && dedicatedExists)
        {
            ShieldReworkPlugin.Log.LogInfo(
                $"Config: client using {client} with overlay from {dedicated}.");
        }
        else if (dedicatedExists)
        {
            ShieldReworkPlugin.Log.LogInfo($"Config: client loaded {dedicated}.");
        }
        else if (clientExists)
        {
            ShieldReworkPlugin.Log.LogInfo($"Config: client loaded {client}.");
        }
        else
        {
            ShieldReworkPlugin.Log.LogInfo(
                $"Config: client — no file yet; will create {client} (also reads {dedicated} when present).");
        }

        return config;
    }

    private static ConfigFile CreateDedicatedConfig(
        string pluginGuid,
        string client,
        string dedicated,
        bool clientExists,
        bool dedicatedExists)
    {
        string activePath = dedicatedExists ? dedicated : client;
        var config = new ConfigFile(activePath, true);

        if (dedicatedExists)
            ShieldReworkPlugin.Log.LogInfo($"Config: dedicated server using {dedicated}.");
        else if (clientExists)
            ShieldReworkPlugin.Log.LogWarning(
                $"Config: dedicated server — no file at {dedicated}; falling back to {client}.");
        else
            ShieldReworkPlugin.Log.LogInfo(
                $"Config: dedicated server — no file yet; will create {dedicated}.");

        return config;
    }

    internal static void ApplyOverlayAfterObjectDbLoad(ConfigFile config)
    {
        if (UseDedicatedLayout)
            return;

        string dedicated = DedicatedPath(ShieldReworkPlugin.PluginGuid);
        if (!File.Exists(dedicated))
            return;

        LoadOverlayDefaults(dedicated);
        ApplyOverlayToBoundEntries(config);
    }

    internal static bool TryGetOverlayDefault(string section, string key, out string serialized)
    {
        return OverlayDefaults.TryGetValue(BuildKey(section, key), out serialized);
    }

    internal static void ApplyOverlayToStaticEntries(ConfigFile config)
    {
        if (UseDedicatedLayout)
            return;

        string client = ClientPath(ShieldReworkPlugin.PluginGuid);
        string dedicated = DedicatedPath(ShieldReworkPlugin.PluginGuid);
        if (!File.Exists(dedicated) || !File.Exists(client))
            return;

        ApplyOverlayAfterObjectDbLoad(config);
    }

    private static void LoadOverlayDefaults(string overlayPath)
    {
        OverlayDefaults.Clear();
        if (!File.Exists(overlayPath))
            return;

        var overlay = new ConfigFile(overlayPath, false);
        foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> pair in overlay)
        {
            OverlayDefaults[BuildKey(pair.Key.Section, pair.Key.Key)] =
                pair.Value.GetSerializedValue() ?? string.Empty;
        }
    }

    private static void ApplyOverlayToBoundEntries(ConfigFile target)
    {
        foreach (KeyValuePair<string, string> pair in OverlayDefaults)
        {
            SplitKey(pair.Key, out string? section, out string? key);
            if (section == null || key == null)
                continue;

            var definition = new ConfigDefinition(section, key);
            foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> bound in target)
            {
                if (bound.Key.Equals(definition))
                {
                    bound.Value.SetSerializedValue(pair.Value);
                    break;
                }
            }
        }
    }

    private static string BuildKey(string? section, string? key) =>
        (section ?? string.Empty) + "\u001f" + (key ?? string.Empty);

    private static void SplitKey(string path, out string? section, out string? key)
    {
        int split = path.IndexOf('\u001f');
        if (split < 0)
        {
            section = null;
            key = null;
            return;
        }

        section = path.Substring(0, split);
        key = path.Substring(split + 1);
    }
}
