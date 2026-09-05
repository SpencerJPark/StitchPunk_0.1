# Stitch Punk — CLAUDE.md

## Game

**Stitch Punk** — a 2.5D real-time strategy game set in the 1900s industrial revolution. The player is a **necro engineer** who reanimates corpses as minions, manages a factory, and navigates a murder mystery. Core loop: RPG-style missions + factory building + trade management.

The world runs on **necromancy-based engineering** — "stitched together tech." Minions are Frankenstein constructs: layered, sewn-together parts. Let this steer naming and design calls.

## Folder → context note

Notes live in `_Vault/Memories/Code/` and are named after the folder: `Authoring`, `Components`, `Systems`, `Data`, `Core`, `MonoBehaviours`, `Editor`, `Shaders`, plus `RULES` (hard conventions), `Contracts` (cross-feature request/event index), and `Gotchas` (silent-failure traps). Anything under `Systems/` not listed below reads `Systems.md`.

| Area | Note |
|---|---|
| `Systems/UtilityAISystemGroup/`, `MinionActionSelectionSystemGroup/`, `StateMachineSystemGroup/` | `Systems_AI.md` |
| `Systems/AnimationSystemGroup/` | `Systems_Animation.md` |
| `Systems/MovementSystemGroup/` | `Systems_Movement.md` |
| `Shaders/` | `Shaders.md` (+ the `shader-edit` skill) |

Coding rules are in `RULES.md` and the root `CLAUDE.md` — not restated here.

## `_Vault/`

An [Obsidian](https://obsidian.md) vault; start at `_Vault/Home.md`.

- `Memories/Code/` — the per-folder notes above. `Memories/Design/` — design thinking. `Memories/Marketing/` — copy and tone.
- `Tasks/Plans/` — approved specs awaiting a build. `Tasks/Verification/` — built, awaiting a play-test. `Tasks/Completed/` — done. `Tasks/Claude/` — audits and handoffs.
- `Raw/` — unprocessed ideas; drop pasted ideas here.

**Status lives in the vault note or the task file, never in a CLAUDE.md.** This file previously carried a per-system status section that drifted 211 commits out of date and contradicted `Systems_AI.md`; do not reintroduce one.

## Where the work is

- **Active:** the DOTS Animation Toolkit — `Packages/com.dotsanimationtoolkit`, `Assets/AnimationToolkitMigration/`, docs in `Docs/AnimationToolkit/`.
- **Built:** dialogue + narrative events; the AI decision/execution split with interrupts, self-defence, flee, talk, sit, pickup, minion move orders, and sound. Cutscene integration (`CutsceneSystemGroup`, `Tasks/NewPlans/CutsceneIntegration_System.md`, G1) is code-complete — start/end/camera/sound/triggers all gate a bound actor off AI and movement — but nobody has watched one play in the game yet; a ⏸ owner checkpoint is open.
- **Parked:** the factory production loop — the data layer bakes, but `ProductionSystem` and `FactoryLibraryBakingSystem` sit commented out in `Core/Unused/`, and `BuildingsSystemGroup` has no live members.
