# Stitch Punk

> A 2.5D real-time strategy game built from the ground up on **Unity DOTS** (Entities 6.5 / Jobs / Burst), where a necro-engineer reanimates corpses as Frankenstein minions, runs a factory, and unravels a murder mystery in a necromancy-powered industrial revolution.

<!--
  ┌─────────────────────────────────────────────────────────────────────────┐
  │  HERO MEDIA — add a screenshot or gameplay GIF here before sharing.      │
  │  This is the single highest-return addition to the README: a technical  │
  │  reviewer decides in seconds whether to keep reading. Drop an image in   │
  │  (e.g. docs/hero.gif or docs/screenshot.png) and swap the line below.    │
  └─────────────────────────────────────────────────────────────────────────┘
-->
<!-- ![Stitch Punk gameplay](docs/hero.gif) -->
<p align="center"><em>▶ Screenshot / gameplay GIF goes here — see the comment above.</em></p>

Stitch Punk is a solo, in-development project and my deep dive into **data-oriented game architecture**. The interesting part of this repository isn't a finished game — it's the engineering: a fully data-oriented simulation with a custom ECS animation pipeline, a utility-AI decision/execution split, incremental pathfinding for crowds, a data-driven design/appearance system, a reflection-API shader node library, and a suite of bespoke Unity editor tooling — all held to a strict, self-imposed set of engineering rules.

---

## Table of Contents

