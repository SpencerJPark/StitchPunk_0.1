# AI Foundations Pass: Design Spec

> **Status:** BUILT 2026-10-17 (AF-P0–P4), now in Verification. Play checklist: `verify-ai-foundations.md`. AF-P6 (perception cache + generic option generation) needs its own spec. The owner delegated every call except one: priority tiers stay the only way to preempt, with no same-tier inertia (AF-D1). All decisions are below, so do not re-ask.
> **Source:** the 2026-10-17 review of UtilityAISystemGroup against the "flat utility AI in DOTS" checklist.
> **Read first:** `Memories/Code/Systems_AI.md`, `Plans/SmartHumanAI_System.md` (H4 waits on this pass), `Plans/CrowdScaleAwareness_System.md` (AF-P6 absorbs it).

**Skills Needed:** `dots-system-scaffold` (TargetOccupancySystem, TargetReservationSystem), `dots-test` (reservation fixture).

## 1. Why

The core was sound: flat scoring, Burst parallel jobs, enableable components, one command interpreter. The review found these problems:

- **Bugs:** a parallel pickup race (`ItemCommands.RunRequestPickup` checks and then writes `EquipBy` on a foreign entity). Motivation is clamped to 0–100 but normalised with `(v+100)/200`, so half of every curve is dead. `CurrentAction` is never written, so ItemAwareness thinks `inCombat` is always false. EnemyAwareness runs single-threaded only because of a logging ECB. The makeup factor is applied to the product instead of to each consideration.
- **Exclusive targets don't exist:** two units sit in the same chair or grab the same rock. `InteractionSO.maxOccupants` is authored and baked, but nothing reads it.
- **Overrides are hard-coded:** the priority tiers live in six awareness files. The UtilityActionSO priority can only raise an option's tier, and a behaviour can't protect itself from preemption.
- **Bloat:** dead fields (`needsValidation`, `maxCandidates`, `targeting`, `ActionSelectionValidationRequest`, `GlobalContext`, `AngeryMobSignal`, `DangerSignal`). Three copies of the StateMachine assign block. Modules are 1:1 wrappers.
- **Thinking:** idle units think every frame, and units without `ThinkRate` (zombies) always think every frame.

## 2. Decisions (owner-delegated, 2026-10-17)

| # | Decision |
|---|---|
| AF-D1 | **Priority tiers are the only preemption path** (owner). There is no same-tier inertia, and utility decides only at decision points. |
| AF-D2 | **Modules become bundles:** `BehaviourModuleSO.actions` is a list (Camp Life = Sit + Eat + Music). `requires` tags stay as editor metadata for the later brain editor (SH-D7). A single-action module is just a one-item list. |
| AF-D3 | **Per-brain priority overrides** live on `BrainSO.priorityOverrides` (ActionType → priority), inherited along `baseBrain` with the child winning. The bake emits a separate action def per (action, priority) pair, so the runtime is unchanged. This is how "an aggressive unit drops its task to fight" is authored. |
| AF-D4 | **The tier ladder lives in one file:** `AIPriorityTier` (Data/Enums): Idle 0, Ambient 1, Engage 2, Defend 3, Critical 4, Leash 5. Awareness systems use the named tier for *context bumps* only (being hit, an alarm, critical flee, mid-fight grab, throw, leash). Plain detection uses the action's authored priority. EnemyAwareness emits 0, and the melee actions are authored at Defend (3) so behaviour is unchanged. |
| AF-D5 | **`BehaviorSO.minimumInterruptPriority`** (default 0): a live behaviour is preempted only by a winner whose priority is ≥ this as well as > activePriority. Player orders ignore it. A per-command uninterruptible window is out of v1. |
| AF-D6 | **Reservations are derived, not stored:** a target's occupants are the units whose StateMachine (active or pending) targets it. Capacity comes from `InteractionBlob.maxOccupants` (0 = unlimited) or 1 for an `Item`. Nothing needs releasing, so a dead or retargeted holder frees the slot on its own. |
| AF-D7 | **Arbitration happens after commit and reverts losers:** `TargetReservationSystem` (single-threaded, after WinnerSelection) accepts new claims in iteration order until capacity is reached. A loser goes back to idle (fresh start) or has its pending fields cleared (preemption), and is forced to re-think. Player orders always accept and may exceed capacity. |
| AF-D8 | **Think schedule:** every brain unit gets `ThinkRate`. `interval <= 0` keeps every-frame thinking. An idle unit thinks on the frame it becomes idle, then every `ThinkRate.IDLE_THINK_INTERVAL` (0.1 s), not every frame. Distance LOD is deferred until a profile asks for it. |
| AF-D9 | **Reactive systems stay ungated** (SelfDefence, Flee, AllyAlert response, GuardPost). The rule: ungated only if it is O(own threats) and must react mid-behaviour. The `reactionDelay` tick moves into `ThreatDecaySystem`, which removes the SelfDefence→Flee ordering trap. |
| AF-D10 | **Motivation normalises as `value/100`**. Every Motivation consideration curve is remapped by `f_new(r) = f_old((r+1)/2)` (keys `t → 2t−1`, tangents × 0.5), so every score in 0–100 is preserved exactly. |
| AF-D11 | **Makeup factor per consideration** (Dave Mark), applied before the product. Weight becomes `lerp(1, score, weight)` (importance). Every authored weight is 1 today, so the weight change is a no-op. The makeup change raises two-consideration actions slightly, which favours the camp needs the owner just tuned towards. |
| AF-D12 | **ActionSelectionSystemGroup becomes top-level:** `UtilityAI → MinionActionSelection → ActionSelection → StateMachine`, giving three named steps: sense and offer, decide, execute. |
| AF-D13 | **One StateMachine transition helper:** `StateMachineTransitions` (Utils): `StartBehavior`, `SetPending`, `ClearPending`, `ResetToIdle`. It is used by WinnerSelection, SocialResponse, BehaviorInterrupt, the BehaviorExecution Complete phase, and arbitration. |

