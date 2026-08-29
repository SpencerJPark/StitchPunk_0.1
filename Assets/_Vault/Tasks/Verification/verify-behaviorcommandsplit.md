---
title: Verify — Behavior Command Split
status: active
created: 2026-08-29
area: code
---

## Goal

Confirm the `BehaviorExecutionSystem` interpreter split is a true zero-behavior-change refactor:
every `BehaviorCommandType` arm now lives as a static handler in `Utils/BehaviorCommands/*.cs`
instead of inline in one switch, `BehaviorInterruptSystem` reuses the shared handlers instead of
duplicating them, and the new PlayMode fixture pins command-index progression. Compiled and
test-run this session with the Editor connected — the items below are the play-test pass the spec's
§10 calls for, which this session couldn't do itself.

## Steps

### Compile + tests (done this session)

- [x] Full recompile — console free of `error CS####` (two real ones were caught and fixed:
  `InteractionSpatialHashSystem` unreachable from `Utils` — Systems computes the flee cell and
  passes it in; a dropped `Unity.Collections.LowLevel.Unsafe` using). The Burst `BC0101`/`BC1055`
  hash-cache errors on `UnitAnimationAssignmentSystem`/`PlayerAttackSystem`/`DesignChangeSystem`
  pre-date this work (confirmed in `Logs/Editor.log` well before these edits, on files this refactor
  never touched) — a standing Editor-session Burst JIT issue, not caused by the split.
- [x] EditMode ▸ `StitchPunk.Tests` — all 54 green, including `BehaviorCommandCatalogTests`
  (catalog untouched, per §0) and `SystemPlacementConformanceTests`.
- [x] PlayMode ▸ new `StitchPunk.Tests.PlayMode` assembly ▸ `BehaviorExecutionSystemTests.
  ThreeCommandBehavior_AdvancesOneCommandPerTick_ThenCompletes` — green. First PlayMode World
  fixture over the interpreter (§10): scripted 3-command `ModifyMotivation` sequence, ticks
  `BehaviorExecutionSystem` directly, asserts `CurrentCommandIndex` advances one per tick and the
  Execute → Complete phase flip lands on the correct tick.

### Regression smoke (owner, needs the Editor + Play mode)

- [ ] Standard AI smoke in `DOTSTestScene`: wander → interact → fight (melee) → flee → talk → sit.
  All six use commands now living in `Utils/BehaviorCommands/`:
  - wander/flee → `MovementCommands.RunApproach` / `RunFlee`
  - fight → `RequestCommands.RunRequestAttack`
  - sit → `AnimationCommands.RunPlayAnimation` + `WaitLoopCommands.RunWaitTime`
  - talk → `RequestCommands.RunRequestSocialResponse` + `WaitLoopCommands` (qualifier early-exit)
  - pickup (if triggered) → `ItemCommands.RunRequestPickup`
- [ ] Interrupt an in-progress behavior (attack a unit mid-Wander, or let one die mid-behavior) and
  confirm cleanup still runs correctly — `BehaviorInterruptSystem` now calls
  `RequestCommands.RunModifyMotivation` / `MiscCommands.RunReleaseInteraction` /
  `AnimationCommands.RunStopAnimation` instead of its own duplicated bodies (§5 dedup target).
- [ ] Confirm `LoopUntil`-driven behaviors (Talk's wait-until-disengage) still exit correctly —
  exercises `WaitLoopCommands.RunLoopUntil` sharing `BehaviorQualifiers.Evaluate`.

## Notes

- `Utils/BehaviorCommandContext.cs` bundles the job's lookups/blob-refs/ECB into one struct passed
  `ref` to most handlers, built once per `Execute()` call. Three handlers reused by
  `BehaviorInterruptSystem` (`ModifyMotivation`, `ReleaseInteraction`, `StopAnimation`) take their
  few dependencies directly instead, since the interrupt job doesn't carry the execution job's full
  lookup set — see the "takes its dependencies directly" comment on each.
- `MovementCommands.RunFlee` takes `centerCell` as a parameter rather than calling
  `InteractionSpatialHashSystem.GetCell` itself — that type lives in the `Systems` assembly, which
  `Utils` cannot reference without a circular asmdef dependency.
- `BehaviorExecutionSystem.cs` shrank from 636 to 329 lines (job/system scaffolding — OnCreate,
  OnUpdate, both lookup-field blocks — accounts for the gap versus the spec's "~200 lines" estimate).
- New PlayMode assembly `Assets/_Scripts/Tests/PlayMode/StitchPunk.Tests.PlayMode.asmdef` mirrors
  `Packages/com.dotsanimationtoolkit/Tests/PlayMode/DotsAnimationToolkit.Tests.PlayMode.asmdef`'s
  shape (unrestricted `includePlatforms`, manual `World` + `GetOrCreateSystem<T>().Update(...)`, no
  scene/GameObjects needed) — this is the project's first PlayMode assembly outside the toolkit
  package; future World-based AI fixtures belong here too.
