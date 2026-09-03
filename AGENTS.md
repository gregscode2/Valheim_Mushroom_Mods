# Working in this repo

A monorepo of BepInEx mods for Valheim, one mod per top-level directory, built
for a large no-map dedicated server.

## Docs

**Releasing, CI, or anything about the build workflow** — read
[docs/devops.md](docs/devops.md). It covers the `ci-test` smoke test, how CI
sources the game assemblies it cannot commit, what a new mod must do to be
discovered by the release workflow, and the log lines that look like failures
but are not.

**Working inside a mod directory** — read that mod's own docs before you touch
its code, and record what you learn there when you are done. The material that
earns a place: a Valheim or BepInEx API that behaves differently than its name
suggests, an approach that was tried and rejected and why, a trap that cost real
time to diagnose. Write down the reasoning, not the diff — git already has the
diff.

Where each mod keeps that material today:

| Mod | Docs |
|---|---|
| CombatAdustments | `docs/shield-rework-requirements.md` |
| CraftableSpawners | `design_decisions.md` |
| Separate Spawns | `CONTEXT.md` |
| haldor-expansion | `DESIGN.md` |
| vegvisir-compass | `README.md` — its "How it works" section |
| RandomYggdrasil | none yet; start `docs/` |

New material goes in a `docs/` directory inside that mod. Where a mod already
keeps its notes in a single top-level file, extend that file rather than opening
a second home for the same thing.

## Building

Most mods build with a bare `dotnet build -c Release`, resolving the game path
from the Steam registry. Each mod names its reference root differently, and two
of them need the path passed explicitly — [docs/devops.md](docs/devops.md) has
the property table and the exceptions.

## Never committed

Game assemblies (`assembly_*.dll`, `UnityEngine*.dll`), BepInEx
(`BepInEx*.dll`, `0Harmony*.dll`), decompiled game source, and build output.
Reference them from the local install instead; CI fetches its own copies, and
releases carry the built DLLs. This is the one mistake the repo has already had
to undo with a history rewrite, and `.gitignore` is the guard — if a change
requires loosening it, that is the signal to stop and ask.
