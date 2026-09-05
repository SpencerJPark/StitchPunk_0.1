# Cutscene Interactions — Design Spec (G2)

> **Status:** ✅ spec ready, not built. Written 2026-09-04.
> **Roadmap:** [`Cutscene_Roadmap.md`](Cutscene_Roadmap.md) — read its §4 protocol first.
> **Depends on:** G1 (`CutsceneSystemGroup`, `CutsceneActor`, `ActiveCutscene`), toolkit A63 (`CutsceneDetachSignal`, hide), A64 (`CutsceneMoveToMark`), A65 (holding events, `ICutsceneEventInspectorProvider`, `CutsceneFacing`). **Parallel-safe with:** A66.
> **Executor:** one Sonnet session.

---

**Skills Needed:**
- `dots-system-scaffold` — the four consumer systems in §3
- `dots-test` — §5 fixtures

---

## 1. Purpose & scope

The toolkit raises four host-facing contracts during a cutscene. This spec builds Stitch Punk's consumer for each, plus the editor seam that makes a dialogue cue authorable by name, and refines the player-control rule for rendezvous holds (owner call: **the player walks to their own mark; NPCs pathfind**).

| Toolkit contract | Game consumer (this spec) |
|---|---|
| `CutsceneMoveToMark` enabled on a bound entity | `CutsceneMoveToMarkSystem` → `MovementAPI.BeginPathRequest` for non-player units; nothing for the `Player`; `HaltPathing` when the toolkit disables it |
| Holding event `AnimEvents.Dialogue` (intParam = sequence id, floatParam = speaker slot index) | `CutsceneDialogueCueSystem` → `ActiveDialogue`; releases hold `"Dialogue"` when the dialogue ends |
| `CutsceneFacing` enabled on a bound actor | `UnitFacingSystem` branch: angle → `UnitFacing` (which already fans out to `PartFacing`) |
| `CutsceneDetachSignal` enabled on a detached entity | `CutsceneDetachSystem` → `ThrownItemRequest` for items; units are placed only |

## 2. Read first

`Systems_AI.md`, `Systems_Movement.md`, `Systems_Animation.md` (facing section), `Contracts.md`. `Packages/com.dotsmovementtoolkit/Runtime/MovementAPI.cs`, `Components/PathfindingComponents.cs`. `Components/AI/Dialogue.cs`, `MonoBehaviours/DialogueUIManager.cs` (where `ActiveDialogue` is disabled — lines ~214 and ~322), `Systems/DialogueSystemGroup/DialogueStartSystem.cs`. `UnitFacingSystem.cs`. `Components/Items/ItemComponents.cs` (`ThrownItemRequest`), `Systems/ItemSystemGroup/ThrownItemSystemGroup/ThrownItemSystem.cs`. `Assets/Generated/DotsAnimationToolkit/AnimEvents.cs` (generated constants — add the registry entry through Project Settings → DOTS Animation Toolkit → Event Names, never by editing the generated file). `Editor/DirectionSetContext/UnitDirectionSetContextProvider.cs` (the registration pattern for editor seams). The G1 systems.

## 3. Systems

All in `Systems/CutsceneSystemGroup/`, `[UpdateInGroup(typeof(CutsceneSystemGroup))]`, ordered after `CutsceneStartSystem`.

### 3.1 `CutsceneMoveToMarkSystem`

Burst job over `CutsceneMoveToMark` (enabled) + `PathRequest` + `Movement`, `WithNone<Player>`: when the mark is newly enabled (track with a game-side enableable `CutsceneMarkIssued` on units, baked disabled by `UnitBakingUtil`, reset by `SpawnStateInitSystem`) → `MovementAPI.BeginPathRequest(ref pathRequest, pathRequestEnabled, mark.position, stoppingDistance: mark.toleranceMeters * 0.5f)` and enable `CutsceneMarkIssued`. A second job over `CutsceneMarkIssued` (enabled) whose `CutsceneMoveToMark` is now **disabled** (arrived or timed out) → `HaltPathing`, `Movement.targetPosition = position`, disable `CutsceneMarkIssued`. The `Player` entity is skipped by the query — it walks on its own input; the rendezvous hold waits for it like anyone else.

### 3.2 `CutscenePlayerControlSystem` (G1) — the rendezvous refinement

`CutsceneActiveTag` is enabled while a cutscene is active **except** when the request is paused on a hold and the `Player` entity has an enabled `CutsceneMoveToMark`. Then input is live so the player can walk. The moment the player's mark resolves (or the hold releases for another reason) the lock returns. Read the request's `CutscenePlaybackState.isPausedOnHold`.

### 3.3 `CutsceneDialogueCueSystem`

