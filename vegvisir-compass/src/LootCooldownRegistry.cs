using System;
using System.Collections.Generic;
using UnityEngine;

namespace VegvisirCompass
{
    /// <summary>
    /// Server-side record of when each Vegvisir was last looted.
    ///
    /// There are no timers here. A single timestamp per looted stone is stored and
    /// compared lazily when a player interacts, so the cost is one dictionary lookup
    /// per interaction and nothing at all per frame.
    ///
    /// The dictionary is lazy - it gains an entry only when a stone is actually
    /// looted - and entries stop mattering once the cooldown elapses, so a periodic
    /// prune keeps the live set bounded to roughly "stones looted in the last
    /// cooldown window".
    ///
    /// State is deliberately in-memory: cooldowns reset when the server restarts.
    /// </summary>
    internal static class LootCooldownRegistry
    {
        /// <summary>Prune once the map grows past this many entries.</summary>
        private const int PruneThreshold = 256;

        private static readonly Dictionary<long, DateTime> LastLooted = new Dictionary<long, DateTime>();
        private static readonly List<long> StaleKeys = new List<long>();

        /// <summary>
        /// Packs a stone position into a stable key. Vegvisirs are static world
        /// objects, so quantising to half a metre identifies a stone reliably across
        /// sessions without needing a ZNetView on the prefab.
        /// </summary>
        internal static long KeyFor(Vector3 position)
        {
            long x = Mathf.RoundToInt(position.x * 2f);
            long z = Mathf.RoundToInt(position.z * 2f);
            return (x << 32) ^ (z & 0xFFFFFFFFL);
        }

        /// <summary>
        /// Attempts to reserve a stone for looting. Returns false when it is still on
        /// cooldown, in which case <paramref name="remainingSeconds"/> says for how
        /// much longer. On success the stone is marked as looted now.
        /// </summary>
        internal static bool TryClaim(Vector3 stonePosition, float cooldownSeconds, out float remainingSeconds)
        {
            remainingSeconds = 0f;

            // With no cooldown configured there is nothing to enforce, so skip the
            // bookkeeping entirely rather than recording timestamps that would never
            // be read. The dictionary then stays empty and costs nothing.
            if (cooldownSeconds <= 0f)
            {
                return true;
            }

            long key = KeyFor(stonePosition);
            DateTime now = DateTime.UtcNow;

            if (LastLooted.TryGetValue(key, out DateTime looted))
            {
                double elapsed = (now - looted).TotalSeconds;
                if (elapsed < cooldownSeconds)
                {
                    remainingSeconds = (float)(cooldownSeconds - elapsed);
                    return false;
                }
            }

            LastLooted[key] = now;
            PruneIfNeeded(cooldownSeconds, now);
            return true;
        }

        /// <summary>
        /// Drops entries whose cooldown has already elapsed. Only runs once the map
        /// exceeds the threshold, so the common case costs nothing.
        /// </summary>
        private static void PruneIfNeeded(float cooldownSeconds, DateTime now)
        {
            if (LastLooted.Count < PruneThreshold) return;

            StaleKeys.Clear();
            foreach (KeyValuePair<long, DateTime> entry in LastLooted)
            {
                if ((now - entry.Value).TotalSeconds >= cooldownSeconds)
                {
                    StaleKeys.Add(entry.Key);
                }
            }

            foreach (long key in StaleKeys)
            {
                LastLooted.Remove(key);
            }

            Plugin.Debug($"Pruned {StaleKeys.Count} expired cooldown entries, {LastLooted.Count} remain.");
            StaleKeys.Clear();
        }

        /// <summary>Clears all state. Called when the server session ends.</summary>
        internal static void Reset()
        {
            LastLooted.Clear();
            StaleKeys.Clear();
        }

        internal static int TrackedCount => LastLooted.Count;
    }
}
