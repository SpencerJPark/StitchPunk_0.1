# Minion Order Robustness — Design Spec

> **Status:** 🔨 built 2026-09-15 (A102 / Despawn / Minion Orders parallel batch, spec id `minion-orders`) — spec retired here, checklist at [`verify-minion-orders.md`](verify-minion-orders.md). Needs a rebake before any look; §12.4 is the build log, §1–§10 the design.
> **Raw source:** [`../Claude/Code_Audit_2026-07.md`](../Claude/Code_Audit_2026-07.md) item #6 — prerequisite for ordering ranged minions (RangedCombat plan)

---

**Skills Needed:**
- `dots-unit-ai` — command-surface enum growth + order-time action resolution (§2, §5)

---

## 1. Purpose & v1 scope

`MinionActionSelectionSystem` hardcodes `ActionType.MeleeSingle` for attack orders. The moment a ranged or thrown-weapon minion exists, player attack orders on it break silently (its brain has no MeleeSingle def → `actionDefIndex` miss → order dropped). Fix: resolve the action from the unit's `AvailableAttack` buffer at **order time** — the same resolution `RequestAttack` already performs at execution time in `BehaviorExecutionSystem` (keyed by `stateMachine.action` against the baked buffer). Second half: grow the command surface **once** instead of four times.

**v1 handles:** attack-order action resolution; the decided command-surface additions.
**Out of v1:** formation orders, queued orders (reserve nothing — the one-shot `OnMinion*Command` pattern extends naturally).

## 2. Architecture

No new systems. Two edits inside the existing minion-order flow (`UnitSelectionManager` (Mono) → `OnMinion*Command` baked per-minion → `MinionActionSelectionSystem` consumes one-shot → writes `StateMachine`):

