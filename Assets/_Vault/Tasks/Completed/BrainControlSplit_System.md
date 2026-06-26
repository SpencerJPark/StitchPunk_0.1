# Brain Control Split System — Death Blank-Slate & Player-Controlled Revival

> **Status:** 🔨 built — all C# landed (regate to `WithPresent`, death blank-slate, player-controlled revive, `MinionSelfDefenceAwarenessSystem`). Compile + play-test pending → see [`verify-brain-control-split.md`](verify-brain-control-split.md).
> **Origin:** surfaced while writing [`verify-minion-revival.md`](verify-minion-revival.md) — extends [`MinionRevival_System.md`](MinionRevival_System.md).

---

**Skills Needed** (project skills in `.claude/skills/` — see [Skills index](../../Memories/Code/Skills.md)):
- `dots-unit-ai` — the new `MinionSelfDefenceAwarenessSystem` (awareness emitting `UtilityActions`) and the regating of existing awareness/execution systems (§5).
- `dots-system-scaffold` — conventions for the new awareness system file (§5).

---

## 1. Purpose & v1 scope

While writing `verify-minion-revival.md`, three problems surfaced: death does **not** clear the
`ThreatEntry` buffer, death disables `AttackRequest` but does **not** reset its fields, and the
"utility brain" concept is mis-used. In the intended mental model the AI has **two halves** —
*Utility AI* (the **decision/awareness** half, "where decisions come from") and the *StateMachine*
(the **execution** half). `UtilityBrain` should gate **only** the decision half.

This spec splits `UtilityBrain` (decision) from the StateMachine (execution) so disabling
`UtilityBrain` cleanly turns off **only** autonomous decision-making, then wires death → blank
slate, revive → player control, plus a minion-side self-defence that yields to player orders.

**v1 handles:**
- Death clears `ThreatEntry` + resets `AttackRequest`, and **disables `UtilityBrain`**.
- Execution + shared-selection systems run regardless of `UtilityBrain` enabled-state.
- Revive enables `PlayerUnitBrain` + `Minion` (player-controlled), leaves `UtilityBrain` disabled.
- `SwapBrainSystem` still converts the brain (faction/attacks/motivations) with `UtilityBrain` disabled.
- New `MinionSelfDefenceAwarenessSystem`: a player minion self-defends only when it has no active player command.

**Out of v1:** autonomous (non-player) minions; per-minion stance toggles (aggressive/passive);
re-acquiring Utility AI after deselection. A corpse whose `becomesUnitType == None` is **not
revivable** — the request is consumed and it stays dead.

## 2. Architecture

**The catch found during grounding:** today `UtilityBrain` is the master gate for the *entire*
pipeline — the execution systems (`BehaviorExecutionSystem`, `BehaviorInterruptSystem`), the
shared selection/clear infra (`WinnerSelectionSystem`, `ClearOptionsSystem`), **and** the
player-command translator (`MinionActionWriteJob` requires `UtilityBrain` **and**
`PlayerUnitBrain`). So simply disabling `UtilityBrain` on death would make the unit fully inert.
The fix is to **decouple execution from `UtilityBrain`**.

Two halves, gated independently:

```
UtilityBrain  (enableable)    ──gates──▶  DECISION half ("Utility AI")
                                          • Awareness: SelfDefence, Flee, Social, Item, ...
                                          • ConsiderationScoring
                                          • Motivation systems
StateMachine  (always present) ─gates──▶  EXECUTION + shared selection/clear (always runs)
PlayerUnitBrain (enableable)  ──gates──▶  PLAYER decision source
                                          • MinionActionSelection
                                          • new MinionSelfDefence
```

- **Decision half** keeps `[WithAll(typeof(UtilityBrain))]`. Disable `UtilityBrain` → these stop.
- **Execution + shared selection/clear** is regated from `[WithAll(typeof(UtilityBrain))]` to
  **`[WithPresent(typeof(UtilityBrain))]`** — runs on units that *have* the component, enabled
  **or disabled** (corpses + player minions), while still letting the existing `in UtilityBrain
  brain` params read `brain.unitType`. `WithPresent` is the minimal change — no need to swap
  unitType sources to `UnitData.unitType`.
