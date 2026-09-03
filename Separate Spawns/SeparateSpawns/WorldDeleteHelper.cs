using System;
using System.Reflection;
using HarmonyLib;

namespace SeparateSpawns
{
    internal static class WorldDeleteHelper
    {
        public static void RemoveLoadedWorld(World world)
        {
            if (world == null)
            {
                return;
            }

            var fileName = string.IsNullOrEmpty(world.m_fileName) ? world.m_name : world.m_fileName;
            var fileSource = AccessTools.Field(typeof(World), "m_fileSource")?.GetValue(world)
                             ?? GetLocalFileSource();
            RemoveWorld(fileName, fileSource);
        }

        public static void RemoveWorld(string worldName, object fileSource)
        {
            var method = typeof(World).GetMethod("RemoveWorld", BindingFlags.Public | BindingFlags.Static);
            if (method == null)
            {
                ModLog.Error("Could not find World.RemoveWorld for seed reroll.");
                return;
            }

            method.Invoke(null, new[] { worldName, fileSource });
        }

        public static void SetWorldFileSource(World world, object fileSource)
        {
            if (world == null || fileSource == null)
            {
                return;
            }

            AccessTools.Field(typeof(World), "m_fileSource")?.SetValue(world, fileSource);
        }

        public static object GetWorldFileSource(World world)
        {
            if (world == null)
            {
                return GetLocalFileSource();
            }

            return AccessTools.Field(typeof(World), "m_fileSource")?.GetValue(world) ?? GetLocalFileSource();
        }

        public static object GetLocalFileSource()
        {
            var type = Type.GetType("FileHelpers+FileSource, assembly_utils")
                       ?? Type.GetType("FileHelpers.FileSource, assembly_utils");
            if (type == null)
            {
                return 1;
            }

            return Enum.ToObject(type, 1);
        }

        /// <summary>
        /// Shuts networking down without writing the doomed world, and marks Game as shutting
        /// down so Application.Quit / OnApplicationQuit does not save over the replacement.
        /// </summary>
        public static void ShutdownGameWithoutSaving()
        {
            try
            {
                if (Game.instance != null)
                {
                    AccessTools.Field(typeof(Game), "m_shuttingDown")?.SetValue(Game.instance, true);
                }

                if (ZNetScene.instance != null)
                {
                    ZNetScene.instance.Shutdown();
                }

                if (ZNet.instance != null)
                {
                    ZNet.instance.ShutdownWithoutSave(false);
                }
            }
            catch (Exception ex)
            {
                ModLog.Error($"Shutdown without save during seed reroll failed: {ex.Message}");
            }
        }
    }
}