1. **Order-time resolution:** on an attack order, walk the minion's `AvailableAttack` buffer and pick the entry whose range best fits the current target distance (mirrors the execution-time logic — extract the shared resolution into `Utils/AIUtils` so order-time and `RequestAttack` cannot drift). Falls back to the first available attack; a unit with an empty buffer refuses the order with a Burst-safe log instead of silently dropping it.
2. **Command surface:** ← DECISION — which of these does the slice need? Current: move / attack / interact / follow.
   - `Stop` — cancel current behavior → Idle (cheap: it's an `ActionInterruptRequest`).
   - `HoldPosition` — Stop + suppress wander/awareness re-decides until next order.
   - `ReturnToPlayer` — move order targeting the player entity (nearly free: existing move path with `targetEntity = player`).
   *Recommendation: `Stop` + `ReturnToPlayer` for the slice; `HoldPosition` adds a suppression flag to `UtilityBrain` gating — defer unless the demo scene needs stationed guards.*

## 5. Systems

- **Edited:** `MinionActionSelectionSystemGroup/MinionActionSelectionSystem.cs` — resolution + new command arms.
- **Edited:** `Utils/AIUtils.cs` (or a new small util) — shared `ResolveAttackAction(availableAttackBuffer, distanceToTarget)` used here and by `BehaviorExecutionSystem.RequestAttack`.
- **Edited:** `MonoBehaviours/Managers/UnitSelectionManager.cs` + `Utils/UnitBakingUtil.AddPlayerControlled` — new `OnMinion*Command` components for the decided verbs (same one-shot pattern).
- **New test:** EditMode fixture for `ResolveAttackAction` (pure buffer+distance logic — exactly the `dots-test` EditMode class).

## 8. Proposed file manifest

**Edited:** `MinionActionSelectionSystem.cs`, `AIUtils.cs`, `UnitSelectionManager.cs`, `UnitBakingUtil.cs`, `Components` (new `OnMinionStopCommand` etc. alongside the existing command components)
**New:** `Tests/AttackResolutionTests.cs`

## 9. Build phases

1. Extract + share the attack-resolution helper; EditMode test pins it.
2. Attack order uses it (behavior unchanged for melee units — characterization: melee minion orders work exactly as before).
3. New command verbs end-to-end (input binding → command component → selection arm).

## 10. Verification

DOTSTestScene: attack-order a melee minion (unchanged behavior), then hand a minion only a thrown/ranged `AvailableAttack` entry (test data) → attack order resolves instead of dropping. `Stop` mid-approach → unit idles, `UtilityActions` cleared. `ReturnToPlayer` from across the map → pathfinds to player.

## Open decisions (collected)

- [ ] §2 — command verbs for the slice: Stop + ReturnToPlayer (recommended) ± HoldPosition.
- [ ] §2 — resolution tiebreak when multiple attacks fit range: highest damage vs first-baked (recommend first-baked = designer-ordered priority).

---

## 12. Worktree build plan (2026-09-15) — spec id `minion-orders`

Every ← DECISION above is settled here. Names re-verified against `2464e854` on 2026-09-15.

### 12.1 Drift from the tree

- **`AvailableAttack` has no range** — it is `{ ActionType actionType; DamageSource damageSource; }`
  (`Components/Units/AttackComponents.cs:12`). §2's "pick the entry whose range best fits" cannot be built from
  the buffer; see MO-D3.
- The hard-coded order arm is `MinionActionSelectionSystem.cs:117-131` (`ActionType.MeleeSingle` twice: the
  `GetActionDefIndex` call and the `UtilityActions.actionType`).
  `BrainBlobUtils.GetActionDefIndex(ref BrainLibraryBlob, UnitType, ActionType)` returns `-1` on a miss.
- Execution-time resolution is `BehaviorCommands.RunRequestAttack` in `Utils/BehaviorCommands/RequestCommands.cs:10-45`
  (the interpreter was split after this spec was written). It matches `stateMachine.action` against the buffer to
  find the `DamageSource`; it needs **no change** once the order arm writes a resolved `actionType`.
- Command components live in `Components/Player/PlayerMinionCommandComponents.cs` (Move, Interact, Attack, Defend,
  Follow); the baker is `UnitBakingUtil.AddPlayerControlled` (`Utils/UnitBakingUtil.cs:202-219`); the issuer is
  `UnitSelectionManager.HandleCommand` (`MonoBehaviours/Managers/UnitSelectionManager.cs`, the F key at ~line 274
  is the Follow precedent). `ActionInterruptRequest` is at `Components/AI/UtilityAiComponents.cs:15`.
- Keys already bound in `MonoBehaviours/`: F, H, Tab, Home, F11, Semicolon, Backslash, Left/RightShift.

### 12.2 Decisions

- **MO-D1** Verbs: **Stop** and **ReturnToPlayer**. No HoldPosition (Defend already stations a minion).
- **MO-D2** Tiebreak: first-baked entry wins — designer order is priority.
- **MO-D3** Resolution rule (replaces the range idea): the ordered attack is the **first `AvailableAttack` entry
  whose `actionType` has an action def for this brain** (`GetActionDefIndex != -1`). No entry → the order is
  refused: the command is disabled, no option is emitted, and a Burst-safe log names the unit index and
  `EnumLogNames.Name(brain.unitType)`.
- **MO-D4** The helper is
  `AIUtils.ResolveOrderedAttack(ref BrainLibraryBlob blob, UnitType unitType, in NativeArray<AvailableAttack> attacks, out ActionType actionType, out int actionDefIndex)`
  returning `bool`; callers pass `buffer.AsNativeArray()`. Pure, Burst-safe, EditMode-testable with a
  `BlobBuilder`-built `BrainLibraryBlob` (see `BlobLibraryUtilsTests.cs` / `ConsiderationBlobTests.cs`).
- **MO-D5** Stop = `OnMinionStopCommand` (no payload): the arm enables `ActionInterruptRequest` on the unit, clears
  its `UtilityActions` buffer for this frame, disables the command, and enables `PlayerUnitBrain` like every other
  arm.
- **MO-D6** ReturnToPlayer = `OnMinionReturnCommand` (no payload): the arm reads the `Player` singleton's
  `LocalTransform.Position` and emits the same Move option the Move arm emits, `isPlayerOrdered = true`. It is a
  one-shot; Follow remains the continuous verb.
- **MO-D7** Keys: **X** = Stop, **R** = ReturnToPlayer (both free per 12.1), in `HandleCommand` beside the F
  precedent.

### 12.3 Tasks

- [x] **T0 — Ground (lead).** Claim; grep every name in 12.1; baseline gate as the Despawn plan's T0.
- [x] **T1 — Helper + fixture (one worker, two files).** `AIUtils.ResolveOrderedAttack` (MO-D4) and
  `Tests/AttackResolutionTests.cs` (namespace `StitchPunk.Tests`, EditMode): `FirstEntryWithADef_Wins`,
  `EntryWithoutADef_IsSkipped`, `EmptyBuffer_ReturnsFalse`. Revert-to-fail by returning the last match instead of
  the first. Gate: `StitchPunk.Tests.AttackResolutionTests` + the conformance pair.
- [x] **T2 — Order arm uses it (one worker, one file)** `[parallel-safe with T3]`:
  `MinionActionSelectionSystem.cs:117-131` replaces both `MeleeSingle` literals with the helper's outputs; the
  refusal path per MO-D3. The job needs a `BufferLookup<AvailableAttack>` (read-only) — add it beside the
  existing lookups.
- [x] **T3 — Components + bake (one worker, two files)** `[parallel-safe]`: `PlayerMinionCommandComponents.cs` gains
  `OnMinionStopCommand` and `OnMinionReturnCommand` (empty enableable structs with the one-line comment style of
  the file); `UnitBakingUtil.AddPlayerControlled` bakes both disabled. **Archetype change: needs a rebake** — say
  so in the report.
- [x] **T4 — Arms + input (one worker, two files, after T2 and T3).** `MinionActionSelectionSystem.cs` gains the
  Stop and Return arms (MO-D5, MO-D6; the `Player` singleton position is read once in `OnUpdate` and passed into
  the job); `UnitSelectionManager.HandleCommand` gains the X and R branches mirroring the F branch (MO-D7).
- [x] **T5 — Vault (docs worker)** `[parallel-safe]`: `Systems_AI.md` (order-time resolution, the two verbs, the
  refusal log), `Contracts.md` (two new command rows), `Components.md`.
- [x] **T6 — Close (lead).** Log, `status minion-orders ready`. Unverified until the owner plays: a ranged minion
  taking an attack order (no ranged unit exists yet — the fixture is the proof), X and R in the scene. The stage
  moves this file to `Tasks/Verification/` with a `verify-minion-orders.md` built from §10.

### 12.4 Log

**Phase 0 (stage, 2026-09-15).** Trunk `faebc8eb`; `doctor` clean (git 2.43.0, hooks installed, `brokerAlive` true, no
stage blockers); no worktrees. Compile gate clean. Baseline: `DotsAnimationToolkit.Tests.EditMode` 866 (865 passed,
standing Conformance_A), `.PlayMode` 285 of 285; `StitchPunk.Tests` **65** (63 passed: `SystemPlacementConformanceTests`
`EverySystemFileDeclaresAnUpdateGroup` and `EverySystemTypeCarriesAnExplicitGroupAttribute` failed on the owner's
converted damage stub `Systems/CombatSystemGroup/DamageEventSystemAnimEventSystem.cs`, which had no `[UpdateInGroup]`).
Fixed on the stage in `529e8bcc` (`[UpdateInGroup(typeof(CombatSystemGroup))]`), re-run 65 of 65.
`StitchPunk.Tests.PlayMode` **16** of 16. Floor for this batch: `StitchPunk.Tests` 65 (+ `AttackResolutionTests`),
`StitchPunk.Tests.PlayMode` 16 (+ despawn's `DespawnSystemTests`), both with zero failures.

**T0 (lead, worktree `spec/minion-orders`, 2026-09-15).** Claimed (opus lead, sonnet workers). Every 12.1 name
re-grepped and matched. Baseline gate on the conformance pair: pass, 9 of 9. Drift found (3):
- `EnumLogNames` had **no `UnitType` overload**, so MO-D3's log could not name the unit type. Added
  `Name(this UnitType)` beside the `ActionType` one (numeric default like the rest).
- `UnitSelectionManager.HandleCommand` runs only on a frame where `OnCommandPlayerInput` is enabled on the player
  (the command input), so the F precedent is "F held while issuing a command". X and R mirror it as MO-D7 says;
  they are **not** standalone key presses. Recorded as a trap in `Systems_AI.md`.
- `Contracts.md` had no minion-command rows and `Components.md` listed only Move/Interact (and named the parked
  `MinionCommandSystem`); both brought current.
X and R are unbound in every `.cs` and `.inputactions` file.

**Wave 1 (five parallel workers, one gate).** T1 helper + fixture; T2 + T4 arms folded into one worker on
`MinionActionSelectionSystem.cs` + `EnumLogNames.cs` (the T2/T4 split existed only for ordering; one worker
removed the second pass on the same file); T3 components + bake; X/R input + `Components.md`; `Systems_AI.md` +
`Contracts.md`. Arm order in the job: Move, Attack, Interact, Follow, Return, **Stop last** so a cancel beats
anything emitted the same frame.

**Gates.** Wave 1 committed as `3a0e7fa1` (explicit paths). Gate `AttackResolutionTests` + the conformance pair:
first run refused "Unity is compiling", retry **pass 12 of 12** (9 conformance + 3 fixture), no compiler or Burst
errors. **Revert-to-fail:** the mutation (loop walks backwards, so the last match wins) committed alone; its gate
refused "compiling" once, then **test-failures 11 passed / 1 failed**: `FirstEntryWithADef_Wins` expected
MeleeSingle, got ProjectileSingle. `git reset --hard HEAD~1`; `AIUtils.cs` sha256 `2bf6ad07…33e0` identical before
and after. Post-reset re-gate (one exit 3 heartbeat gap, retried): **pass 12 of 12**.

**Unverified (no Play mode this batch).** Compile of the non-test assemblies is proven only as far as the gate's
recompile reaches (it compiled clean). Burst compilation of `MinionActionWriteJob` with the new FixedString
interpolations (`orderedAttackType.Name()`, `brain.unitType.Name()`) is unproven until the job runs. The melee
characterization: a melee minion's attack order now uses its **first** `AvailableAttack` entry that has a def. It
behaves as before only if that entry is `MeleeSingle`; a unit whose SO lists `MeleeContinuous` first will now order
MeleeContinuous. The unit SO data was not inspected. No ranged minion exists, so the fixture is the proof.
X and R in the scene are unverified.

### For integration

- **Vault truths to check:** `Systems_AI.md` "Player / minion command pipeline" says seven `OnMinion*Command`,
  X = Stop / R = ReturnToPlayer with the `OnCommandPlayerInput` trap, `ResolveOrderedAttack` + the refusal Warning,
  and Stop last in arm order. `Contracts.md` has `OnMinionStopCommand` / `OnMinionReturnCommand` rows.
  `Components.md` lists all seven commands, consumed by `MinionActionSelectionSystem`, not the parked
  `MinionCommandSystem`. The other game lead also edits `Contracts.md` / `Components.md`; take both sides.
- **Rebake:** `UnitBakingUtil.AddPlayerControlled` bakes two new enableable components, so every
  player-controllable unit's archetype changes. Reopen the subscene or enter Play before judging anything.
- **Play checks for `verify-minion-orders.md`:**
  1. Attack-order a melee minion: it attacks as before. Check the log line
     `[MinionOrder] Unit N -> Attack T with <ActionType>` and confirm which attack it picked.
  2. Remove every `AvailableAttack` entry with a brain def from a test minion (or give it only an unmapped one) and
     order an attack: expect the Warning `refused Attack … for <UnitType>` and a minion that keeps its prior behavior.
  3. Hold X while issuing a command mid-approach: the unit interrupts to Idle.
  4. Hold R while issuing a command from across the map: the unit paths to where the player stood that frame and
     stops there (it does not keep following).
  5. F still follows continuously.

**Integration (stage, 2026-09-15).** Merged after `despawn` (`3985d330`); `Components.md` / `Contracts.md` auto-merged with
both leads' rows. Compile gate clean. Suites: `StitchPunk.Tests` **68 of 68** (floor 65 + `AttackResolutionTests` 3),
`StitchPunk.Tests.PlayMode` 19 of 19, package EditMode 866 (standing Conformance_A only, before A102's merge), PlayMode 285.
Stage check of the lead's "melee orders may change" note: every unit asset carries one attack, `MeleeContinuous` (17);
`PlayerZombieBrain` defines `MeleeContinuous` but not `MeleeSingle`, so the old hard-coded `MeleeSingle` lookup missed on
zombie minions and dropped their attack orders — the resolver fixes that rather than changing melee. X and R are read
inside `HandleCommand` (held while the command input fires), like F; the owner's key-feel question is in the verify file.
Spec moved to `Tasks/Verification/` with `verify-minion-orders.md`.