- **Player half** gates on `PlayerUnitBrain`, not `UtilityBrain`.

**Why this is safe:** the death/teardown path (`ActionInterruptRequest` → `BehaviorInterruptSystem`)
and the death-behavior execution **must** keep running after `UtilityBrain` is disabled —
`WithPresent` guarantees that. Player command translation/selection must run with `UtilityBrain`
disabled — `PlayerUnitBrain` gate + `WithPresent` on shared selection guarantee that.

## 3. Entry points

All reuse existing enableable request/tag components — **no new components**:
- **Death** — `Dead` (enabled by `DamageApplicationSystem`); `DeathSystem` does the teardown
  (now also disables `UtilityBrain`, clears `ThreatEntry`, resets `AttackRequest`).
- **Revive** — `ReviveRequest` consumed by `ReviveRequestSystem`; now enables `PlayerUnitBrain`
  + `Minion` instead of re-enabling `UtilityBrain`.
- **Conversion** — `SwapBrainRequest` consumed by `SwapBrainSystem` (regated to run with
  `UtilityBrain` present-but-disabled).
- **Player control** — `PlayerUnitBrain` (existing) is the control tag; `OnMinion*Command`
  one-shot command components feed `MinionActionWriteJob`.

## 4. Data model

No new SO/Blob library, no new enums, no new components. Reuses:
- `UtilityBrain { UnitType unitType }`, `PlayerUnitBrain {}` (`Components/AI/UtilityAiComponents.cs`).
- `StateMachine` (non-enableable; `UtilityAiComponents.cs:39`) — the always-present execution-gate.
- `ThreatEntry` buffer (`UtilityAiComponents.cs`), `AttackRequest` (`Components/Units/AttackComponents.cs`).
- `Minion` enableable tag (`Components/Units/UnitComponents.cs:82`).

## 5. Systems

### Regate (drop `[WithAll(typeof(UtilityBrain))]` → add `[WithPresent(typeof(UtilityBrain))]`)
Must run for corpses + player minions (`UtilityBrain` disabled):
- `BehaviorExecutionSystem` (`ActionExecutionSystemGroup`) — keeps its `in UtilityBrain brain` params (`:156`, `:207`).
- `BehaviorInterruptSystem` (`ActionExecutionSystemGroup`, OrderFirst) — death teardown path (`:61`).
- `WinnerSelectionSystem` (`ActionSelectionSystemGroup`, OrderLast) — turns player `UtilityActions` into a `StateMachine` decision (`:49`).
- `ClearOptionsSystem` (`AIAwarenessSystemGroup`, OrderFirst) — must clear the `UtilityActions`
  buffer for player minions too; keep its `[WithDisabled(typeof(Dead))]`.

> Per-system audit when editing: if a regated job reads `in UtilityBrain`, `WithPresent` keeps it
> readable; if any cannot use `WithPresent` cleanly, fall back to `in UnitData unitData` +
> `unitData.unitType` (`SwapBrainSystem`/bake keep `UnitData.unitType` in sync).

### Keep `[WithAll(typeof(UtilityBrain))]` (the genuine Utility-AI decision half)
- `ConsiderationScoringSystem` (`ActionSelectionSystemGroup`) — player options are `isPlayerOrdered`, never scored, so not running for player minions is correct.
- Awareness systems: `SelfDefenceAwarenessSystem`, `FleeAwarenessSystem`, `SocialAwarenessSystem`, `ItemAwarenessSystem` (+ any awareness sibling) — `AIAwarenessSystemGroup`.
- `MotivationChangeRequestSystem` (+ motivation siblings).

### Change player-command gate
- `MinionActionSelectionSystem` / `MinionActionWriteJob` (`MinionActionSelectionSystemGroup`):
  change `[WithAll(typeof(UtilityBrain), typeof(PlayerUnitBrain))]` →
  `[WithAll(typeof(PlayerUnitBrain))][WithPresent(typeof(UtilityBrain))]`, keeping the `in UtilityBrain brain` read.

