---
status: built 2026-11-06 (UN-1..UN-9 except the spikes, which need a play look; checklist verify-unit-inspector.md); original status: spec; owner answered the scoping questions 2026-11-06 (see section 0); open questions below carry defaults
replaces: the nav-grid debug overlay as the way to understand navigation (the overlay stays available from the debug panel, default off)
---

# Unit Inspector: watch one unit's navigation and plan

## 0. What the owner asked for (settled 2026-11-06; do not re-ask)
- A debug tool to **watch how a specific unit navigates and what its plan is**, because the whole-grid overlay was not useful.
- Lives **in the game's debug menu** (backquote panel): a toggle "Unit inspector", default OFF like every debug tool.
- **Pick a unit by clicking it** while the toggle is on; it stays watched until another is picked or it is cleared.
- **World overlay:** planned path, targets and ranges, stuck/blocked state, recent trail.
- **Info panel:** chosen action and why (the scored options), needs and motivations, decision history.
- Not asked for (leave out): the behaviour command-sequence view; an Editor window. The data source is built so an Editor window can read it later.

## 1. The data that already exists (verified in code and the vault notes)
| Question | Where it is | Notes |
|---|---|---|
| What is it doing? | `StateMachine` (action, activeBehavior, targetEntity, targetPosition/hasTargetPosition, currentPhase, CurrentCommandIndex, activePriority) | written by `WinnerSelectionSystem`, run by `BehaviorExecutionSystem` |
| What did it consider? | `UtilityActions` buffer: targetEntity, targetPosition, actionType, priority, totalUtility, actionDefIndex, isPlayerOrdered (+ `advertisedDelta` on ActionOption for needs-based scoring) | the buffer is rewritten every think tick; a snapshot per frame is enough |
| Why those scores? | `Motivation` buffer (needs/drives), Bravery, personality context, `UtilityBrain` (enableable) | read-only |
| Where is it walking? | `PathfindingAgent` (mode, targetPosition, stoppingDistance, needsRepath), `PathRequest` (enableable), `AStarFollower` + `AStarPathWaypoint` buffer (corner points, current index), `FlowFieldFollower`, `DStarLiteFollower`, `StuckDetector`, `MovementStuck` (enableable) | package `com.dotsmovementtoolkit` |
| Abstract (off-screen) state | `AbstractSimulation` (enableable), `AbstractRoute`, `AbstractRestAnchor` | an abstract unit is shown as "abstract" with its route end, not a live path |

**Gap:** nothing records the sequence of decisions. The inspector adds a ring buffer for the watched unit only (section 3).

