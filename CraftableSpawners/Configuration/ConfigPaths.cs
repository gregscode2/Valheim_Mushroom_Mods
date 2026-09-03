using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using BepInEx;
using BepInEx.Configuration;

namespace CraftableSpawners.Configuration;

/// <summary>
/// Client config: <c>Valheim/BepInEx/config/</c>.
/// Dedicated server config: <c>Valheim/config/bepinex/</c>.
/// </summary>
internal static class ConfigPaths
{
    internal const string ConfigFileName = "CraftableSpawners.cfg";

    /// <summary>Client layout: <c>Valheim/BepInEx/config/CraftableSpawners.cfg</c>.</summary>
    internal static string ClientPath =>
        Path.Combine(Paths.GameRootPath, "BepInEx", "config", ConfigFileName);

    /// <summary>Dedicated server layout: <c>Valheim/config/bepinex/CraftableSpawners.cfg</c>.</summary>
    internal static string ServerPath =>
        Path.Combine(Paths.GameRootPath, "config", "bepinex", ConfigFileName);

    internal static bool IsDedicatedServer()
    {
        string processName = Process.GetCurrentProcess().ProcessName;
        if (processName.IndexOf("valheim_server", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        foreach (string arg in Environment.GetCommandLineArgs())
        {
            if (string.Equals(arg, "-batchmode", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Load order:
    /// 1. Client <c>BepInEx/config/</c> when present (client save target).
    /// 2. Server <c>config/bepinex/</c> when present (server save target / fallback).
    /// 3. When both exist, server file overlays client values on conflict.
    /// 4. If neither exists, create at the path for this runtime (client vs dedicated server).
    /// </summary>
    internal static ConfigFile CreateMergedConfig()
    {
        string client = ClientPath;
        string server = ServerPath;
        bool clientExists = File.Exists(client);
        bool serverExists = File.Exists(server);
        bool dedicated = IsDedicatedServer();

        string activePath;
        if (clientExists)
            activePath = client;
        else if (serverExists)
            activePath = server;
        else
            activePath = dedicated ? server : client;

        var config = new ConfigFile(activePath, true);

        if (clientExists && serverExists)
        {
            CraftableSpawnersPlugin.Log.LogInfo(
                $"Config: using {client} with overlay from {server}.");
        }
        else if (serverExists)
        {
            CraftableSpawnersPlugin.Log.LogInfo($"Config: loaded dedicated server path {server}.");
        }
        else if (clientExists)
        {
            CraftableSpawnersPlugin.Log.LogInfo($"Config: loaded client path {client}.");
        }
        else
        {
            CraftableSpawnersPlugin.Log.LogInfo(
                dedicated
                    ? $"Config: no file yet; will create {server}."
                    : $"Config: no file yet; will create {client} (also reads {server} when present).");
        }

        return config;
    }

    /// <summary>
    /// When both client and server config files exist, apply server values on top.
    /// </summary>
    internal static void ApplyAlternateOverlay(ConfigFile config)
    {
        if (!File.Exists(ClientPath) || !File.Exists(ServerPath))
            return;

        var overlay = new ConfigFile(ServerPath, false);
        foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> pair in overlay)
        {
            foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> bound in config)
            {
                if (!bound.Key.Equals(pair.Key))
                    continue;

                bound.Value.SetSerializedValue(pair.Value.GetSerializedValue());
                break;
            }
        }
    }
}