### New system
- **`MinionSelfDefenceAwarenessSystem`** — `[UpdateInGroup(typeof(MinionActionSelectionSystemGroup))]`,
  gated `[WithAll(typeof(PlayerUnitBrain))]`. Mirrors `SelfDefenceAwarenessSystem`: reads
  `ThreatEntry`, targets the highest-threat alive in-range attacker, emits a self-defence
  `UtilityActions` attack option at a priority **below** a player order — so it only takes effect
  when the minion has **no active player-ordered behavior** (`WinnerSelectionSystem` won't clobber
  a live higher-priority player behavior). `← DECISION: priority tier + "no active command" test
  (idle-only vs. compare StateMachine.activePriority).`

### Death / revive / conversion edits
- `DeathSystem` (`Systems/HealthSystemGroup/DeathSystem.cs`): **disable `UtilityBrain`** (add lookup);
  **`threats.Clear()`** on `ThreatEntry`; reset `AttackRequest` to `default` in addition to disabling it (`:96`).
- `ReviveRequestSystem` (`ReviveRequestSystem.cs`): bail (consume the request, stay dead) when
  `becomesUnitType == None`; otherwise restore health, drop `Dead`, stamp `SwapBrainRequest`, and
  enable `PlayerUnitBrain` + `Minion` — **never** re-enable `UtilityBrain` — then fire
  `ActionInterruptRequest`.
- `SwapBrainSystem` (`SwapBrainSystem.cs`): add `[WithPresent(typeof(UtilityBrain))]` so the
  conversion runs while `UtilityBrain` is disabled; keep writing `utilityBrain.unitType` + `unitData.unitType`.

## 6. MonoBehaviour bridge

`UnitSelectionManager.cs` already enables `PlayerUnitBrain` on command (5 call sites) — unchanged.
Revival now front-loads `PlayerUnitBrain` so a minion is player-controlled before the first command.

## 7. Integration points

- **Combat:** `ThreatEntry` (cleared on death; read by new minion self-defence), `AttackRequest` (reset on death).
- **AI pipeline:** the `UtilityActions` → `StateMachine` flow; `WinnerSelectionSystem` `isPlayerOrdered` rule already lets player orders win unconditionally.
- **Death/revive:** `DamageApplicationSystem` (enables `Dead`), `BehaviorInterruptSystem` (teardown), `SwapBrainSystem` (conversion).
- **Save:** `Minion` is `IPersist`; no DTO change. Re-verify Phase 5 of `verify-minion-revival.md` (control-tag now on at revive).

## 8. Proposed file manifest

**New:**
- `Assets/_Scripts/Systems/MinionActionSelectionSystemGroup/MinionSelfDefenceAwarenessSystem.cs`

**Edited:**
- `Systems/StateMachineSystemGroup/ActionExecutionSystemGroup/BehaviorExecutionSystem.cs` — regate to `WithPresent`.
- `Systems/StateMachineSystemGroup/ActionExecutionSystemGroup/BehaviorInterruptSystem.cs` — regate to `WithPresent`.
- `Systems/UtilityAISystemGroup/UtilityDecisionSystemGroup/WinnerSelectionSystem.cs` — regate to `WithPresent`.
- `Systems/UtilityAISystemGroup/ActionAwarenessSystemGroup/ClearOptionsSystem.cs` — regate to `WithPresent` (+keep `WithDisabled(Dead)`).
- `Systems/MinionActionSelectionSystemGroup/MinionActionSelectionSystem.cs` — gate on `PlayerUnitBrain` + `WithPresent(UtilityBrain)`.
- `Systems/HealthSystemGroup/DeathSystem.cs` — disable `UtilityBrain`, clear `ThreatEntry`, reset `AttackRequest`.
- `Systems/HealthSystemGroup/ReviveRequestSystem.cs` — enable `PlayerUnitBrain`+`Minion`, stop re-enabling `UtilityBrain`.
- `Systems/HealthSystemGroup/SwapBrainSystem.cs` — `WithPresent(UtilityBrain)`.

