using System.Collections.Generic;
using UnityEngine;

namespace VegvisirCompass
{
    /// <summary>
    /// Finds where a merchant actually is, rather than where they might one day be.
    ///
    /// Vanilla scatters several candidate camps for each trader and only commits to one
    /// when a player gets close, so resolving a merchant by location alone returns the
    /// nearest *candidate* - somewhere the trader may never appear.
    ///
    /// The order of preference follows what the player can actually walk to:
    ///
    ///   1. A settled merchant, once their site has been locked in. Exactly one trader
    ///      survives that, so the nearest-trader search below finds it by construction.
    ///   2. A provisional merchant currently standing in a camp.
    ///   3. Nothing, in which case the caller falls back to the nearest candidate site -
    ///      the closest place the merchant could yet turn up.
    ///
    /// Server-side only; ZDOMan holds nothing useful on a client.
    /// </summary>
    internal static class MerchantLocator
    {
        /// <summary>
        /// The position of a merchant who exists right now, settled or provisional.
        /// False when none is standing anywhere, which is the honest answer: until a
        /// player has been near a camp, the game itself has not decided.
        /// </summary>
        internal static bool TryGetSettledPosition(MerchantDef merchant, Vector3 near, out Vector3 position)
        {
            position = Vector3.zero;
            if (merchant == null) return false;

            List<ZDO> traders = MerchantPlacement.CollectTraders(merchant);
            if (traders.Count == 0)
            {
                Plugin.Debug($"{merchant.DisplayName}: none standing anywhere; " +
                             "falling back to the nearest candidate site.");
                return false;
            }

            // Nearest rather than first: the ZDO table returns them in storage order, and
            // a world can legitimately hold several provisional merchants at once.
            float best = float.MaxValue;
            foreach (ZDO zdo in traders)
            {
                Vector3 candidate = zdo.GetPosition();
                float distance = CompassItem.HorizontalDistance(near, candidate);
                if (distance < best)
                {
                    best = distance;
                    position = candidate;
                }
            }

            bool settled = MerchantPlacement.IsLocked(merchant);
            Plugin.Debug($"{merchant.DisplayName}: {traders.Count} standing, nearest at {position} " +
                         $"({best:0}m away, {(settled ? "settled" : "provisional")}).");
            return true;
        }

        /// <summary>The merchant a location name belongs to, or null.</summary>
        internal static MerchantDef ForLocation(string locationName)
        {
            if (string.IsNullOrEmpty(locationName)) return null;

            foreach (MerchantDef def in MerchantCatalog.All)
            {
                if (string.Equals(locationName, def.LocationName, System.StringComparison.OrdinalIgnoreCase))
                {
                    return def;
                }
            }
            return null;
        }
    }
}
