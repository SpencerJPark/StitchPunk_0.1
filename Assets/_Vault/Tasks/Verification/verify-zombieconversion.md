---
title: Verify — Zombie Conversion (phase 1)
status: active
created: 2026-09-04
area: code
---

## Goal

Confirm that enabling `ZombifyRequest` on a **living** unit converts it — brain, faction, attacks
and skin — in the frame it resolves, and that nothing else regressed. Spec + build log:
[`../Plans/ZombieConversion_System.md`](../Plans/ZombieConversion_System.md) §11.

The Editor was busy on another session while this was written, but it did recompile: all six
touched assemblies built clean at 17:44 on 2026-09-04 (`Logs/Editor.log`, no `error CS` from these
files), and a Burst `BC1016` in `ZombifyJob` was caught there and fixed. **Nothing has been rebaked
or run.** Start at the gate.

## Gate (first session with the Editor free)

- [x] C# compile — clean on 2026-09-04 17:44 for all touched files (`ZombifySystem.cs`,
  `UnitComponents.cs`, `UnitBakingUtil.cs`, `SpawnStateInitSystem.cs`, `DebugZombifyMenu.cs`,
  `NarrativeEventSO.cs`, `NarrativeEventManager.cs`, `NarrativeEventSOEditor.cs`). The one
  `error CS0104` in that log is another session's `CutsceneStageAuthoring.cs`, not this work.
- [ ] Re-check Burst after the next recompile: `ZombifyJob` first failed `BC1016` building a
  `FixedString32Bytes` from a string literal; the names now come from a non-`[BurstCompile]`
  `OnCreate` as job fields ([[Gotchas]]). Confirm the error is gone — do not "fix" it by dropping
  `[BurstCompile]` from the job.
- [ ] **Rebake** — `UnitBakingUtil` now adds `ZombifyRequest`, so every unit archetype changed.
  Reopen the subscene / re-enter Play mode before testing.
- [ ] EditMode ▸ `StitchPunk.Tests` — `SystemPlacementConformanceTests` must still pass
  (`ZombifySystem` declares `[UpdateInGroup(HealthSystemGroup)]` and lives in the matching folder).
- [ ] Owed: a PlayMode fixture over target resolution (`targetUnitType = None` →
  `UnitDataBlob.becomesUnitType`) and the in-flight-swap defer. Write it only if you can revert the
  behaviour and watch it fail.

## Play-test (owner, `DOTSTestScene` or `Game`)

Put `DebugZombifyMenu` on any GameObject in the scene; the panel sits right of the save menu.

- [ ] Point at a living citizen, press **Nearest to mouse** → in the same frame:
  - [ ] skin swaps to the zombie look (this needs the phase-2 assets — a "Zombie" tag in the Skin
    group plus alternative palette colours; **with no zombie designs authored the unit converts but
    looks unchanged**, which is expected, not a bug)
  - [ ] `UnitData.unitType` / `UtilityBrain.unitType` re-keyed in the Entities window
  - [ ] `Faction`, `AttackFaction`, `AvailableAttack`, `Motivation` buffers rebuilt from the new
    unit's library entry
  - [ ] the unit's current behavior is torn down and it re-decides (it should start behaving like a
    zombie: aggressing on humans)
- [ ] Set the delay to 2s and convert → nothing happens for two seconds, then the full conversion.
- [ ] **All within Nm** on a crowd → every living unit in range converts, none twice, no errors.
- [ ] Press the button with the mouse over empty ground → nothing converts, one log line, no
  exception.
- [ ] Convert a unit with no `becomesUnitType` authored (e.g. an existing zombie) → request is
  consumed, unit unchanged, no error spam.
- [ ] Kill a citizen, revive it with the player reviver **in the same second** as a zombify request
  → both resolve; the revive's brain swap is not lost (the defer path), no unit ends up
  half-converted.
- [ ] Convert, then let the unit die → death, ragdoll and (if `UndeadAuthoring` is on it) revive
  still behave.
- [ ] Convert a **spawned** (pooled) citizen, let it die and be reclaimed → the reclaimed body does
  not come back mid-conversion (`SpawnStateInitSystem` reset).
- [ ] Save after converting, reload → known gap: the skin persists, the brain does not (see spec
  §11). Confirm it is *only* that, no errors.

## Narrative action

- [ ] Add a **Zombify** action to a `NarrativeEventSO` group in the inspector — it appears in the
  action dropdown, its three fields draw, and the summary line reads sensibly.
- [ ] Fire the event on a `NarrativeEntityId`-tagged NPC → it converts.
- [ ] With `waitForConversion` + a 2s delay, the narrative group waits for the conversion before
  advancing to the next action.

## Notes

Record here: anything the gate caught, and the owner's call on §9.4 (bite conversion vs
corpse-revive as the game's zombie-creation path).
