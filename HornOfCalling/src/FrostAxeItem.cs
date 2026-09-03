using System.Collections.Generic;
using UnityEngine;

namespace HornOfCalling
{
    /// <summary>
    /// Owns the mod's item: cloning it from a vanilla prefab, and registering it and
    /// its recipe with ObjectDB and ZNetScene.
    ///
    /// Placeholder content. The item is a Frost Axe until the horn itself is designed;
    /// the registration machinery around it is what this file is really about, and that
    /// does not change when the item does.
    /// </summary>
    internal static class FrostAxeItem
    {
        internal const string PrefabName = "FrostAxe";

        /// <summary>Name shown in the inventory. Plain text, not a localization token -
        /// the mod ships no translation table, so a token would display raw.</summary>
        private const string DisplayName = "Frost Axe";

        private const string Description = "An iron axe rimed with frost.";

        /// <summary>Vanilla item the axe borrows its model, stats and icon from.</summary>
        private const string CloneSourceItem = "AxeIron";

        /// <summary>Prefab name of the crafting station the recipe is bound to.</summary>
        private const string StationPrefabName = "piece_workbench";

        private const int MinStationLevel = 1;

        /// <summary>Craft cost: item name, amount for the first craft, extra per upgrade level.</summary>
        private static readonly (string Item, int Amount, int PerLevel)[] Cost =
        {
            ("Wood", 1, 1),
        };

        private static GameObject _prefab;
        private static GameObject _prefabContainer;
        private static bool _recipeAdded;

        // --- Registration -------------------------------------------------------

        /// <summary>
        /// Adds the item to the given ObjectDB, building the prefab on first call.
        /// Safe to call repeatedly; ObjectDB.Awake and CopyOtherDB both fire more than once.
        /// </summary>
        internal static void EnsureRegistered(ObjectDB odb)
        {
            if (odb == null || odb.m_items == null) return;

            // ObjectDB also exists in the main menu in a stripped-down form. Anything
            // cloned from it there would be incomplete, so wait for the real one.
            if (odb.GetItemPrefab("Wood") == null) return;

            if (_prefab == null)
            {
                _prefab = BuildPrefab(odb);
                if (_prefab == null) return;
            }

            if (!odb.m_items.Contains(_prefab))
            {
                odb.m_items.Add(_prefab);
                odb.UpdateRegisters();
                Plugin.Log.LogInfo("Registered " + PrefabName + " with ObjectDB.");
            }
        }

        /// <summary>
        /// Adds the crafting recipe. Separate from EnsureRegistered because it needs the
        /// crafting station, which is a *piece* and so only exists once ZNetScene has
        /// built its prefab list - later than the first ObjectDB.Awake.
        /// </summary>
        internal static void EnsureRecipeRegistered(ObjectDB odb)
        {
            if (_recipeAdded || _prefab == null || odb == null || odb.m_recipes == null) return;

            ItemDrop item = _prefab.GetComponent<ItemDrop>();
            if (item == null) return;

            CraftingStation station = FindStation();
            if (station == null) return; // Not up yet; a later patch will retry.

            var resources = new List<Piece.Requirement>();
            foreach ((string name, int amount, int perLevel) in Cost)
            {
                GameObject resourcePrefab = odb.GetItemPrefab(name);
                ItemDrop resource = resourcePrefab != null ? resourcePrefab.GetComponent<ItemDrop>() : null;
                if (resource == null)
                {
                    Plugin.Log.LogError(
                        "Cannot build the " + PrefabName + " recipe: no item prefab named " + name + ".");
                    return;
                }

                resources.Add(new Piece.Requirement
                {
                    m_resItem = resource,
                    m_amount = amount,
                    m_amountPerLevel = perLevel,
                    m_recover = true,
                });
            }

            Recipe recipe = ScriptableObject.CreateInstance<Recipe>();
            recipe.name = "Recipe_" + PrefabName;
            recipe.m_item = item;
            recipe.m_amount = 1;
            recipe.m_enabled = true;
            recipe.m_craftingStation = station;
            recipe.m_repairStation = station;
            recipe.m_minStationLevel = MinStationLevel;
            recipe.m_resources = resources.ToArray();

            odb.m_recipes.Add(recipe);
            _recipeAdded = true;
            Plugin.Log.LogInfo("Registered the " + PrefabName + " recipe at " + StationPrefabName + ".");
        }

        /// <summary>
        /// Registers the prefab with ZNetScene so the item can exist as a networked
        /// object once dropped on the ground.
        /// </summary>
        internal static void EnsureNetworkRegistered(ZNetScene scene)
        {
            if (scene == null || _prefab == null) return;

            if (!scene.m_prefabs.Contains(_prefab))
            {
                scene.m_prefabs.Add(_prefab);
            }

            int hash = PrefabName.GetStableHashCode();
            if (!scene.m_namedPrefabs.ContainsKey(hash))
            {
                scene.m_namedPrefabs[hash] = _prefab;
                Plugin.Log.LogInfo("Registered " + PrefabName + " with ZNetScene.");
            }
        }

        // --- Construction -------------------------------------------------------

        /// <summary>
        /// Locates the crafting station component by prefab name.
        ///
        /// Searched over every loaded CraftingStation rather than pulled from
        /// ZNetScene, because the station is needed from several patch points and this
        /// works at all of them. Logs what it did find on failure - a station name that
        /// changed between game versions is otherwise a silent missing recipe.
        /// </summary>
        private static CraftingStation FindStation()
        {
            CraftingStation[] stations = Resources.FindObjectsOfTypeAll<CraftingStation>();
            if (stations == null || stations.Length == 0) return null;

            foreach (CraftingStation station in stations)
            {
                if (station != null && station.name == StationPrefabName) return station;
            }

            var names = new List<string>();
            foreach (CraftingStation station in stations)
            {
                if (station != null) names.Add(station.name);
            }
            Plugin.Log.LogWarning(
                "No crafting station prefab named '" + StationPrefabName + "'. Found: " +
                string.Join(", ", names.ToArray()));
            return null;
        }

        private static GameObject BuildPrefab(ObjectDB odb)
        {
            GameObject source = odb.GetItemPrefab(CloneSourceItem);
            if (source == null)
            {
                Plugin.Log.LogError(
                    "Cannot build " + PrefabName + ": no item prefab named " + CloneSourceItem + ".");
                return null;
            }

            // Instantiating into an inactive parent keeps Unity from running Awake on
            // the clone, so it behaves as a prefab rather than a live scene object.
            if (_prefabContainer == null)
            {
                _prefabContainer = new GameObject("HornOfCallingPrefabs");
                _prefabContainer.SetActive(false);
                Object.DontDestroyOnLoad(_prefabContainer);
            }

            GameObject clone = Object.Instantiate(source, _prefabContainer.transform);
            clone.name = PrefabName;

            ItemDrop drop = clone.GetComponent<ItemDrop>();
            if (drop == null)
            {
                Plugin.Log.LogError("Item prefab " + CloneSourceItem + " has no ItemDrop component.");
                Object.Destroy(clone);
                return null;
            }

            // SharedData is a plain [Serializable] class, so Instantiate deep-copied it
            // and these edits cannot leak back into the item we cloned from.
            ItemDrop.ItemData.SharedData shared = drop.m_itemData.m_shared;
            shared.m_name = DisplayName;
            shared.m_description = Description;

            Plugin.Log.LogInfo("Built " + PrefabName + " from " + CloneSourceItem + ".");
            return clone;
        }
    }
}
