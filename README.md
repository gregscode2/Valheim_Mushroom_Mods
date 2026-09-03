# Mushroom Mods

[![CI](https://github.com/NickSpinosa/Valheim_Mushroom_Mods/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/NickSpinosa/Valheim_Mushroom_Mods/actions/workflows/ci.yml)

Mushroom mods are a small collection of utility and quality of life mods designed for large nomap servers

## Mods

| Mod | What it does |
|---|---|
| [Vegvísir Compass](vegvisir-compass/README.md) | Loot a limited-use compass from a Vegvísir that points you at its boss, instead of revealing it on the map |
| [Separate Spawns](Separate%20Spawns/README.md) | Splits players into groups at world creation, each with its own starting area and a private portal back to the world centre |
| [Haldor Expansion](haldor-expansion/README.md) | Adds five gathering materials to Haldor's stock, to relieve resource scarcity on a long-lived server |
| [Combat Adjustments](CombatAdustments/src/CombatAdjustments.ShieldRework/README.md) | Shield stagger, durability and tower block-armor rework |
| [Craftable Spawners](CraftableSpawners) | Craftable natural spawners |
| [Random Yggdrasil](RandomYggdrasil) | Randomises the Yggdrasil branch rotation per world, synced across the server |
| [Horn of Calling](HornOfCalling/README.md) | Scaffold — adds a craftable item to the Forge, currently a placeholder Frost Axe |

## Installing

Download the DLLs you want from the [latest release](../../releases/latest) and
drop them into `BepInEx/plugins/`. Most of these mods are server-authoritative,
so install them on **every client and on the dedicated server** — check each
mod's own README.

No built DLL is committed to this repo; releases carry the artifacts.

## Building and releasing

See [docs/devops.md](docs/devops.md).
