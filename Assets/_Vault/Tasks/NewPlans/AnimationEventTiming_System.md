# Animation Event Timing — Design Spec (StateMachine/AI simplification)

> **Status:** 📝 spec drafted — decisions open, awaiting owner edit
> **Raw source:** [`../Claude/Systems_Gap_Audit_2026-08.md`](../Claude/Systems_Gap_Audit_2026-08.md) area 2
> **Prerequisites:** [`AnimationToolkitMigration_System.md`](AnimationToolkitMigration_System.md) phases 1–5 (events only exist on toolkit actors), and `Plans/BehaviorCommandSplit_System.md` (spec ready) built **first** — new command arms land in the split-out command classes, not the monolith switch.

---

**Skills Needed:**
- `dots-system-scaffold` — the event-latch system (§4)
- `dots-test` — EditMode coverage for the wrap/window crossing latch (pure math)

---

## 1. Purpose

Every AI/combat duration today is a hand timer that must agree with a clip by coincidence:

- `AttackBlob.hitTime` (default 0.3s) — `AttackRequestSystem`'s windup before the `DamageEvent` enqueue.
- `PlayerAttackSystem`'s `cooldown = max(cooldown, hitTime + 0.05)`.
- Every `WaitTime` behavior command that exists only to cover a clip's length (Pickup's `WaitTime 1s`, Talk beats).

Re-time a clip in the Clip Editor and none of these follow. This plan makes authored animation events (`AnimEventOutput`: typed key ≥16 + int/float payload, reserved `ClipFinished`=1 / `ClipResolveFailed`=2) the timing source, so **retiming a clip retimes the gameplay for free**.

## 2. The latency contract — decide once, write it down

`StateMachineSystemGroup` and `CombatSystemGroup` run **before** `AnimationToolkitSystemGroup` in the frame; the toolkit clears and re-emits `AnimEventOutput` during its own update. Consumers ordered earlier therefore read events **one frame late** — the toolkit documents this as its contract.

**Accept the frame.** 16ms at 60fps is invisible on a swing; reordering animation before combat would break the "state machine decides → animation obeys" direction of every other system. Consequence: an event emitted on the toolkit's frame N is acted on at frame N+1, and the buffer is valid until the toolkit's next update — earlier-in-frame consumers read it safely. Record this in `Systems_AI.md` so a future session doesn't "fix" the order. ← DECISION (accepting is the recommendation; the alternative — a second command-apply pass after the toolkit — buys 16ms for real ordering complexity).

## 3. New behavior commands

Two new **blocking** `BehaviorCommandType` values, implemented as split-out command classes:

- **`WaitForAnimEvent { eventKey, layerIndex }`** — completes when the actor's `AnimEventOutput` holds the key (checked via `AnimEventsPending` gate, so event-less frames cost nothing).
- **`WaitForClipFinished { layerIndex }`** — completes on `ClipFinished` for that layer; **also completes on `ClipResolveFailed`** (a missing clip must not hang a behavior forever — same missing-data-completes philosophy as the existing qualifier semantics).

Both carry a **timeout float baked from the SO (0 = none)** as the safety rail; on timeout, complete with a warning log. The bake-validation catalog (`BehaviorBakeValidation`, already built) gains both commands so a designer can't author them before the interpreter arm exists.

`WaitTime` stays — it is still right for real durations (Sit's 8 seconds is a design choice, not a clip length).

## 4. Combat: hitTime → Hit event

`AttackRequestSystem` keeps its shape (armed `AttackRequest`, range/alive re-check, `DamageBus.raw` enqueue) but the trigger changes: instead of `elapsed >= attackBlob.hitTime`, it fires when the attacker's `AnimEventOutput` carries `AnimEvents.Hit` (layer = Action). Player attacks get the same for free — `PlayerAttackSystem` only *starts* swings.

- **Fallback:** if the attack's clip has no Hit event authored (or the unit has no toolkit actor), the `hitTime` timer path remains — `hitTime` becomes the documented fallback, with a bake-time warning when an attack clip lacks the event. ← DECISION: keep-fallback-forever vs delete `hitTime` once all attack clips carry events. *Recommendation: keep it one milestone, then delete — permanent dual paths are how the two vocabularies drifted last time.*
- **Cooldown:** `PlayerAttackSystem`'s `hitTime + 0.05` floor becomes "cooldown starts at `ClipFinished`" or simply keeps the authored cooldown — ← DECISION, recommend the latter (cooldown is game feel, not sync).
- Multi-hit attacks (future combos) fall out free: two Hit events on one clip = two enqueues — the `intParam` payload can carry a damage-scale index later.

## 5. What shrinks

- `UnitAnimationAssignmentSystem`'s Action-layer half: with behaviors owning action clips via commands and `WaitForClipFinished` handling handback, assignment reduces to the Base locomotion layer (idle/walk/stance from velocity). The "non-looping layer owns until finished" dance and `HasActiveNonLoopingLayer` disappear.
- Behavior assets: Pickup's `WaitTime 1s` → `WaitForClipFinished`; Talk/Sit keep `WaitTime` where the duration is design, not clip cover.
- The audit's future consumers (ragdoll triggers, dialogue cues, shader alt-views) are **explicitly not built here** — this plan establishes the consumption pattern (the sound consumer from the migration plan is the template; combat is the second instance).

## 6. Proposed file manifest

**New:** `Utils/BehaviorCommands/WaitForAnimEventCommand.cs`, `WaitForClipFinishedCommand.cs` (homes per the Split plan's layout)
**Edited:** `AiEnums.cs` (+2 `BehaviorCommandType`, append-only) · `BehaviorSO.cs`/`BehaviorBlobs.cs` (command fields) · behavior bake-validation catalog · `AttackRequestSystem.cs` · `PlayerAttackSystem.cs` · `AttackSO`/`AttackBlobs` (hitTime demoted to fallback + tooltip) · `UnitAnimationAssignmentSystem.cs` (shrink) · `Systems_AI.md` (latency contract + new commands) · `Contracts.md` (`AnimEventOutput` consumer rows)
**Assets:** Hit events authored on Claw/Punch clips; Pickup behavior re-authored.

## 7. Build phases

1. Command enum + blob + bake-validation entries (no interpreter arm yet — validation proves the guard works).
2. The two command classes + interpreter dispatch; Pickup behavior converted; verify approach→pickup→handback in play.
3. Combat: Hit event on attack clips, `AttackRequestSystem` event trigger + fallback warning; player attack follows.
4. Assignment shrink + docs (`Systems_AI.md`, `Contracts.md`).
5. Verify → retire to `Verification/` with `verify-animationeventtiming.md`.

## 8. Verification

- Retime the Punch clip's Hit event in the Clip Editor → the damage moment moves in play with **no SO edit** (the whole point — demo this one first).
- Pickup completes exactly when its clip ends at 1×, at 2× speed, and while off-screen (logic group never gates on visibility — events still fire).
- An attack clip with no Hit event logs the bake warning and still lands damage via the hitTime fallback.
- A behavior waiting on a clip whose id fails to resolve completes via `ClipResolveFailed` + timeout instead of hanging.
- Interrupt mid-`WaitForAnimEvent` (flee preemption) tears down cleanly — the command holds no state outside `StateMachine`.

## Open decisions

- [ ] §2 latency: accept the 1-frame contract (recommended)
- [ ] §4 hitTime fallback: temporary (recommended) vs permanent
- [ ] §4 player cooldown source: authored value (recommended) vs ClipFinished
