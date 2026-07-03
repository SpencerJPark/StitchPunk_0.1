# Minion Order Robustness — Design Spec

> **Status:** ✅ spec ready · edit the inline **← DECISION** markers, then hand back to start the build.
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
