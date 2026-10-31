# Next session: Smart Human AI H4 (tactics) on the new AI foundations

You are the **orchestrator**. Build SmartHumanAI **H4: Keep Range, Heal When Hurt, Retreat to Allies**. The owner wants this before the CampLoop Phase 6 raid. Edits go to **`worker` subagents on smaller models** (Sonnet for logic, Haiku for mechanical work). Set `model` explicitly on every spawn; the owner asked for this.

## Read, in order
1. Root `CLAUDE.md`, then `Assets/_Vault/Memories/Code/Systems_AI.md` whole. It is current as of 2026-10-19 and holds the tier ladder, reservations, think rate, leash and three fixed traps.
2. `Assets/_Vault/Tasks/Plans/SmartHumanAI_System.md` §8 (the H4 sketch) and §9 (SH-D1–D9, do not re-ask).
3. `Assets/_Vault/Tasks/Verification/AIFoundations_System.md` §2 (AF-D1–D13, settled; AF-D1 "tiers are the only preemption path" is the owner's own call).

## State on 2026-10-19 (all on `main`, pushed)
- `bac717b2` AI Foundations: `AIPriorityTier`, brain `priorityOverrides`, `BehaviorSO.minimumInterruptPriority`, derived reservations (`TargetOccupancy` + `TargetReservationSystem`), every brain has a `ThinkRate` with a 0.1 s idle re-think, module bundles, motivation `value/100`.
- `786e23fb` Play-found freezes fixed. Reservations are action-matched (every unit prefab inherits `Interaction(Talk)` from MaleCitizen). Animation waits cannot hang (no unit has animation data). Waypoints bake `Dynamic`.
- `3b2e57d7` + `e8308a86` Guard leash: a guard gives up only when the hostile is outside the leash AND farther than `GuardPost.keepFightingWithinMeters` (6 m) from the guard. A guard walking home turns back for an intruder inside 0.8 × leash.
- `ce60206d` Falling through the floor fixed in `com.dotsmovementtoolkit` (`GravityStep`: a swept fall, a 0.5 m probe, and a recovery net at 20 m below the last ground).
- Live in CampLoop: bandits break off tasks, grab rocks, throw them, melee, and keep fighting next to you; citizens and bandits keep cycling with nobody stalled. PlayMode 71/71 plus the package EditMode tests.

## How H4 fits the foundations (decide in the spec, don't re-derive)
- **Tiers:** use `AIPriorityTier` names only, never a raw number. Heal When Hurt's "combat-time bump" is a context bump in the awareness system: `Critical` beats a live `Defend` fight. Retreat at < 30 % HP fits Critical too; check it against critical Flee (also Critical) so the two do not fight.
- **Who gets a behaviour:** per-brain differences go in `BrainSO.priorityOverrides` or a module bundle, not in C# branches on UnitType.
- **Keep Range:** it moves to a *position*. Use `hasTargetPosition` options (GuardPost is the example). The leash will strip combat options only by the target rule. A Keep Range move is not a combat action, so decide whether `GuardPost` should clamp its position inside the leash.
- **Healing items:** they are `Item`s, so reservations already stop two units grabbing one item. Do not add a release step.

## Traps that cost time this week
- **Offline gate across assemblies:** `devloop gate` compiles one assembly against the *Editor-built* DLLs of its references. A new member in Components, Data or Utils shows CS1061/CS1501 in Systems until the Editor recompiles. That is not a code error: `refresh_unity`, then re-gate.
- **Stale Burst after revert-to-fail:** after a mutation, Burst can keep serving the *mutated* job with Burst back on, even after a byte-identical restore. Run the revert-to-fail with Burst off, then make an expression-shape change (hoist into a named local) and run the suite once with **Burst on** before committing. Memory: `feedback_burst_stale_after_revert`.
- **Debug live, don't guess:** in Play mode, `execute_code` (CodeDom C# 6) can query `World.DefaultGameObjectInjectionWorld.EntityManager` for every `StateMachine` (action, CurrentCommandIndex, CommandTimer, targetEntity) plus `UtilityActions` and `ThinkRate`. All three freezes were found this way in minutes. Teleporting the player with many `execute_code` calls causes frame hitches. Those exposed the fall-through bug, so a hitch is a real test condition.
- **No unit has toolkit animation data** (MaleCitizen's actor profile was deleted in c07dfd75). Anything that waits on an animation event now completes at once. Do not build H4 timing on clip events.

## Open owner items (do not block H4 on them)
- `DrinkAction` scores on Hunger, not Thirst. This looks like an authoring mistake; the owner hasn't decided.
- The untracked `Assets/InitTestScene…unity` and `Assets/_Recovery/` look like test-run or crash leftovers. Ask before deleting.
- `verify-ai-foundations.md` and `verify-smart-ai.md` are the owner's play checklists.
- **AF-P6** (perception cache, `targetSource`, generic option generation) needs its own spec after H4. It absorbs `CrowdScaleAwareness_System.md` and lines up with H6.

## Deliverable
A short H4 spec (plan-system skill, `Tasks/Plans/`, decisions recorded and not asked unless they're truly product calls), the build in parallel worker waves with one gate per wave, tests proven red, and a CampLoop live check with `execute_code`. Then update `Systems_AI.md`, SmartHumanAI §11, memory, commit and push.
