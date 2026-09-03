using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using UnityEngine;

namespace SeparateSpawns
{
    internal static class ModPaths
    {
        public static string RosterFile => "SeparateSpawns.groups.json";

        public static string PluginConfigFile => "abortipus.separatespawns.cfg";

        private static bool? _useDedicatedLayout;

        /// <summary>
        /// Client default: Valheim/BepInEx/config
        /// </summary>
        public static string GetClientConfigRoot()
        {
            return Paths.ConfigPath;
        }

        /// <summary>
        /// Dedicated server default: Valheim/config/bepinex (lowercase folder name).
        /// </summary>
        public static string GetDedicatedConfigRoot()
        {
            var gameRoot = GetGameRoot();
            if (string.IsNullOrEmpty(gameRoot))
            {
                return null;
            }

            return Path.Combine(gameRoot, "config", "bepinex");
        }

        /// <summary>
        /// Back-compat alias for logging/tools.
        /// </summary>
        public static string GetAlternateConfigRoot() => GetDedicatedConfigRoot();

        public static bool UseDedicatedConfigLayout()
        {
            if (_useDedicatedLayout.HasValue)
            {
                return _useDedicatedLayout.Value;
            }

            if (HasDedicatedLaunchFlag() || PlatformIdHelper.IsHeadlessServerContext())
            {
                _useDedicatedLayout = true;
                return true;
            }

            _useDedicatedLayout = false;
            return false;
        }

        public static IEnumerable<string> GetConfigRoots()
        {
            var clientRoot = GetClientConfigRoot();
            var dedicatedRoot = GetDedicatedConfigRoot();

            if (UseDedicatedConfigLayout())
            {
                if (!string.IsNullOrEmpty(dedicatedRoot))
                {
                    yield return dedicatedRoot;
                }

                if (!string.IsNullOrEmpty(clientRoot) &&
                    !PathsEqual(clientRoot, dedicatedRoot))
                {
                    yield return clientRoot;
                }

                yield break;
            }

            if (!string.IsNullOrEmpty(clientRoot))
            {
                yield return clientRoot;
            }

            if (!string.IsNullOrEmpty(dedicatedRoot) &&
                !PathsEqual(clientRoot, dedicatedRoot))
            {
                yield return dedicatedRoot;
            }
        }

        public static string GetDefaultConfigRoot()
        {
            if (UseDedicatedConfigLayout())
            {
                var dedicatedRoot = GetDedicatedConfigRoot();
                if (!string.IsNullOrEmpty(dedicatedRoot))
                {
                    return dedicatedRoot;
                }
            }

            return GetClientConfigRoot();
        }

        /// <summary>
        /// Returns the first existing config path among supported roots, or the default write path.
        /// </summary>
        public static string ResolveConfigPath(string relativePath)
        {
            foreach (var root in GetConfigRoots())
            {
                var path = Path.Combine(root, relativePath);
                if (File.Exists(path) || Directory.Exists(path))
                {
                    return path;
                }
            }

            return Path.Combine(GetDefaultConfigRoot(), relativePath);
        }

        /// <summary>
        /// Prefer an existing file's directory; otherwise use the default root for this host type.
        /// </summary>
        public static string GetWriteConfigPath(string relativePath)
        {
            foreach (var root in GetConfigRoots())
            {
                var path = Path.Combine(root, relativePath);
                if (File.Exists(path))
                {
                    return path;
                }
            }

            var parentRelative = Path.GetDirectoryName(relativePath);
            if (!string.IsNullOrEmpty(parentRelative))
            {
                foreach (var root in GetConfigRoots())
                {
                    var parent = Path.Combine(root, parentRelative);
                    if (Directory.Exists(parent))
                    {
                        return Path.Combine(root, relativePath);
                    }
                }
            }

            return Path.Combine(GetDefaultConfigRoot(), relativePath);
        }

        /// <summary>
        /// Resolves abortipus.separatespawns.cfg from the first root that contains it, otherwise the default write path.
        /// </summary>
        public static string GetPluginConfigPath()
        {
            foreach (var root in GetConfigRoots())
            {
                var path = Path.Combine(root, PluginConfigFile);
                if (File.Exists(path))
                {
                    return path;
                }
            }

            return GetWriteConfigPath(PluginConfigFile);
        }

        private static string GetGameRoot()
        {
            if (string.IsNullOrEmpty(Paths.BepInExRootPath))
            {
                return null;
            }

            return Path.GetDirectoryName(Paths.BepInExRootPath);
        }

        private static bool HasDedicatedLaunchFlag()
        {
            try
            {
                foreach (var arg in Environment.GetCommandLineArgs())
                {
                    if (arg.Equals("-dedicated", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // Ignore and fall back to runtime detection.
            }

            return false;
        }

        private static bool PathsEqual(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            {
                return false;
            }

            return string.Equals(
                Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
