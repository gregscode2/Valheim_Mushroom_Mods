# Horn of Calling — context

Why this mod is built the way it is.

## Jötunn was tried and dropped

The first version registered its item through [Jötunn](https://valheim-modding.github.io/Jotunn/),
which reduces item registration to about ten lines. It was dropped because the cost
landed in the wrong place: Jötunn is a separate BepInEx plugin that every player and the
dedicated server would have had to install, which would have made this the only mod in
the repo with an install-time dependency beyond BepInEx.

vegvisir-compass already adds a custom item without it, so the pattern existed in-repo.
Registering by hand is roughly 60 lines against `ObjectDB` and `ZNetScene`, and the
result has no runtime dependency at all.

Dropping it also removed a pile of build friction that was purely Jötunn's:

- **`JotunnLib` ships `build/JotunnLib.props`, which injects ~150 copy-local
  `<Reference>` items** pointing into the game folder. Left alone, every build copied
  the entire Unity runtime into `bin/`, against the repo rule that no game assembly is
  ever shipped. It needed a target stamping `Private=false` onto every reference.
- **Its `BEPINEX_PATH` is broken on Linux.** `Paths.props` builds it as
  `$(VALHEIM_INSTALL)\BepInEx` — a hardcoded backslash, not a separator on Unix — so
  Jötunn's own BepInEx references never resolved on Linux. Working around that pulled in
  a `BepInEx.Core` PackageReference and a `nuget.config` for `nuget.bepinex.dev`, since
  BepInEx is not on nuget.org.
- **It forced `net462`** (JotunnLib ships only that target), which in turn needed
  `Microsoft.NETFramework.ReferenceAssemblies` to build on Linux, which dragged ~100
  netstandard facade DLLs into `bin/`.
- **It auto-detects the game install silently**, falling back to
  `$(HOME)/.steam/steam/steamapps/common/Valheim` on Unix. That path exists on a normal
  Linux Steam install, so game references resolved *even when the project was
  misconfigured* — builds could succeed for reasons unrelated to `ValheimPath`.

None of that applies now. The project is `netstandard2.1` like the rest of the repo,
`bin/Release` holds three files, and the only package reference is the publicizer.

## Registration order is the whole problem

Adding an item by hand is not hard; getting it to happen at the right moment is.

- **`ObjectDB` is populated more than once.** There is a stripped-down copy in the main
  menu, then the real one merged in via `CopyOtherDB`. Registering only on `Awake` gives
  an item cloned from an incomplete database. Both entry points are patched, and
  `EnsureRegistered` guards on `odb.GetItemPrefab("Wood") == null` to detect the
  main-menu copy and bail.

- **`CopyOtherDB` does not merge — it replaces.** The name suggests copying entries in.
  It actually reassigns the list references outright:

  ```csharp
  m_items   = other.m_items;
  m_recipes = other.m_recipes;
  ```

  So everything registered against the main-menu database is discarded when a world
  loads. **Never latch a "registered" boolean**; test the live list every time. This cost
  real time to find because the failure is asymmetric and looks nothing like its cause:
  item registration re-tests `odb.m_items.Contains(_prefab)` and so silently healed
  itself, while the recipe used a `_recipeAdded` flag and stayed gone. The symptom was
  `spawn FrostAxe` working perfectly while the workbench showed nothing — which reads
  like a recipe bug, not a lifecycle one.

- **No patch point is guaranteed to be both late enough and ordered correctly.**
  `CopyOtherDB` can replace the recipe list while the crafting station prefabs are still
  unloaded, and `ZNetScene.Awake` may already have run by then, leaving no retry. The
  backstop is a prefix on `Player.UpdateKnownRecipesList`, which runs immediately before
  the game enumerates recipes — the one moment the recipe is definitely needed. With the
  presence check the common case is a single list scan.
- **The recipe cannot be built at the first `ObjectDB.Awake`.** A `Recipe` needs a
  `CraftingStation`, which is a component on a *piece* prefab, not an item — so it does
  not live in `ObjectDB` and may not exist yet. Recipe registration is therefore a
  separate idempotent step attempted from all three patch points, returning early and
  retrying while the station is missing.
- **Cloning must not run `Awake`.** The clone is instantiated into an inactive,
  `DontDestroyOnLoad` container so Unity treats it as a prefab rather than a live scene
  object. Borrowed from vegvisir-compass.
- `ItemDrop.ItemData.SharedData` is a plain `[Serializable]` class, so `Instantiate`
  deep-copies it. Edits to the clone's `m_shared` cannot leak back into the vanilla item.

## Publicizer

`ObjectDB.UpdateRegisters()` and `ZNetScene.m_namedPrefabs` are both private and both
are required. `BepInEx.AssemblyPublicizer.MSBuild` rewrites the reference assemblies at
build time only — nothing changes at runtime and players need nothing extra.

## Item naming

`m_shared.m_name` is set to the plain string `"Frost Axe"`, not a `$item_` localization
token. The mod ships no translation table, and an unresolved token displays in game as
the literal `$item_frostaxe` rather than failing — a silent, easy-to-miss bug. If
localization is added later, the token and the table have to land together.

## Crafting station lookup

The station is found by scanning `Resources.FindObjectsOfTypeAll<CraftingStation>()` for
a matching `name`, rather than reading it out of `ZNetScene`, because the lookup is
needed from three patch points with different guarantees about what is loaded.

The names are prefab names and are not guessable from the in-game labels: the Workbench
is `piece_workbench` while the Forge is plain `forge`. Both were confirmed from the log,
not assumed.

On failure it logs every station name it *did* find. A station prefab renamed between
game versions otherwise shows up as a recipe that silently never appears, which is
expensive to diagnose from the game side.

## CI

No change to `.github/actions/build-mods` was needed. The mod resolves through
`ValheimManaged` / `BepInExCore`, which the workflow already passes as global
properties, and every assembly it references was already in the verify step's required
list.

Verified locally by building with `-p:ValheimPath=/nonexistent` plus the real paths as
globals, reproducing how a runner overrides the local default.