## 2. Architecture in one page
- **One watched unit:** a singleton `DebugWatchedUnit { Entity unit }` (created by the bridge). Nothing runs for unwatched units; every cost below is on one entity.
- **Pick:** `UnitInspectorPicker` (MonoBehaviour, hybrid bridge). While the "Unit inspector" toggle is on and the pointer is not over the debug UI, a left-click casts a camera ray into the Unity Physics `CollisionWorld` on the Units layer (layer 6; same layer the caravan crash system uses). If nothing is hit it falls back to the nearest unit within 1.5 m of the ground point under the mouse (same ground-point approach as `DebugZombifyMenu`). Clicking empty ground clears the pick.
- **Snapshot:** `UnitInspectorSnapshot` (plain managed struct/class) filled once per frame by `UnitInspectorReader` from the watched entity's components. Panel and overlay read only the snapshot, never the ECS directly, so an Editor window can reuse it.
- **Decision history:** `DecisionHistoryEntry` buffer added to the watched unit when picked (and removed when cleared): 20 entries, ring. `DebugDecisionHistorySystem` (in `StateMachineSystemGroup`, after `WinnerSelectionSystem`'s transition apply) writes an entry whenever `StateMachine.action` or `activeBehavior` changes, with world time, the old and new action, priority, the winning utility, whether it was player-ordered, and the `advertisedDelta` winner. Interrupt cause is best-effort (see UN-Q2).
- **Trail:** the bridge samples the watched unit's position every 0.25 s into a ring of 40 (10 s).
- **Overlay:** `UnitInspectorOverlay` draws with `GL` lines in `RenderPipelineManager.endCameraRendering` (both Game and Scene cameras): the path polyline (current waypoint highlighted), a marker on the target entity or position, rings for sight, attack and flee distances, the trail (fading), and colour by state. Colour rules: walking = green, waiting for a path request = yellow, stuck (`MovementStuck` or stuck accumulator past the threshold) = red, blocked by a body = orange, abstract = blue line to the route end.
- **Panel:** `UnitInspectorPanel` (OnGUI, same style as the other debug windows, placed under the debug panel on the right). Sections: header (name, unit type, faction, health, bravery), **Plan** (action, behaviour, phase, target, mode and path state, waypoint i of n, distance to goal), **Why** (the scored options table with the winner marked, utility, priority, advertisedDelta), **Needs** (the motivation values and personality context), **History** (the ring, newest first, with seconds-ago).
- **Toggle:** registered as "Unit inspector" through `DebugMenuVisibility.RegisterToggle`; panel and overlay draw only while on, and the pick is cleared when it is turned off.

## 3. Decisions (UN-D1 onward; defaults apply unless the owner says otherwise)
- **UN-D1 Reads only, no writes** to any gameplay component. The only game-side write is the history buffer on the watched entity.
- **UN-D2 Cost when off is zero:** no system updates, no buffer on any unit, no GL callback registered.
- **UN-D3 The panel never blocks the game:** clicking the panel does not pick; clicking the world picks and is also passed on (a debugging click may also issue a normal game order; see UN-Q1).
- **UN-D4 Live units only for the path:** an abstract unit shows state and its route end, not a path.
- **UN-D5 One snapshot struct is the contract** between the data and both views (panel, overlay, a future Editor window).
- **UN-D6 Development builds and the Editor only** (`#if UNITY_EDITOR || DEVELOPMENT_BUILD`), like the debug panel.

## 4. Open questions
- **UN-Q1 Does the picking click also reach the game?** Default: yes (no input suppression); if it causes accidental orders, switch to Alt+click or suppress while the toggle is on.
- **UN-Q2 Interrupt cause in the history:** record only "winner changed" now, or add a cause enum written by `BehaviorInterruptSystem` (interrupt kind, flee, stuck, player order, finished)? Default: add the small cause enum, set only where the interrupt system already knows it.
- **UN-Q3 Follow the unit with the camera?** Default: no.
- **UN-Q4 Show the nav grid cost near the unit** (a small window of cells) as part of the overlay? Default: no, it is the thing that was not useful.

## 5. Spikes (orchestrator, Unity MCP)
| ID | Question | Answer by |
|---|---|---|
| UN-S1 | Does `GL` line drawing in `endCameraRendering` show in both the Game and Scene view under URP 17.5 with the 2.5D camera, with correct depth against the ground and units? (`Hidden/Internal-Colored` material, ZTest off vs LEqual) | a one-line test in the debug panel |
| UN-S2 | Does a click raycast against the Units layer hit a unit through its billboard visuals (are units' physics capsules where the sprite is)? | click three units in Play |
| UN-S3 | Cost of reading the `UtilityActions` buffer and the path buffer on the main thread every frame (expect negligible); confirm no sync-point stall | `Profiler` marker around the reader |

## 6. Phases and tasks
All names final. Verify real signatures in `Library/PackageCache` and the component files before calling. One worker per row; workers never use the Unity MCP.

### U1. Data
| ID | Files | Brief | Gate | Par |
|---|---|---|---|---|
| UN-1 | `Components/Debug/DebugWatchedUnit.cs`, `Components/Debug/DecisionHistory.cs` | `DebugWatchedUnit : IComponentData { Entity unit; }`; `DecisionHistoryEntry : IBufferElementData { double worldSeconds; ActionType fromAction; ActionType toAction; BehaviorType toBehavior; byte priority; float winningUtility; bool wasPlayerOrdered; byte cause; }`, `[InternalBufferCapacity(0)]`; `enum DecisionCause : byte { WinnerChanged, Interrupted, Fled, Stuck, PlayerOrder, Finished }`. Use the real enum names from `UtilityAiComponents.cs` / the behaviour enums | `StitchPunk.Components` | A |
| UN-2 | `Utils/DecisionHistoryRing.cs` + `Tests/DecisionHistoryRingTests.cs` | Pure static: `Append(ref DynamicBuffer<DecisionHistoryEntry>, entry, capacity 20)` drops the oldest at capacity. Test: 25 appends keep the last 20 in order. Prove it can fail by changing the capacity | `StitchPunk.Utils` + fixture | B |

### U2. Systems
| ID | Files | Brief | Gate | Par |
|---|---|---|---|---|
| UN-3 | `Systems/StateMachineSystemGroup/DebugDecisionHistorySystem.cs` | `[UpdateInGroup(typeof(StateMachineSystemGroup))]`, after `WinnerSelectionSystem`. Runs only when `DebugWatchedUnit` exists and its unit has the history buffer; diffs `StateMachine.action`/`activeBehavior` against the previous value kept in a managed-free field on the system (one `ActionType`, one `BehaviorType`), appends via UN-2, `cause` per UN-Q2 default. No `RequireForUpdate<GameSceneTag>` (group gates) | `StitchPunk.Systems` | - |

### U3. Bridge (picker, snapshot, trail)
| ID | Files | Brief | Gate | Par |
|---|---|---|---|---|
| UN-4 | `MonoBehaviours/Debug/UnitInspectorSnapshot.cs` | The plain snapshot class from section 2 (all fields the panel and overlay need: names, health, bravery, action, behaviour, phase, target entity/position, option rows, motivation rows, nav state enum, path points list, current waypoint index, stuck flag, abstract flag, trail points, history rows, ranges). No logic | `StitchPunk.MonoBehaviours` | A |
| UN-5 | `MonoBehaviours/Debug/UnitInspectorReader.cs` | `Fill(EntityManager, Entity unit, UnitInspectorSnapshot snapshot, float time)` reading the components in section 1 (guard every `HasComponent`; the unit may be abstract, dead, or destroyed). Keeps the trail ring | `StitchPunk.MonoBehaviours` | B |
| UN-6 | `MonoBehaviours/Debug/UnitInspectorPicker.cs` | The click pick per section 2, sets/clears `DebugWatchedUnit`, adds/removes the `DecisionHistoryEntry` buffer on the previous and new unit (structural change on the main thread) | `StitchPunk.MonoBehaviours` | C |

### U4. Views
| ID | Files | Brief | Gate | Par |
|---|---|---|---|---|
| UN-7 | `MonoBehaviours/Debug/UnitInspectorPanel.cs` | OnGUI window per section 2, reads only the snapshot; registers the "Unit inspector" toggle; self-installs like `DebugMenuPanel`; hosts the per-frame reader call | `StitchPunk.MonoBehaviours` | D |
| UN-8 | `MonoBehaviours/Debug/UnitInspectorOverlay.cs` | GL overlay per section 2 and the colour rules; depth setting from UN-S1 | `StitchPunk.MonoBehaviours` | D |

### U5. Verification and notes
| ID | Files | Brief | Gate | Par |
|---|---|---|---|---|
| UN-9 | `_Vault/Tasks/Verification/verify-unit-inspector.md`, `_Vault/Memories/Code/Systems_AI.md` (a short section) | Owner checklist: pick a bandit mid-chase and watch the path and the options table; pick a citizen heading home; pick an abstract unit; watch a unit get stuck against a wall (red); read the history while a player order interrupts; confirm the toggle off costs nothing | none | - |

Checkpoint U (owner play pass): pick a bandit during a fight and a citizen on a walk; the path, target and rings match what the unit does, the options table names the winner, the history shows the last changes.

## 7. Order of work
Spikes UN-S1 and UN-S2 first (they decide the overlay and the pick). UN-1, UN-2 in parallel; UN-3 after them; UN-4 and UN-5 in parallel with UN-3; UN-6 after UN-1; UN-7 and UN-8 last. About 9 worker tasks plus three spikes.

## 8. Known gaps by design (v1)
No behaviour command-sequence view, no camera follow, no multi-unit watch list, no Editor window, no replay or recording of history beyond the last 20 changes, and abstract units show no path.