## 3. Data model changes

- `UtilityActions`: drop `needsValidation`.
- `UtilityAiComponents.cs`: delete `CurrentAction`, `ActionSelectionValidationRequest`, `GlobalContext`, `AngeryMobSignal`, `DangerSignal`.
- `ThinkRate`: `+ bool wasIdle`, `+ const IDLE_THINK_INTERVAL = 0.1f`.
- `ActionDefBlob`: drop `targeting` and `maxCandidates`. `UtilityActionSO`: drop the same fields (orphan YAML is harmless). `TargetingMode` enum deleted.
- `BehaviorConfigBlob`: `+ int minimumInterruptPriority`. `BehaviorSO`: the same field.
- `BrainSO`: `+ List<ActionPriorityOverride> priorityOverrides` and `+ bool TryGetPriorityOverride(ActionType, out int)`. `BehaviourModuleSO.action` becomes `List<UtilityActionSO> actions`.
- New `TargetOccupancy` singleton: `NativeParallelMultiHashMap<Entity, Entity> occupantsByTarget`, rebuilt each frame.

## 4. Systems

- **New** `UtilityAwarenessSystemGroup/TargetOccupancySystem`: rebuilds the occupancy map after ClearOptions.
- **New** `ActionSelectionSystemGroup/TargetReservationSystem`: arbitration, OrderLast, after WinnerSelection.
- **Edited:** ConsiderationScoring, WinnerSelection, EnemyAwareness, ItemAwareness, InteractionAwareness, SelfDefence, ThreatDecay, ThinkSchedule, SocialResponse, BehaviorInterrupt, BehaviorExecution, BrainLibraryBaking, BehaviorLibraryBaking, UnitBakingUtil.

## 5. Build phases

| Phase | Content |
|---|---|
| AF-P0 | Orchestrator, mechanical: this spec, the group move, `AIPriorityTier`, removal of dead fields and types, the transition helper, the `ThinkRate` field, the module YAML migration, melee priority 3. |
| AF-P1 | Workers, wave 1: scoring maths, EnemyAwareness, ItemAwareness + baking, ThinkSchedule + tests, ThreatDecay + SelfDefence, brain bake overrides, BrainSO + modules, module consumers. Orchestrator: curve remap in the Editor. |
| AF-P2 | Workers, wave 2: WinnerSelection (helper + interrupt threshold), interrupt and social response through the helper, execution Complete through the helper, the BehaviorSO field and its bake. |
| AF-P3 | Workers, wave 3: occupancy system, reservation system, awareness filters, PlayMode reservation fixture. |
| AF-P4 | Orchestrator: Editor compile, touched fixtures, rebake, vault notes, commit. |
| AF-P5 | **Owner play-test** (`verify-ai-foundations.md`). |
| AF-P6 | **Later, its own spec:** a perception cache (unit spatial hash from CrowdScaleAwareness, plus nearestHostile / topThreat / alertTarget written once per think), `UtilityActionSO.targetSource` with one generic option-generation job, and consideration inputs that replace the motivation-as-signal writes (BloodLust/SelfPreservation/SelfDefence = 100). This is the step that makes a new behaviour an editor-only job. |

## 6. Verification

The compile gate runs per wave. EditMode tests: BrainComposition, SystemGroupOrder, SystemPlacementConformance, BehaviorCommandCatalog, UnitBrainCoverage. PlayMode tests: SmartHumanAi, BehaviorExecution, MinionStandingOrder, and the new reservation fixture (proved red by skipping the arbitration). Then a rebake and the owner's play checklist.

## Open decisions

None. Every fork above is settled.
