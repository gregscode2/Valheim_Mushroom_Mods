# Horn of Calling

Adds a craftable item to the Forge.

> **Status: scaffold.** The item is currently a placeholder **Frost Axe** — a clone of
> the vanilla iron axe, craftable at a level 2 Forge for 20 Iron / 10 Wood. The horn
> itself is not implemented yet; the registration plumbing around it is.

Plain BepInEx + Harmony, no extra runtime dependency — install `HornOfCalling.dll` into
`BepInEx/plugins/` and nothing else.

The mod adds an item and a recipe, so it needs to be on **every client and on the
dedicated server**.

## Building

```bash
dotnet build HornOfCalling/HornOfCalling.csproj -c Release
```

Defaults to the Linux Steam install (`~/.local/share/Steam/steamapps/common/Valheim`),
falling back to the default Windows path. Override it by creating
`Directory.Build.user.props` next to `Directory.Build.props` (gitignored):

```xml
<Project>
  <PropertyGroup>
    <ValheimPath>D:\Games\Valheim</ValheimPath>
  </PropertyGroup>
</Project>
```

A successful build copies the DLL straight into `BepInEx/plugins/`. There is no hot
reload — restart the game after each build.

The build errors early with a named message if `assembly_valheim.dll` or `BepInEx.dll`
can't be found, rather than emitting a wall of `CS0246`.

## How it works

Three registration steps, all idempotent, driven from Harmony postfixes in
[`src/Patches.cs`](src/Patches.cs):

| Step | Where | Why there |
|---|---|---|
| Clone the prefab, add to `ObjectDB.m_items` | `ObjectDB.Awake`, `ObjectDB.CopyOtherDB` | ObjectDB is built twice — a stripped main-menu copy, then the real world one |
| Add the `Recipe` | any of the three patches | Needs the Forge, which is a *piece* and may not exist at the first `ObjectDB.Awake` |
| Add to `ZNetScene.m_prefabs` / `m_namedPrefabs` | `ZNetScene.Awake` | Lets the item exist as a networked object once dropped |

## Local setup

BepInEx must be installed into the game folder before the plugin can load. On the
**native Linux** build:

1. Extract [BepInExPack_Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/)
   (5.4.2333) into the Valheim directory, then `chmod u+x start_game_bepinex.sh`.
2. Steam → Valheim → Properties → Compatibility: **uncheck** "Force the use of a
   specific Steam Play compatibility tool". Proton breaks doorstop injection on the
   native build.
3. Steam → Valheim → Properties → Launch Options: `./start_game_bepinex.sh %command%`

BepInEx is up if `BepInEx/LogOutput.log` appears after a launch. If it never does, run
`start_game_bepinex.sh` from a terminal — the Steam container runtime hides the error.

## Testing

New world → `F5` → `devcommands` → `spawn FrostAxe 1`. Then build a Forge, upgrade it to
level 2, and confirm the recipe appears for 20 Iron / 10 Wood.

The log names each registration step as it happens, so a partial failure is visible:

```bash
grep -i hornofcalling "$VALHEIM/BepInEx/LogOutput.log"
```

Unity-side stack traces land in `~/.config/unity3d/IronGate/Valheim/Player.log`.

## Notes

See [docs/CONTEXT.md](docs/CONTEXT.md) for the API traps behind the design above.
