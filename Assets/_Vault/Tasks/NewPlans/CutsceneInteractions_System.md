# Cutscene Interactions — Design Spec (G2)

> **Status:** ✅ Phases 1–5 built and gated 2026-09-06; stopped at the ⏸ owner checkpoint. Written 2026-09-04.
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

- [x] **Phase 1 — marks (§3.1, 3.2) + `CutsceneMarkIssued` baking.** Test (PlayMode): `MoveToMark_IssuesAPathRequestForUnitsButNotThePlayer` (two entities, one with `Player`; enable marks; update; assert `PathRequest` enabled only on the unit with the mark's position) and `MarkResolved_HaltsPathing`.
- [x] **Phase 2 — dialogue cue (§3.3, 3.6) + registry entry.** Test (PlayMode): `DialogueCue_StartsActiveDialogue_AndReleasesTheHoldWhenItEnds` (hand-built request entity with an `AnimEventOutput` Dialogue event and a playback state paused on hold `"Dialogue"`; update; assert `ActiveDialogue` enabled; disable it; update; assert `CutsceneHoldRelease` enabled with the id).
- [x] **Phase 3 — facing (§3.4).** Test (PlayMode, extend the existing facing fixture if one exists — grep `UnitFacingSystem` in `Tests/`): `CutsceneFacing_OverridesMovementDerivedFacing`.
- [x] **Phase 4 — detach (§3.5).** Test (PlayMode): `DetachSignal_BecomesAThrownItemRequestOnItems`.
- [x] **Phase 5 — full suites once**, `Contracts.md` rows, `Systems_AI.md`/`Systems_Animation.md` one line each.
- [x] **⏸ Owner checkpoint — built and machine-verified 2026-09-06; awaiting the owner's eyes.** In `DOTSTestScene`: a cutscene with marks for the player and one minion beside a crate, a rendezvous hold, then a Dialogue holding event, then a clip. Press F9: the minion pathfinds to its disc; you walk to yours; the moment both arrive the hold releases and WASD stops working; dialogue opens; closing it continues the cutscene; the minion faces the way its root keys carry it.

## 6. Notes / build log

### Phase 1 — marks (2026-09-06)

**A `[WithDisabled]` that matched nothing, with no error.** `ClearResolvedMarkJob` was written the
obvious way — `[WithDisabled(typeof(CutsceneMoveToMark))]` on a job that also takes
`EnabledRefRW<CutsceneMarkIssued>` — and it silently matched zero entities every frame while a
hand-built `EntityQueryBuilder` with the *identical* five constraints matched the same entity
(measured live through `execute_code`: `handQueryCount=1`, job body never reached). Rewriting the
switch-off as `[WithPresent(typeof(CutsceneMoveToMark))]` plus an explicit `EnabledRefRO`/`ValueRO`
check — the shape `Gotchas.md` already prescribes for reacting to a component being switched *off* —
made it match. Recorded in `Gotchas.md`; the rule to carry forward is that `[WithPresent]` + a body
check is the only reliable "react to a disable", even when `[WithDisabled]` looks like it says
exactly that.

**Two spec readings resolved rather than silently picked.**

1. §3.1 says the resolve pass writes `Movement.targetPosition = position`. Ambiguous between the
   mark's position and the unit's own. It writes the **unit's own** `LocalTransform.Position`: an
   arrival is already inside tolerance, a timeout has already placed the unit *on* the mark, and it
   is the same "stop where you stand" idiom `CutsceneStartSystem` uses when it gates an actor.
   Writing the mark instead would keep `UnitMoverSystem` walking a unit that has arrived.
2. §3.2's rendezvous exception **overrides a `blockPlayerInput` narrative event**, rather than
   deferring to the existing "only disable when no narrative event is active" guard. Deferring would
   have made the exception dead code on the primary path — a cutscene started by `PlayCutsceneAction`
   always has `ActiveNarrativeEvent` enabled — and an author who gave the player a mark has asked for
   them to walk to it. The lock returns the frame the mark resolves.

`ClearResolvedMarkJob` also takes `Movement` as `[WithPresent]`, not enabled-only: a unit that dies
mid-walk has `Movement` disabled by `DeathSystem`, and an enabled-only query would strand
`CutsceneMarkIssued` on it forever, swallowing its next order after a pool reclaim.

### Phase 2 — dialogue cue (2026-09-06)

`Assets/Generated/DotsAnimationToolkit/AnimEvents.cs` was regenerated through
`ConstantsGenerator.BuildVocabularyConstantsSource`/`WriteGeneratedFile` — the exact path the
Project Settings page uses — rather than hand-edited. `AnimEvents.Dialogue` is `0x13` (19). Two
asmdefs had to gain a `StitchPunk.Generated` reference to see it: `StitchPunk.Editor` (the provider)
and `StitchPunk.Tests.PlayMode` (the fixture).

**A detached `VisualElement` dispatches no `ChangeEvent`, so an editor seam probed off-panel looks
inert.** The first live probe of the provider reported that neither the sequence field nor the
speaker dropdown wrote anything, which read exactly like a broken binding; the real cause is that
`SendEvent` needs a panel's dispatcher and the probe built its container standing alone. Re-run with
the container parented into a live `EditorWindow.rootVisualElement`, every write lands: speaker
`Player -> 0`, `Bertha -> 1`, `(no speaker) -> -1`, and picking `Dialogue_New` stored its
`sequenceId` in `intParam` with a rebuild reading both back. Recorded in `Gotchas.md` — any future
`execute_code` probe of a UI Toolkit field must parent it first or it proves nothing.

Slot labels in the speaker dropdown are made unique (`name (slot N)` on a collision) because the
chosen label, not `DropdownField.index`, is what resolves back to the stored index.

### Phase 3 — facing (2026-09-06)

`UnitFacingJob` no longer excludes cutscene actors — `[WithDisabled(typeof(CutsceneActor))]` became
`[WithPresent]` plus an `EnabledRefRO<CutsceneActor>` check, so a bound actor with an enabled
`CutsceneFacing` takes the cutscene's angle and one without keeps the facing it had. The precedence
(cutscene, then attack aim, then movement delta) is named in one public static,
`UnitFacingJob.ResolveMovementXY`, the same way `WorldToFacingSpace` was already public for
`FacingSpaceTests` — which is what let the branch be pinned without hand-building a `UnitLibraryBlob`.

The two new EditMode cases live in the existing `FacingSpaceTests` and pin the convention trap
directly: `CutsceneAngleToFacingSpace` is `(cos, sin)` from +X toward +Z, so 0 is east and 90 north.
Reverting it to the `LocalTransform` Y-euler convention `(sin, cos)` fails both cases, which is the
reflection about 45 degrees that cost the toolkit an amendment.

**Not covered by a fixture, deliberately:** that the branch fires inside a real `UnitFacingSystem`
update. Doing so needs baked `UnitDataLibrary`/`PartLibrary` blob singletons, and the fixture would
mostly assert `FacingResolver`'s own quantization, which the toolkit's suite already pins. The
observable — a minion turning as its root keys carry it — is a checkpoint item for the owner's eyes.

### Phase 4 — detach (2026-09-06)

`CutsceneDetachSystem` is one `.Schedule()`d job over the enabled signal, writing `ThrownItemRequest`
through a `ComponentLookup`; a unit's signal is consumed and nothing else happens to it. Three things
this phase paid for:

1. **An `in` parameter alongside `EnabledRefRW` of the same type is a run-time aliasing throw.** The
   generator emits both an RO and an RW `ComponentTypeHandle` for the type and the job safety system
   rejects the pair — `InvalidOperationException: ... two containers may not be the same (aliasing)`,
   no compile error. Taking the signal by `ref` makes it one handle. (The marks jobs already had
   matching access modes by luck: `ref` + `EnabledRefRW`, and `in` + `EnabledRefRO`.)
2. **A newly added `[BurstCompile]` job can run as somebody else's compiled code for a run or two.**
   The first PlayMode run threw `ObjectDisposedException` naming `ComponentTypeHandle<OnAttackPlayerInput>`
   and an `execute_code` probe threw an NRE whose Burst stack pointed at `CutsceneAnimEventSoundJob`
   — neither type appears anywhere in this system, and the probe's world contained no `CutscenePlay`
   entity at all. Dropping `[BurstCompile]` made it run correctly; putting it back, after another
   compile cycle, also ran correctly. Same family as the Burst JIT cache note already in the vault:
   **a bogus failure naming a job you did not touch is a reason to recompile and re-run before
   debugging**, and a green run taken too soon after an edit may be running the previous binary.
3. **A revert that only removed `SetComponentEnabled` did not fail the test** — the component still
   read enabled after the lookup's indexer write. Removing the whole throw branch does fail it, which
   is the revert kept on record. The explicit `SetComponentEnabled` call stays: what the job means is
   "enable the request", and that must not depend on a side effect of an indexer assignment.

### Phase 5 — suites and docs (2026-09-06)

Full suites, all four, run once at this point:

| Suite | Discovered | Passed |
|---|---|---|
| `DotsAnimationToolkit.Tests.EditMode` | 718 | 717 — the one pre-existing `Conformance_A_AsmdefReferenceLists_MatchSection13Exactly` asmdef-drift failure, unrelated to G2 |
| `DotsAnimationToolkit.Tests.PlayMode` | 261 | 261 |
| `StitchPunk.Tests` (EditMode) | 59 | 59 — baseline was 57; +2 facing cases. `SystemPlacementConformanceTests` passes, so the three new system files sit in the folder their group is named after |
| `StitchPunk.Tests.PlayMode` | 7 | 7 — baseline was 3; +2 marks, +1 dialogue cue, +1 detach. **First time this assembly's own count is recorded** |

Docs updated: `Contracts.md` gained four rows (`CutsceneMoveToMark`, `CutsceneFacing`,
`CutsceneDetachSignal`, `CutsceneHoldRelease`) plus the `AnimEvents.Dialogue` consumer on the
`AnimEventOutput` row and `CutsceneDetachSystem` as a second `ThrownItemRequest` producer;
`Systems.md` gained the three new systems and the rendezvous exception; `Systems_AI.md` and
`Systems_Animation.md` one entry each; `Gotchas.md` four traps.

### The checkpoint, built 2026-09-06 in DOTSTestScene

**What was authored.** `G2CheckpointCutscene.asset` (key `936148452665553662`): two slots — *Player*
(a Prop slot with a mark and no root keys, so nothing stages the player) and *Minion* (an Actor slot
on `NewRig`/`NewClipSet`). Marks at `(2, 0, 2.3)` and `(-0.9, 0, 2.3)`, beside the crates; a
rendezvous hold `"Rendezvous"` at 0.1 s with `autoReleaseWhenMarksReached`; a holding `Dialogue`
event at 0.2 s carrying sequence id 1 and speaker slot index 1; then a looping `Walk` block and root
keys that carry the minion east to `x = +4.5` and back west to `x = −4.5`, so the facing has to turn.
It bakes to three segments, verified through `CutsceneBlobBuilder` before any scene was touched.

`Dialogue_G2Rendezvous.asset` (sequence id 1) is a Start → Line → Line → End graph. In
`DOTSTestScene`'s subscene: a `NarrativeEventAuthoring` (the trap G1 found — this scene had none), a
`DialogueManagerAuthoring` (nor one of these), and a `Cutscene Stage - G2 Checkpoint` binding the
Player slot to `PlayerUnit` and the Minion slot to `TestRotter`. In `TestArea`: a `DialoguePanel`
under the existing Canvas with speaker/subtitle labels, a `DialogueUIManager` wired to them, and
`Managers/CutsceneDebug` carrying the F9 `CutsceneDebugTrigger`.

**Three things the checkpoint found, all content or host wiring rather than G2 code.**

1. **`DialogueUIManager` resolved its ECS singletons in `Start()`**, which in a SubScene project runs
   before the baked entities stream in. It logged `"No DialogueManagerAuthoring entity found"` once
   and then sat inert for the whole session — dialogue could never have displayed in a subscene
   scene. Now resolved lazily from `Update` until it succeeds, with the complaint deferred 5 s and
   said once. `NarrativeEventManager.ResolveEcsReferences` has the same `Start()` shape and is the
   next thing to hit this; left alone because nothing this checkpoint exercises goes through it.
2. **`CutsceneDebugTrigger` defaulted to the `Override` layer, and `NewRig` declares one layer.**
   Every clip block was silently dropped — the actor slid its root lane with no clip, which is
   exactly the symptom G1's checkpoint reported as "the minions slide instead of walking". The
   trigger in `TestArea` is set to `Base`; measured after the change, the bound actor's layer 0
   carries `clip=17929205651740358465` (Walk) at the cutscene's own `speed`.
3. **The `CutsceneFacing` → `UnitFacing` bridge has no content to land on, project-wide.** `UnitFacing`
   and the `BodyPart` buffer are added by `CharacterRigAuthoring`, and **no prefab in the project has
   one** (scanned all 42; only `MaleCitizen.prefab` has an `ActorAuthoring`, and it has no
   `CharacterRigAuthoring`). Measured in a live world: `unitFacingEntities=0`, `bodyPartBuffers=0`,
   while `partFacingEntities=6` — the toolkit's own mirror points exist and nothing game-side can
   write them. So `UnitFacingJob` never runs on a cutscene actor, and the turn cannot be seen yet.
   The *input* half is confirmed live: the toolkit wrote `CutsceneFacing` on the bound minion and it
   swung `186.6° → 0°` as the root keys reversed, which is the east/west convention the branch reads.
   Closing this is content work of G0's kind (a `CharacterRigAuthoring` + body-part tree on
   `MaleCitizen`, bridging `BodyPart.entity` to the toolkit's rig parts), not G2 code.

**Machine-verified, live, in `TestArea` + `DOTSTestScene`** (numbers from `execute_code` against the
running world, not from reading the code):

- The stage bakes with both bindings resolved to real entities.
- On the request, the minion is pathed to **exactly its mark** (`pathTarget=(-0.9, 0, 2.3)`,
  `markIssued=True`), and the player is **not** pathed (no `PathRequest` at all, and the query
  excludes them anyway).
- While the player's mark is outstanding the clock is paused on `'Rendezvous'` and
  `CutsceneActiveTag` is **False** — the player has input. The frame their mark resolves it is
  **True** again.
- The minion arrives by distance (`markEnabled=False` at 0.78 s, far inside the 20 s timeout), its
  flag clears and its `PathRequest.requestedMode` becomes `Stop`.
- The `Dialogue` cue opens the real panel: `activeSequenceId=1`, `panelActive=True`,
  speaker `'Citizen'`, subtitle text as authored, and `TryGetCurrentHoldId` reports `'Dialogue'`.
- Two Interact presses walk the graph to its End node; the hold is released with
  `holdId='Dialogue'`, the clock resumes into segment 2, and the minion walks its lane
  (`x: −0.9 → −0.28 …`) with `CutsceneFacing` reading `0` (east).
- A mark that is never reached times out and places the actor — seen for real when the first bound
  actor was killed by the sandbox mid-walk, and the scene continued rather than softlocking.

**Not machine-verifiable, and the reason the owner has to look:** whether any of it reads correctly
on screen — the panel's legibility, the minion's walk cycle, and above all whether it *turns*, which
per finding 3 it currently cannot.