**Docs:** update `../Verification/verify-minion-revival.md` (Phase 4 "autonomous until commanded" →
"player-controlled at revive; self-defends when uncommanded"); `../../Memories/Code/Systems_AI.md`.

## 9. Build phases

1. **Decouple execution from `UtilityBrain`** — regate the 4 shared/execution systems to
   `WithPresent`; compile; confirm normal (alive) units behave identically.
2. **Death blank-slate** — `DeathSystem`: disable `UtilityBrain` + clear `ThreatEntry` + reset
   `AttackRequest`. Confirm a killed unit still ragdolls (interrupt + death behavior run) and stops
   making decisions.
3. **Player-controlled revive** — `ReviveRequestSystem` enables `PlayerUnitBrain`+`Minion`;
   `SwapBrainSystem`/`MinionActionWriteJob` regate. Confirm a revived corpse is commandable with
   `UtilityBrain` disabled.
4. **Minion self-defence** — add `MinionSelfDefenceAwarenessSystem`; confirm an uncommanded minion
   fights back, and a player order overrides it.

## 10. Verification

Play `Assets/Scenes/TestArea/DOTSTestScene.unity`, inspect in the Entities window:

- **Phase 1:** Kill + revive an ordinary citizen — identical behavior to today (regate is a no-op while `UtilityBrain` stays enabled on living units). No compile errors.
- **Phase 2:** Kill a unit → `UtilityBrain` **disabled**, `ThreatEntry` buffer **empty**,
  `AttackRequest` fields zeroed; ragdoll/death animation still plays (proves StateMachine half runs with `UtilityBrain` off).
- **Phase 3:** Revive a (convertible) corpse → `PlayerUnitBrain` **enabled**, `Minion` **enabled**,
  `UtilityBrain` **disabled**; `Faction`/`AttackFaction`/`AvailableAttack` rebuilt to the zombie form
  (SwapBrain ran while disabled). Right-click a human → it paths in and bites; right-click ground → it moves.
- **Phase 4:** Leave the revived minion uncommanded near a hostile → it self-defends (new system).
  Issue a move order → it obeys and ignores the threat until the order completes.
- **Editor-only / Spencer:** the `PlayerZombie` `UnitSO` + `becomesUnitType` wiring + revivable
  prefabs (already tracked in `verify-minion-revival.md`); Phase 5 save round-trip.

## Open decisions (resolved at build)

- [x] §5 — `MinionSelfDefenceAwarenessSystem`: emit at `SELF_DEFENCE_PRIORITY = 3` and rely on the
  `activePriority` gate. Player orders run at `int.MaxValue`, so a priority-3 self-defence option
  can never preempt a live player order — "self-defend unless commanded" falls out for free, no
  idle check needed.
- [x] §5 — Control tag: **reuse `PlayerUnitBrain`** (enabled at revive; no new component).
- [x] §1/§5 — Revive with `becomesUnitType == None`: **the corpse is not revivable at all** — the
  request is consumed and the unit stays dead (refined from "leave disabled"). Every successful
  revive is a player-controlled minion.
- [x] §5 — Death-cleanup breadth: **also clear `MotivationChangeRequest` + `RecentInteraction`**
  (plus `ThreatEntry` + `AttackRequest`) for a full blank slate.
- [x] §5 — `WithPresent` worked for every regated system (`in/ref UtilityBrain` stays readable);
  no fallback to `UnitData.unitType` was needed except in the new minion system, which reads
  `unitData.unitType` because its query requires `UtilityBrain` **disabled**.

## Known limitations

- `MotivationChangeRequestSystem` stays gated on `UtilityBrain`, so a player minion's
  `MotivationChangeRequest` entries (e.g. from a player-ordered Sit's `ModifyMotivation`) are not
  drained while it's a minion. Benign for zero-decay zombies, and the buffer is cleared on death;
  regate later if minions ever need live needs.
