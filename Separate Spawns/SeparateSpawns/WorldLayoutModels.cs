using System.Collections.Generic;
using UnityEngine;

namespace SeparateSpawns
{
    internal sealed class CandidateSpawnPoint
    {
        public Vector3 Position;
        public int MeadowsPatchId;
        public int AdjacentForestPatchId;
        public int IslandId;
        public int NearbyBurialChambers;
        public float MeadowsAreaSquareMeters;
        public Vector3? ExistingEikthyr;
    }

    internal sealed class LayoutAssignment
    {
        public Dictionary<string, CandidateSpawnPoint> GroupSpawns = new Dictionary<string, CandidateSpawnPoint>();
        public float Score;
        public float IslandScore;
        public float DistanceScore;
        public float MeadowsSizeScore;
        public float ClosestSpawnDistance;
        public float AverageMeadowsAreaSquareMeters;
        public bool Complete;
        public int GroupsPlaced;
    }

    internal sealed class LayoutGenerationResult
    {
        public List<LayoutAssignment> Layouts = new List<LayoutAssignment>();
        public int TotalAttempts;
        public int ValidLayouts;
        public LayoutAssignment LastAttempt = new LayoutAssignment();
        public LayoutAssignment BestPartialAttempt = new LayoutAssignment();
    }

    internal sealed class WorldLayoutData
    {
        public bool Frozen;
        public bool Failed;
        public string FailureReason;
        public Dictionary<string, Vector3> GroupSpawnPositions = new Dictionary<string, Vector3>();
        public Dictionary<string, Vector3> SpawnedEikthyrPositions = new Dictionary<string, Vector3>();
        public Dictionary<string, bool> PortalActivated = new Dictionary<string, bool>();
        public List<LayoutAssignment> TopLayouts = new List<LayoutAssignment>();
        public Vector3 SacrificialStonesPosition;
        public List<Vector3> BurialChamberPositions = new List<Vector3>();
        public List<Vector3> EikthyrAltarPositions = new List<Vector3>();
    }

    internal sealed class WorldLayoutCache
    {
        public WorldLayoutData Current { get; private set; }

        public void Set(WorldLayoutData data)
        {
            Current = data;
        }

        public Vector3? GetSpawnForGroup(string groupName)
        {
            if (Current == null || string.IsNullOrEmpty(groupName))
            {
                return null;
            }

            if (Current.GroupSpawnPositions.TryGetValue(groupName, out var position))
            {
                return position;
            }

            return null;
        }
    }
}