Main thread (it reads the request entity's `AnimEventOutput` buffer and writes the dialogue singleton). Each frame with `ActiveCutscene` enabled and the request's `AnimEventsPending` enabled: for each event with `eventKey == AnimEvents.Dialogue`: `speakerEntity` = the bound entity of slot index `(int)floatParam` (or `Entity.Null` for −1), set `ActiveDialogue { sequenceId = intParam, speakerEntity }` and enable it — the same write `NarrativeEventManager.ExecuteDialogueTriggerAsync` does. Track `dialogueCueOpen = true`. When `dialogueCueOpen` and `ActiveDialogue` is disabled (the UI manager ended it) and `CutscenePlaybackApi.TryGetCurrentHoldId` returns `"Dialogue"` → write `CutsceneHoldRelease { holdId = "Dialogue" }`, enable, clear the flag. A dialogue event authored without `holdUntilReleased` still starts a dialogue; it just does not wait.

One-frame latency note: `AnimEventOutput` on the request entity is written by `CutsceneTimelineSystem` (toolkit group, later in the frame), so this system reads last frame's events — the documented contract in `Contracts.md`.

### 3.4 `UnitFacingSystem` — the `CutsceneFacing` branch

Replace G1's skip: if `CutsceneFacing` is present and enabled, derive `movementXY` from the angle instead of `Movement.targetPosition` — `angle → float2(cos, sin)` in the toolkit's facing convention (read `FacingResolver.FromMovement`'s expected space and `WorldToFacingSpace` in this file; write a comment naming the convention). Everything downstream (`FacingResolver.FromMovement`, `PartFacing` fan-out, view offsets) is unchanged. Sort key: this is one `if` in `UnitFacingJob.Execute` plus a `[ReadOnly] ComponentLookup<CutsceneFacing>` — `using Unity.Collections;`.

### 3.5 `CutsceneDetachSystem`

Main thread or Burst with a lookup: for every entity with `CutsceneDetachSignal` enabled: if it has `ThrownItemRequest` → `{ velocity = signal.worldImpulse, thrower = signal.previousHost, throwOrigin = LocalTransform.Position }` and enable (the existing `PlayerUnequipSystem` shape); else nothing — a unit detached from a cart is simply placed. Disable the signal.

### 3.6 Editor seam — `CutsceneDialogueEventInspectorProvider`

`Assets/_Scripts/Editor/CutsceneContext/CutsceneDialogueEventInspectorProvider.cs` implementing the toolkit's `ICutsceneEventInspectorProvider`, registered from `[InitializeOnLoadMethod]` (copy `UnitDirectionSetContextProvider`'s registration). For `AnimEvents.Dialogue`: an `ObjectField<DialogueSequenceSO>` that writes `sequence.sequenceId` into `intParam`, and a speaker dropdown over the cutscene's slot names writing the index into `floatParam` (−1 = none). Requires the `Dialogue` entry to exist in the event registry (§2).

## 4. Decisions

- **DECIDED — the player is never pathed.** The query excludes `Player`; the toolkit's arrival test is what waits for them.
- **DECIDED — dialogue payload:** `intParam` = sequence id, `floatParam` = speaker slot index. Hidden behind the provider UI; documented in `Contracts.md`.
- **DECIDED — detach physics:** items reuse `ThrownItemRequest`; units are placed, not launched (a launched unit is a ragdoll/death path — out of scope, revisit if a cutscene ever needs it).
- **DECIDED — facing:** the game maps angle → `UnitFacing`; the toolkit never writes `PartFacing` (toolkit A65-D2).

## 5. Build phases

- [ ] **Phase 1 — marks (§3.1, 3.2) + `CutsceneMarkIssued` baking.** Test (PlayMode): `MoveToMark_IssuesAPathRequestForUnitsButNotThePlayer` (two entities, one with `Player`; enable marks; update; assert `PathRequest` enabled only on the unit with the mark's position) and `MarkResolved_HaltsPathing`.
- [ ] **Phase 2 — dialogue cue (§3.3, 3.6) + registry entry.** Test (PlayMode): `DialogueCue_StartsActiveDialogue_AndReleasesTheHoldWhenItEnds` (hand-built request entity with an `AnimEventOutput` Dialogue event and a playback state paused on hold `"Dialogue"`; update; assert `ActiveDialogue` enabled; disable it; update; assert `CutsceneHoldRelease` enabled with the id).
- [ ] **Phase 3 — facing (§3.4).** Test (PlayMode, extend the existing facing fixture if one exists — grep `UnitFacingSystem` in `Tests/`): `CutsceneFacing_OverridesMovementDerivedFacing`.
- [ ] **Phase 4 — detach (§3.5).** Test (PlayMode): `DetachSignal_BecomesAThrownItemRequestOnItems`.
- [ ] **Phase 5 — full suites once**, `Contracts.md` rows, `Systems_AI.md`/`Systems_Animation.md` one line each.
- [ ] **⏸ Owner checkpoint.** In `DOTSTestScene`: a cutscene with marks for the player and one minion beside a crate, a rendezvous hold, then a Dialogue holding event, then a clip. Press F9: the minion pathfinds to its disc; you walk to yours; the moment both arrive the hold releases and WASD stops working; dialogue opens; closing it continues the cutscene; the minion faces the way its root keys carry it.

## 6. Notes / build log
