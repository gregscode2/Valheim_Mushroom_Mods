using System.Collections;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace SeparateSpawns
{
    internal static class WorldBootstrap
    {
        private static Plugin _plugin;
        private static bool _subscribed;
        private static bool _bootstrapStarted;

        public static void Initialize(Plugin plugin)
        {
            _plugin = plugin;
            if (_subscribed || ZoneSystem.instance == null)
            {
                return;
            }

            ZoneSystem.instance.GenerateLocationsCompleted += OnLocationsGenerated;
            _subscribed = true;
            ModLog.Info("Separate Spawns subscribed to location generation.");

            if (IsLocationsGenerated())
            {
                ModLog.Info("Locations already generated; running bootstrap now.");
                _plugin.StartCoroutine(BootstrapWhenReady());
            }
        }

        public static void Shutdown()
        {
            if (_subscribed && ZoneSystem.instance != null)
            {
                ZoneSystem.instance.GenerateLocationsCompleted -= OnLocationsGenerated;
            }

            _subscribed = false;
            _bootstrapStarted = false;
        }

        private static void OnLocationsGenerated()
        {
            ModLog.Info("GenerateLocationsCompleted fired.");
            if (!ZNet.instance.IsServer())
            {
                LoadExistingLayoutForClient();
                return;
            }

            _plugin.StartCoroutine(BootstrapWhenReady());
        }

        private static IEnumerator BootstrapWhenReady()
        {
            if (_bootstrapStarted)
            {
                yield break;
            }

            _bootstrapStarted = true;

            for (var attempt = 0; attempt < 60; attempt++)
            {
                if (ZNet.instance != null && ZNet.instance.IsServer() && ZNet.GetWorldIfIsHost() != null && ZRoutedRpc.instance != null)
                {
                    LayoutSync.Register();
                    RosterSync.Register();
                    PortalActivationSync.Register();
                    yield return BootstrapServer();
                    yield break;
                }

                yield return new WaitForSeconds(0.5f);
            }

            ModLog.Error("Separate Spawns bootstrap timed out waiting for server world load.");
            _bootstrapStarted = false;
        }

        private static bool IsLocationsGenerated()
        {
            var property = AccessTools.Property(typeof(ZoneSystem), "LocationsGenerated");
            if (property == null)
            {
                return false;
            }

            return property.GetValue(ZoneSystem.instance) is bool generated && generated;
        }

        private static void LoadExistingLayoutForClient()
        {
            if (ZNet.instance.GetWorldUID() == 0)
            {
                return;
            }

            LayoutSync.Register();
            RosterSync.Register();
            PortalActivationSync.Register();

            var existing = WorldLayoutStore.Load(ZNet.instance.GetWorldUID());
            if (existing != null)
            {
                Plugin.LayoutCache.Set(existing);
            }
            else
            {
                LayoutSync.RequestLayoutFromServer();
            }

            RosterSync.RequestFromServer();
        }

        private static IEnumerator BootstrapServer()
        {
            yield return null;

            var world = ZNet.GetWorldIfIsHost();
            if (world == null)
            {
                ModLog.Error("Bootstrap aborted: host world was not available.");
                _bootstrapStarted = false;
                yield break;
            }

            ModLog.Info($"Bootstrapping Separate Spawns for world '{world.m_name}' (uid {world.m_uid}, seed {world.m_seedName}).");

            if (Plugin.Roster.GetGroupNames().Count == 0)
            {
                RosterSync.LoadServerRosterFromDisk();
            }

            var existing = WorldLayoutStore.Load(world.m_uid);
            if (existing != null && (existing.Frozen || existing.GroupSpawnPositions.Count > 0))
            {
                if (existing.SacrificialStonesPosition == Vector3.zero)
                {
                    var stonesCatalog = LocationCatalog.Build(Plugin.ConfigValues);
                    existing.SacrificialStonesPosition = stonesCatalog.SacrificialStones;
                }

                Plugin.LayoutCache.Set(existing);
                TryApplySpawnDifficulties(existing);
                LayoutSync.Broadcast(existing);
                RosterSync.Broadcast();
                PortalManager.PlacePortals(existing, Plugin.ConfigValues);
                ModLog.Info("Loaded existing Separate Spawns layout.");
                yield break;
            }

            if (existing != null && existing.Failed)
            {
                ModLog.Error($"Separate Spawns previously failed for this world: {existing.FailureReason}");
                yield break;
            }

            var config = Plugin.ConfigValues;
            var groupNames = Plugin.Roster.GetGroupNames().ToList();
            if (groupNames.Count == 0)
            {
                ModLog.Warning("No groups configured; Separate Spawns disabled.");
                yield break;
            }

            ModLog.Info($"Generating Separate Spawns layout for {groupNames.Count} groups...");
            var map = BiomeMapBuilder.Build(
                config.BiomeStep.Value,
                config.GridStep.Value,
                config.InnerRadius.Value,
                config.BiomeSplitGapDistance.Value,
                config.IslandSplitGapDistance.Value,
                config.MinPatchArea.Value);
            var locations = LocationCatalog.Build(config);
            map.CountBurialChambers(locations);
            map.LogPatchStatistics();
            var candidates = CandidateSpawnFinder.Find(map, locations, config, out var rejections);
            ModLog.Info($"Found {candidates.Count} eligible candidate spawn points.");
            var generation = LayoutGenerator.GenerateLayouts(groupNames, candidates, config);
            LayoutGenerator.ApplyRelativeScores(generation.Layouts, config);

            if (generation.ValidLayouts == 0)
            {
                var seedRerollAttempt = SeedRerollStore.GetAttemptCount(world.m_name) + 1;
                var maxSeedRerolls = Plugin.ConfigValues.MaxSeedRerolls.Value;
                var reason =
                    $"No valid layouts after {generation.TotalAttempts} attempts (valid={generation.ValidLayouts}, candidates={candidates.Count}, lastPlaced={generation.LastAttempt.GroupsPlaced}, bestPartial={generation.BestPartialAttempt.GroupsPlaced}, checked={rejections.TotalChecked}, meadows={rejections.NotMeadows}, forest={rejections.NoNearbyForest}, coast={rejections.NoAdjacentCoast}, stones={rejections.TooCloseToStones}, chambers={rejections.NotEnoughChambers}, water={rejections.Underwater})";
                ModLog.Error(reason);
                ModLog.Error($"Seed reroll attempt {seedRerollAttempt}/{maxSeedRerolls} for world '{world.m_name}' (seed {world.m_seedName}).");
                if (config.EnableLayoutReports.Value)
                {
                    LayoutReportWriter.WriteFailureReport(
                        world.m_uid,
                        world.m_name,
                        world.m_seedName,
                        seedRerollAttempt,
                        maxSeedRerolls,
                        map,
                        locations,
                        generation,
                        candidates.Count,
                        rejections,
                        config,
                        reason);
                }

                HandleInfeasibleSeed(world, reason, generation);
                yield break;
            }

            var layouts = generation.Layouts.OrderByDescending(layout => layout.Score).ToList();
            var reportCount = config.EnableLayoutReports.Value
                ? Mathf.Max(1, config.TopReportCount.Value)
                : 1;
            var topLayouts = LayoutGenerator.SelectDiverseLayouts(
                layouts,
                reportCount,
                config.LayoutDiversityDistance.Value);
            if (topLayouts.Count == 0)
            {
                topLayouts = layouts.Take(Mathf.Min(reportCount, layouts.Count)).ToList();
            }

            var winner = topLayouts[0];
            var layoutData = new WorldLayoutData
            {
                Frozen = false,
                SacrificialStonesPosition = locations.SacrificialStones,
                BurialChamberPositions = locations.BurialChambers,
                EikthyrAltarPositions = locations.EikthyrAltars,
                TopLayouts = topLayouts
            };

            foreach (var pair in winner.GroupSpawns)
            {
                layoutData.GroupSpawnPositions[pair.Key] = pair.Value.Position;
                ModLog.Info($"Group {pair.Key} spawn: ({pair.Value.Position.x:F0}, {pair.Value.Position.z:F0})");
                EikthyrPlacer.EnsureAltarNearSpawn(pair.Key, pair.Value.Position, config, layoutData);
            }

            var difficulties = SpawnDifficultyRanker.RankGroups(winner, map);
            Plugin.Roster.ApplySpawnDifficulties(difficulties);

            Plugin.LayoutCache.Set(layoutData);
            WorldLayoutStore.Save(world.m_uid, layoutData);
            if (config.EnableLayoutReports.Value)
            {
                LayoutReportWriter.WriteReports(
                    world.m_uid,
                    world.m_name,
                    world.m_seedName,
                    map,
                    locations,
                    topLayouts,
                    candidates.Count,
                    rejections,
                    config);
            }

            PortalManager.PlacePortals(layoutData, config);
            LayoutSync.Broadcast(layoutData);
            RosterSync.Broadcast();

            ModLog.Info(
                $"Chosen layout score={winner.Score:F2} (islands={winner.IslandScore:F2}, distance={winner.DistanceScore:F2}, meadowsSize={winner.MeadowsSizeScore:F2}, closest={winner.ClosestSpawnDistance:F0}m, avgMeadowsArea={winner.AverageMeadowsAreaSquareMeters:F0}m2).");
        }

        public static void MarkWorldFrozen()
        {
            if (Plugin.LayoutCache.Current == null || ZNet.instance.GetWorldUID() == 0)
            {
                return;
            }

            if (Plugin.LayoutCache.Current.Frozen)
            {
                return;
            }

            Plugin.LayoutCache.Current.Frozen = true;
            WorldLayoutStore.Save(ZNet.instance.GetWorldUID(), Plugin.LayoutCache.Current);
        }

        private static void HandleInfeasibleSeed(World world, string reason, LayoutGenerationResult generation)
        {
            var attempts = SeedRerollStore.GetAttemptCount(world.m_name);
            if (attempts >= Plugin.ConfigValues.MaxSeedRerolls.Value)
            {
                ModLog.Error(
                    $"Giving up after {attempts} seed reroll attempts and {generation.TotalAttempts} layout attempts with {generation.ValidLayouts} valid layouts.");
                var failed = new WorldLayoutData
                {
                    Failed = true,
                    FailureReason = reason
                };
                Plugin.LayoutCache.Set(failed);
                WorldLayoutStore.Save(world.m_uid, failed);
                ModLog.Error("Maximum seed rerolls reached. Falling back to vanilla spawns.");
                return;
            }

            SeedRerollStore.IncrementAttempt(world.m_name);
            ModLog.Warning(
                $"Seed reroll attempt {attempts + 1}/{Plugin.ConfigValues.MaxSeedRerolls.Value} after {generation.TotalAttempts} layout attempts with 0 valid layouts. Regenerating world {world.m_name}.");

            try
            {
                var worldName = world.m_name;
                var fileSource = WorldDeleteHelper.GetWorldFileSource(world);
                var oldSeed = world.m_seedName;

                // Critical: quit-with-save would rewrite the deleted world and crash / undo the reroll.
                WorldDeleteHelper.ShutdownGameWithoutSaving();
                WorldDeleteHelper.RemoveLoadedWorld(world);

                var newSeed = World.GenerateSeed();
                var replacement = new World(worldName, newSeed);
                WorldDeleteHelper.SetWorldFileSource(replacement, fileSource);
                replacement.m_needsDB = false;
                if (world.m_startingGlobalKeys != null && world.m_startingGlobalKeys.Count > 0)
                {
                    replacement.m_startingGlobalKeys.AddRange(world.m_startingGlobalKeys);
                    replacement.m_startingKeysChanged = true;
                }

                replacement.SaveWorldMetaData(System.DateTime.Now);
                ModLog.Error(
                    $"World '{worldName}' regenerated: seed {oldSeed} -> {newSeed}. Restart the server/client to continue.");
            }
            catch (System.Exception ex)
            {
                ModLog.Error($"Seed reroll failed while regenerating world: {ex}");
            }

            Application.Quit();
        }

        private static void TryApplySpawnDifficulties(WorldLayoutData layoutData)
        {
            if (layoutData?.GroupSpawnPositions == null || layoutData.GroupSpawnPositions.Count == 0)
            {
                return;
            }

            if (!Plugin.Roster.NeedsDifficultyAssignment(layoutData.GroupSpawnPositions.Keys))
            {
                return;
            }

            var config = Plugin.ConfigValues;
            ModLog.Info("Assigning missing spawn difficulties from the frozen layout.");
            var map = BiomeMapBuilder.Build(
                config.BiomeStep.Value,
                config.GridStep.Value,
                config.InnerRadius.Value,
                config.BiomeSplitGapDistance.Value,
                config.IslandSplitGapDistance.Value,
                config.MinPatchArea.Value);
            var difficulties = SpawnDifficultyRanker.RankGroups(layoutData.GroupSpawnPositions, map);
            Plugin.Roster.ApplySpawnDifficulties(difficulties);
        }
    }
}