- [Concept](#concept)
- [Why This Project Exists](#why-this-project-exists)
- [Tech Stack](#tech-stack)
- [Architecture at a Glance](#architecture-at-a-glance)
- [Systems Deep Dive](#systems-deep-dive)
  - [The AI Brain: Decision / Execution Split](#the-ai-brain-decisionexecution-split)
  - [Movement & Pathfinding](#movement--pathfinding)
  - [The Custom Animation Pipeline](#the-custom-animation-pipeline)
  - [Character Design & Appearance](#character-design--appearance)
  - [Combat, Damage & Ragdolls](#combat-damage--ragdolls)
  - [The ScriptableObject → BlobAsset Data Pipeline](#the-scriptableobject--blobasset-data-pipeline)
- [Rendering & Shaders](#rendering--shaders)
- [Custom Editor Tooling](#custom-editor-tooling)
- [Engineering Discipline](#engineering-discipline)
- [Testing](#testing)
- [Project Structure](#project-structure)
- [Building & Running](#building--running)
- [Project Status](#project-status)

---

## Concept

**Stitch Punk** is a 2.5D real-time strategy game set in an alternate 1900s industrial revolution — one where **necromancy is the engine of technological progress**. The player is a *necro-engineer* who:

- **Reanimates corpses** into layered, sewn-together minions ("stitched-together tech")
- **Builds and manages a factory** that produces flesh-automatons from scavenged parts
- **Commands minions** in RTS-style combat and tasking
- **Navigates a narrative** of dialogue-driven missions and a central murder mystery

The core loop blends RPG missions, factory building, and trade management. Minions are Frankenstein constructs — visually assembled from independent, swappable body parts, which drives much of the technical design below.

---

## Why This Project Exists

Most Unity games are built on `MonoBehaviour`s and the classic object-oriented component model. Stitch Punk is a deliberate bet on the opposite: **Unity's Data-Oriented Technology Stack (DOTS)** — Entities (ECS), the C# Job System, and the Burst compiler. The goal was to learn, at production depth, how to:

- Structure an entire game as **data + stateless systems**, not objects with behavior
- Write **Burst-compiled, multithreaded** gameplay code that scales to large crowds
- Manage the hard parts of ECS that tutorials skip: manual job-dependency wiring across systems, blob-asset baking pipelines, enableable-component state machines, entity remapping on spawn, and camera-visibility gating of presentation work
- Keep a large codebase (~**425 C# files / ~38,000 lines** across **10 assemblies**) navigable and consistent over a long solo build

The `Assets/_Vault/` directory is an [Obsidian](https://obsidian.md) knowledge base documenting the architecture, gotchas, and per-folder conventions — treated as a first-class part of the project, not an afterthought.

---

## Tech Stack

| Area | Technology |
|---|---|
| **Engine** | Unity **6000.5.0f1** (Unity 6.5) |
| **Core architecture** | Unity **DOTS** — Entities 6.5, Entities.Graphics, Burst, Collections, Mathematics, Jobs |
| **Physics** | Unity Physics 6.5 (raycasts for ground/ragdoll, spatial queries) |
| **Rendering** | Universal Render Pipeline (URP) 17.5, custom Shader Graph + reflection-API HLSL nodes, custom renderer features |
| **Camera** | Cinemachine 3.1 |
| **Input** | Input System 1.19 |
| **Async** | UniTask (allocation-free async/await for narrative event orchestration) |
| **DI** | Reflex (dependency injection for the MonoBehaviour layer) |
| **UI** | Rive (vector UI) + uGUI |
| **Language** | C# (strict conventions — see [Engineering Discipline](#engineering-discipline)) |

---

## Architecture at a Glance

The entire simulation is organized as a **single, explicitly-ordered pipeline of ECS system groups**, centralized in one manifest file (`Systems/SystemGroups.cs`). Every system declares which group it runs in; the folder tree under `Systems/` *is* the group tree; and structural conformance tests enforce that this stays true.

Each frame, the `SimulationSystemGroup` runs roughly this pipeline:

```
GameManager   → world services (registries, spatial hashes, damage bus, floating origin, camera visibility)
Player        → input → narrative → dialogue → equipment
UtilityAI     → motivation decay + awareness systems score options
MinionSelect  → player orders override AI options
StateMachine  → score winner → execute its behavior command sequence
Item          → equip / consume / pickup / thrown items
Movement      → flowfield + D* Lite routing → path following → transform integration
Buildings     → factory production loop (data layer built)
Combat        → attack requests → damage bus → resolution
Health        → death / ragdoll init / heal / revive / brain-swap
Design        → runtime appearance re-skin
Animation     → assign clips → advance time → sample keyframes → apply pose
```

then, in `LateSimulationSystemGroup`:

```
Spawn → SpawnInit → Ragdoll → Sound → Despawn → Save
```

**Design principles that recur throughout:**

- **Decision / execution separation** — systems that *decide* are kept apart from systems that *act*. The AI scores options; a single interpreter executes the winner. Combat producers enqueue damage events; a single consumer applies them.
- **Data lives in blobs, logic lives in systems** — `IComponentData` structs hold state only; all behavior is in Burst-compiled `ISystem` + `IJobEntity` jobs.
- **Group-level scene gating** — top-level feature groups gate on a scene tag once, rather than every system re-checking.
- **Contract components for cross-feature communication** — features talk to each other through a documented set of request/event components (an indexed "API surface"), not by reaching into each other's internals.

---

## Systems Deep Dive

### The AI Brain: Decision / Execution Split

Unit AI is architected as a **pure decision layer feeding a single execution interpreter** — a "Brain Control Split" that keeps decision-making and action entirely decoupled.

**Decision side** — a suite of *awareness systems* observe the world and append scored entries to a `UtilityActions` buffer:

- `EnemyAwarenessSystem` — attack options
- `SelfDefenceAwarenessSystem` — fight-back after a threat-flinch delay
- `FleeAwarenessSystem` — flight response, gated by per-unit **Bravery** and health (units break off a fight mid-combat when scared and hurt)
- `InteractionAwarenessSystem` — nearby interactables (chairs, waypoints) from a spatial hash
- `SocialAwarenessSystem` — conversations between compatible factions
- `ItemAwarenessSystem` — pick up a weapon when threatened, a bandage when hurt, food when idle

Each option is scored by a **utility function driven by pre-sampled response curves** (needs-based scoring: motivation delta × considerations). A `WinnerSelectionSystem` picks the highest-priority, highest-utility option and writes it into the unit's `StateMachine`.

**Execution side** — a single `BehaviorExecutionSystem` interprets the winning behavior. Behaviors are **authored as data**: each `BehaviorSO` asset defines a sequence of commands (`Approach`, `RequestAttack`, `PlayAnimation`, `WaitTime`, `LoopUntil`, `ModifyMotivation`, `ReleaseInteraction`, …) baked into a blob. The interpreter walks the sequence, blocking on some commands and firing-and-advancing on others. A `LoopUntil` command jumps back until a qualifier holds (`TargetDead`, `TimerExpired`, `MotivationSatisfied`, …) — which is how a melee unit "swing until the target is dead."

Because behaviors are unit-agnostic data, the same `Attack` behavior resolves the correct per-unit attack and animation at runtime from the unit's own baked data.

**Interrupts** are a first-class, single-path concern: a dedicated `BehaviorInterruptSystem` is the *only* place a running behavior is torn down — for death, revive, path-stuck, or a higher-priority option preempting the current one. It runs the interrupted behavior's cleanup commands, cancels in-flight attacks and pathing, and swaps in the pending behavior in the same frame.

Player-controlled minions reuse the exact same execution pipeline: player orders are injected as top-priority options that outrank AI decisions, with self-defense still firing when a minion is otherwise uncommanded.

> Full detail: [`Systems_AI.md`](Assets/_Vault/Memories/Code/Systems_AI.md)

### Movement & Pathfinding

Movement is split into four ordered sub-groups (routing → coordination → following → execution) and uses **two pathfinding strategies** depending on unit type:

- **Flowfield** for crowds — a shared vector grid points every cell toward a target; hundreds of units in a horde just sample the cell they occupy. Cheap and scales to large groups.
- **D\* Lite** for individuals (player, special units) — an incremental A\* variant that efficiently *replans* when obstacles change, rather than recomputing from scratch.

Units belong to a `Horde` entity that holds the shared destination; each member carries a formation offset so the crowd spreads into a formation rather than stacking. A `FloatingWorldOriginSystem` recenters the world to avoid floating-point precision loss over large play areas.

> Full detail: [`Systems_Movement.md`](Assets/_Vault/Memories/Code/Systems_Movement.md)

### The Custom Animation Pipeline

**There is no Unity Animator.** Animation is a fully custom, ECS-native, keyframe pipeline built for a 2.5D "layered paper doll" look and designed to batch hundreds of characters into few draw calls.

- **Units are layered quads** — each body part is a flat mesh in a hierarchy. Animation moves, rotates (Z-axis), or swaps the texture index of these quads.
- **Textures are texture arrays** — animation frames packed into a single array asset, so one GPU-instanced material serves an entire crowd while each character varies via per-instance material overrides.
- **7 animation layers** evaluated in order (`Base`, `Direction`, `Action`, `Face`, `Eyes`, `Mouth`, `Override`) with per-keyframe **blend modes** (override / additive) and **5 interpolation types**. This composites, e.g., a walk cycle + 8-directional facing + an attack swing + a facial expression simultaneously.
- **Data-driven** — clips are authored as `AnimationClipSO` assets (tracks of keyframes per body part), registered against an `AnimationType` enum, and baked into a blob. Systems never touch the SO at runtime.
- **Execution** — one system group advances time, samples keyframes, applies the pose to transforms, pushes texture indices to material property blocks, and billboards the root to face the camera.

**Presentation work is camera-visibility gated:** off-screen rigs skip sampling, pose-apply, and image-index pushes — but their *timers keep advancing*, so a unit re-entering view is already at the correct pose with no snap. Critically, **simulation never gates on visibility** — the world keeps running off-screen so behavior and saves stay deterministic.

> Full detail: [`Systems_Animation.md`](Assets/_Vault/Memories/Code/Systems_Animation.md)

### Character Design & Appearance

Because minions are assembled from swappable parts, appearance is fully **data-driven and runtime-mutable**:

- Each character rolls a randomized `PersistedDesign` at spawn — a shape per part group and a color index per palette, drawn from *authored* option lists (authoring decides what's allowed to randomize).
- A `DesignApplySystem` derives every part's texture slice and up to three tint colors from the design + palette blobs, writing per-instance material overrides so the whole crowd still batches into one draw call.
- A `DesignChangeSystem` re-skins a unit at runtime — e.g. **"zombify"** swaps every palette entry to its undead alternative while preserving the character's rolled identity.
- Colors are palette-driven with a "converted variant" per entry, baked sRGB→linear at bake time.

### Combat, Damage & Ragdolls

Combat runs on a **recycled damage-event bus** rather than per-unit damage buffers or per-hit entity churn:

- Producers (melee attacks, hazard zones, thrown items) `Enqueue` source-agnostic `DamageEvent` values into a shared `NativeQueue`.
- A resolution system expands area-of-effect events into single-target hits.
- A single consumer drains the queue, applies damage, gates *threat/retaliation by faction* (so friendly fire and environmental damage don't provoke fight-back), and flags death.

This required **manual cross-system job-dependency wiring** — a `NativeQueue` passed through a singleton bypasses ECS's automatic dependency tracking, so producer job handles are explicitly registered and completed before the drain (the same pattern Unity's own `EntityCommandBufferSystem` uses). Getting it wrong is a race condition, not a compile error — exactly the kind of low-level concurrency work DOTS forces you to reason about.

Death triggers a **custom 2D ragdoll**: on the killing blow, knockback is captured and the corpse is launched with a real 3D velocity, each joint flailing as a one-segment pendulum, bouncing off walls via raycast, then settling to an authored rest pose — and corpses even stack into piles via a spatial hash of settled bodies. Revival reverses it cleanly and can swap the unit's "brain" (AI → player-controlled minion).

### The ScriptableObject → BlobAsset Data Pipeline

The backbone pattern of the whole project: **designer-friendly `ScriptableObject` data is baked into Burst-accessible `BlobAsset`s** at bake time, and systems read the blob via a singleton — never the SO. This keeps all runtime data in unmanaged memory that Burst jobs can touch, with zero managed references.

The project has libraries for animations, units, attacks, body parts, color palettes, sounds, items, effects, behaviors, brains, and factory recipes — each following the same five-file pipeline (SO → LibrarySO → Blob struct → holder component → baking system in `PostBakingSystemGroup`). This pattern is so repetitive and bug-prone (blob-builder off-by-ones, dispose guards, correct baking group) that it's been captured as a reusable code-scaffolding skill.

---

## Rendering & Shaders

The 2.5D look is a custom **cel-shaded + painterly** pipeline built on URP and Shader Graph, using Unity 6.5's **Shader Function Reflection API** — one HLSL function per file, marked for export, becomes a real Shader Graph node:

- **Cel-shaded lighting node** — banded diffuse, toon specular, and rim lighting supporting the main light plus up to 8 per-object additional lights; used by every production graph.
- **Painterly node library** — variable-stop color ramps, value contrast, hue/sat/value control, per-instance position-hash variation (so moving a prop re-rolls its brush strokes), and a height-to-normal node that derives surface bumps from a stroke mask.
- **Packed-channel recolor nodes** — composite a channel-packed mask into a recolorable sprite (R/G/B = independent tintable layers, alpha = blend strength), so a single texture drives multi-zone recoloring while keeping outlines intact.
- **Custom renderer features** — a view-space normals capture pass and a Roberts-cross edge-detection pass drive silhouette outlines. (Documented tradeoff: these are incompatible with MSAA, so the project uses post-process SMAA instead — a real rendering-pipeline constraint worked through and written up.)

> Full detail: [`Shaders.md`](Assets/_Vault/Memories/Code/Shaders.md)

## Custom Editor Tooling

A meaningful share of the codebase is **bespoke Unity editor tooling** built to make the data-driven pipelines usable:

- **Animation Editor** — a custom window to author and *preview* keyframe clips without entering play mode, sampling the SO directly and applying poses live.
- **Dialogue Editor** — a GraphView-based node editor for branching dialogue trees: drag-and-drop node placement, one-per-tree constraints, and first-visit / repeat-visit ("refresher") paths.
- **Texture Channel Packer** — packs grayscale masks into RGBA channels with saveable recipes for one-click repacks (used for painterly masks and packed recolor sprites).
- **Texture Array Builder** — builds Texture2DArrays with *hand-authored* mip levels (painterly control instead of Unity's box-filter blur).
- **Painterly mask & gradient-LUT generators** — bake stroke masks and palette atlases from ScriptableObject definitions.

> Full detail: [`Editor.md`](Assets/_Vault/Memories/Code/Editor.md)

## Engineering Discipline

The project holds itself to a strict, documented set of rules (`_Vault/Memories/Code/RULES.md`) — partly to keep DOTS code safe and Burst-friendly, partly as an exercise in a maintainable large solo codebase:

- **Readability over brevity** — never `var`, never single-character names. Explicit types everywhere; names read like documentation.
- **Never `.Run()` a job** — always `.Schedule()` or `.ScheduleParallel()`, assigned to the dependency chain. No blocking the main thread.
- **No managed allocations inside Burst jobs** — no `new List<>`, no `string`, no boxing.
- **`ISystem` (struct) + `[BurstCompile]`** preferred over `SystemBase` throughout.
- **Data-only components, logic-only systems** — no logic in `IComponentData`, no game logic in authoring/MonoBehaviour code.
- **Every system declares its group** — no ad-hoc ordering; a single manifest owns the pipeline.
- **Cross-feature communication only through contract components** — indexed and documented.

These are enforced not just by convention but by **structural conformance tests** and a set of custom **code-scaffolding skills** that encode the correct patterns so new systems start compliant.

## Testing

EditMode unit tests (Unity Test Runner) cover the pure-logic core that doesn't need a running world — AI scoring curves, direction quantization, grid math, blob-slice math — plus **structural conformance tests** that enforce the architecture itself:

- Every system carries an `[UpdateInGroup]` attribute (a forgotten one silently misplaces a system).
- Every system file lives in the folder named after its group.
- Every adjacent pair in the pipeline has an explicit ordering edge, and no ordering edge crosses a group boundary (which Unity would silently ignore).

## Project Structure

```
Assets/
├── _Scripts/                    # 10 assemblies, ~425 files
│   ├── Components/               # IComponentData / buffers / enums / tags — DATA ONLY
│   ├── Authoring/                # MonoBehaviour + Baker pairs (prefab/SO → entity)
│   ├── Data/                     # ScriptableObjects + BlobAsset structs
│   ├── Systems/                  # ISystem + IJobEntity — ALL gameplay logic (120 files)
│   ├── MonoBehaviours/           # Managers, input, camera (non-ECS hybrid layer)
│   ├── UI/ · Core/ · Utils/      # UI, singletons, helpers
│   ├── Editor/                   # Custom editor windows & inspectors
│   └── Tests/                    # EditMode unit + conformance tests
├── Shaders/
│   ├── Graphs/                   # Production shader graphs (cel-shaded / painterly)
│   ├── Nodes/                    # Reflection-API HLSL node library
│   └── RenderFeatures/           # Outline / edge-detection passes
├── _Vault/                       # Obsidian knowledge base (architecture docs)
└── Scenes/                       # Game.unity + DOTS sandbox scenes
```

> **`Assets/_Vault/`** is the project's living documentation — open it in [Obsidian](https://obsidian.md) for graph view, backlinks, and full-text search. Per-folder context files under `_Vault/Memories/Code/` document conventions and hard-won gotchas for each subsystem.

## Building & Running

This is an **Editor-driven Unity project** — there is no standalone build script or CLI entry point.

**Requirements:** Unity **6000.5.0f1** (Unity 6.5) with the DOTS packages listed in `Packages/manifest.json` (resolved automatically on first open).

1. Open the project in Unity 6.5.
2. Open `Assets/Scenes/Game.unity` (main scene) or `Assets/Scenes/TestArea/DOTSTestScene.unity` (DOTS sandbox).
3. Enter Play mode. DOTS subscenes bake automatically on scene open / play.
4. Tests run via **Window ▸ General ▸ Test Runner** (EditMode).

## Project Status

Stitch Punk is **actively in development** — the engineering foundation is mature, and gameplay features are being layered on top of it. Broadly:

**Built & working:** the full ECS system pipeline; utility-AI decision/execution split with interrupts, self-defense, flee, social, sit, and item-pickup behaviors; player-controlled minion orders; flowfield + D\* Lite movement; the custom layered-quad animation pipeline; data-driven character design & runtime zombify; the damage-bus combat model + 2D ragdolls; dialogue & narrative-event systems with custom node editors; the SO→Blob data pipeline; save/load; and the cel-shaded/painterly render pipeline with custom editor tooling.

**In progress / next:** waypoint & schedule-driven AI behaviors, re-enabling the factory production loop (data layer already built), and building out narrative content.

---

<sub>Solo project by Spencer Park — a study in data-oriented game architecture with Unity DOTS. The `_Vault/` directory documents the full architecture and the reasoning behind each design decision.</sub>
