# Cutscene Roadmap — from "gated but undemoed" to the ultimate animation tool

> **Status:** ✅ specs written 2026-09-04, nothing built yet. Owner product calls recorded in §2.
> **Supersedes:** `Tasks/Plans/Cutscene_System.md` (the pre-toolkit CutsceneSO design — deleted, git keeps it).
> **Executor:** each spec below is sized for one fresh Claude Sonnet session with no prior context. §4 is the protocol that session follows. Read it before opening any spec.

---

## 1. Where the feature stands (verified 2026-09-04)

Phase G + A58–A60 shipped a complete **package-side** cutscene feature in `Packages/com.dotsanimationtoolkit`: `CutsceneAsset` → Cutscene Editor tab (cast panel, in-tab viewport, animated clip preview, transport with real holds) → `CutsceneBlobBuilder` → `CutsceneTimelineSystem` + `CutscenePartOverrideSystem`. EditMode 712 / PlayMode 243 green. **No human has watched one play.**

**Nothing in the game can play one.** Zero references to `CutscenePlaybackApi`, `CutsceneCameraPose`, `CutsceneActorBinding` or `CutsceneBlobBuilder` exist under `Assets/_Scripts/`. The editor's slot→GameObject bindings are editor-only `GlobalObjectId` strings that never reach an entity. `CutsceneActiveTag` exists but no input system reads it.

**Runtime bugs found by review** (each has a spec task below):

| Bug | Where | Spec |
|---|---|---|
| A key at a hold's exact time lands in the *next* segment, so motion into a hold is lost at runtime; the editor (flat-list sampler) interpolates correctly, so preview ≠ playback | `CutsceneBlobBuilder.AssignToSegment` half-open rule + per-segment `CutsceneBlobSampler` | A62-T1 |
| First clip block after a hold is always a hard cut (blend derived from "previous block in the same segment") | `CutsceneTimelineSystem.ProcessClipBlocks` | A62-T3 |
| A slot with zero root keys is written to position (0,0,0) every frame — both editor and runtime | `CutsceneBlobSampler.SampleTransform` / `CutscenePoseSampler.Sample` return zero for an empty list and callers write it unconditionally | A62-T2 |
| ~~`CutsceneControl.speed` scales the cutscene clock only~~ **FIXED — verified 2026-09-05 (G0-T6): a cutscene fired at `speed = 0.05` reads back `PlaybackLayer.speed == 0.050` on both actors. Do not re-fix.** | `ProcessClipBlocks` | A62-T4 |
| Clip blocks whose start is the segment's t=0 are issued one frame late after a hold release | hold branch returns before `ProcessClipBlocks` | A62-T5 |
| Facing has no runtime application at all (recorded gap) | — | A65-T2 |

## 2. Owner product calls (2026-09-04)

1. **Move-to marks.** Actors walk to authored spots before the cutscene continues ("everyone hops in the car, we don't leave without them"). **NPCs pathfind through the game's movement; the player keeps control and walks there by hand.** The clock waits at a rendezvous hold until every mark is reached, with an optional timeout that teleports stragglers so nothing softlocks.
2. **Interactions the first specs must deliver:** carry and throw (prop → actor socket, detach with impulse); ride or board (actors attach to a prop, hidden or seated, and the prop's root keys carry them); hand-over between actors; **dialogue as a first-class lane** (a cue that starts a sequence and holds the clock until it ends).
3. **Editor polish that must land before "finished":** Auto Key; multi-select, box-select, copy/paste; a curve editor for cutscene keys; viewport click-select and an in-viewport gizmo (A59-T2/T3) plus the frozen header column.
4. **Acceptance cutscene: "Rendezvous and Depart."** Player + two minions walk to marks beside a cart prop, board it (hidden), a dialogue beat holds, the cart drives off on root keys with a camera move, it stops, everyone detaches placed on the ground, gameplay resumes where they stand. It exercises every system at once and is the final test of every spec.

## 3. The specs, in order

Toolkit amendments live in `Docs/AnimationToolkit/` (the package's own doc system). Game plans live here in `Tasks/NewPlans/`.

| # | Spec | Delivers | Depends on | May run in parallel with |
|---|---|---|---|---|
| A61 | [`Amendment_A61_CutsceneStageBaking_Spec.md`](../../../../Docs/AnimationToolkit/Amendment_A61_CutsceneStageBaking_Spec.md) | `CutsceneStageAuthoring` + baker: the asset and its scene bindings bake to a `CutsceneStage` entity; cast panel "Sync to Stage"; `CreatePlayRequestFromStage` | — | A62 |
| A62 | [`Amendment_A62_CutsceneRuntimeCorrectness_Spec.md`](../../../../Docs/AnimationToolkit/Amendment_A62_CutsceneRuntimeCorrectness_Spec.md) | the five runtime bugs above, with PlayMode fixtures that fail on the old code | — | A61 |
| G1 | [`CutsceneIntegration_System.md`](CutsceneIntegration_System.md) | `CutsceneSystemGroup`, `CutsceneRequest` signal, `CutsceneActor` gate (AI/movement/input), Cinemachine bridge, `PlayCutsceneAction`, sound consumer, save lock — **first cutscene plays in the game** | A61, A62 | A63 |
| G0 | [`ActorContentRebuild_System.md`](ActorContentRebuild_System.md) | ✅ **DONE 2026-09-05, owner-verified on screen.** The rig, per-part authoring and one **walk clip** that make `MaleCitizen` a real toolkit actor | G1 | — |
| A63 | ✅ **T0–T6 done 2026-09-05, awaiting the owner's eyes.** [`Amendment_A63_CutsceneAttachLane_Spec.md`](../../../../Docs/AnimationToolkit/Amendment_A63_CutsceneAttachLane_Spec.md) | attach lane: socket attach, root attach (ride), hand-over, detach with impulse signal, hide-while-attached; editor lane + preview | A62 | G1 |
| A64 | ✅ **T1–T5 done 2026-09-05, awaiting the owner's eyes.** [`Amendment_A64_CutsceneMarks_Spec.md`](../../../../Docs/AnimationToolkit/Amendment_A64_CutsceneMarks_Spec.md) | marks lane + rendezvous holds: `CutsceneMoveToMark` request, arrival detection, timeout teleport; editor lane, Scene-view mark handles, preview travel | A63 | — |
| A65 | [`Amendment_A65_CutsceneCuesFacingBlocks_Spec.md`](../../../../Docs/AnimationToolkit/Amendment_A65_CutsceneCuesFacingBlocks_Spec.md) | holding events (the dialogue cue), host inspector seam for event payloads, runtime facing (`CutsceneFacing` + direction-variant re-pick), per-block speed and start offset | A64 | — |
| G2 | [`CutsceneInteractions_System.md`](CutsceneInteractions_System.md) | game consumers: marks → `MovementAPI`, dialogue cue ↔ `ActiveDialogue`, `CutsceneFacing` → `UnitFacing`, detach → `ThrownItemRequest`, player-control rule during rendezvous | A63, A64, A65, G1 | A66 |
| A66 | [`Amendment_A66_CutsceneEditorPolish1_Spec.md`](../../../../Docs/AnimationToolkit/Amendment_A66_CutsceneEditorPolish1_Spec.md) | Auto Key, multi-select / box-select / copy-paste across lanes, easing curve editor for cutscene keys | A65 | G2 |
| A67 | [`Amendment_A67_CutsceneEditorPolish2_Spec.md`](../../../../Docs/AnimationToolkit/Amendment_A67_CutsceneEditorPolish2_Spec.md) | viewport click-select, in-viewport gizmo + Key, frozen header column, cast/inspector compaction, viewport navigation parity | A66 | — |
| G3 | [`CutsceneAcceptance_System.md`](CutsceneAcceptance_System.md) | the "Rendezvous and Depart" cutscene authored with real assets, a debug trigger, the owner's verification checklist, perf check | everything above | — |
| A68 | [`Amendment_A68_CutsceneDocsRelease_Spec.md`](../../../../Docs/AnimationToolkit/Amendment_A68_CutsceneDocsRelease_Spec.md) | `cutscenes.md` rewrite, new `cutscene-api.md` reference, `Samples~` cutscene sample compiled through a temp assembly, CHANGELOG, HANDOFF closure, version bump | G3 | — |

**Critical path:** ~~A61 → G1 → G0 → A63 → A64~~ → **A65 (next)** → G2 → A66 → A67 → G3 → A68. A62 runs beside A61. **The first thing the owner should see on screen is G1's checkpoint**: a two-slot cutscene playing from a debug key — built and machine-verified 2026-09-05 in its own `Assets/Scenes/CutsceneG1Checkpoint.unity`, awaiting the owner's eyes.

> **A63 also grew a T0 (2026-09-05).** `NewRig.asset` declared zero sockets and the only
> socket-shaped object in the project was an inert `HandSocket` GameObject under `PlayerUnit.prefab`,
> so A63's own checkpoint had nothing to attach to. One `RigTarget` socket (RightHand) now sits on
> the rig and bakes onto every `MaleCitizen` actor. **A64 onward can assume actors have sockets.**

> **G0 was inserted after G1 (2026-09-05).** Building G1's checkpoint proved the integration works
> and simultaneously proved the game has **no animation content at all**: one empty `RigAsset`, two
> stub clips, a dangling `ClipSet.rig`, and `ActorAuthoring` referenced by zero prefabs and zero
> scenes. The checkpoint minions slide their root-motion lanes instead of walking. Every spec after
> this one adds features on top of a stack that cannot show a moving character, and §6's acceptance
> cutscene already assumes "`NewRig.asset` actors with the live clip set" that do not exist — so the
> content gets rebuilt before A63, not after.

## 4. Execution protocol (for the Sonnet session running a spec)

You are one session, one spec, fresh context. Do exactly this:

1. **Read, in order:** the repo root `CLAUDE.md`; `Docs/AnimationToolkit/HANDOFF.md` §2, §3, §5, §6 (conventions, gate, directives — skip its §4 history); `Assets/_Vault/Memories/Code/RULES.md`; the spec you were given, in full; then only the files the spec's "Read first" list names. Do not read the other specs in this roadmap.
2. **Ground before writing.** Open every file the spec tells you to change and read the surrounding code. The spec names real types and members verified on 2026-09-04; if a name has drifted, grep for it and follow the code, then note the drift in the spec's §7 log.
3. **Work the tasks in order.** After each task: save → compile gate (`mcp__UnityMCP__refresh_unity` → poll `editor_state.isCompiling` → `mcp__UnityMCP__read_console` for `error CS` / `BC`) → run **only the fixtures the task names** via `mcp__UnityMCP__run_tests` with `test_names`/`group_names` → tick the task's checkbox in the spec → commit that task alone (stage paths explicitly, never `git add -A`; message `A6x-Tn: <what>`). If the Editor is closed, say so, fall back to static review, and leave the checkbox unticked with a note.
4. **Prove a test can fail.** Before keeping any new fixture, revert the fix and watch it fail. A test that passes both ways is deleted.
5. **Full suites once, at the end of the spec** (`DotsAnimationToolkit.Tests.EditMode` then `.PlayMode`, plus `StitchPunk.Tests` / `StitchPunk.Tests.PlayMode` for game specs). Check the discovered total did not drop.
6. **Subagents are allowed, with one rule:** a subagent may read and write files for a task the spec marks **[parallel-safe]**, and never touches `mcp__UnityMCP__*`. Only the parent session compiles, runs tests, and commits. Three processes driving one Editor grew `Logs/Editor.log` to 2.2 GB once (HANDOFF §2) — that is the rule's origin. When two tasks are marked parallel-safe with each other, spawn them, wait, then run one gate over both.
7. **Owner checkpoints are real stops.** A task marked **⏸ owner checkpoint** ends your session with a message telling the owner exactly what to open, press and look at. Do not continue past it.
8. **Escalate, never quietly re-spec.** A spec/reality conflict becomes a note in the spec's §7 log and a question at the end of your session, not a silent edit that makes the doc agree with the code.
9. **Close the session** by updating the spec's status line, `Docs/AnimationToolkit/HANDOFF.md` §4 (one paragraph: what landed, what is owed), and the matching `_Vault/Memories/Code/*.md` note if you learned a trap.

## 5. Shared vocabulary (every spec uses these names; do not invent synonyms)

Toolkit runtime, namespace `DotsAnimationToolkit`:

- `CutsceneStage : IComponentData { BlobAssetReference<CutsceneBlob> blob; ulong cutsceneKey; }` + `CutsceneStageBinding : IBufferElementData { uint slotId; Entity target; }` — a baked, scene-resident cutscene (A61).
- `CutsceneClipBlockBlob.blendDuration` (A62), `.speed`, `.clipStartOffset` (A65), `.directionVariants` (A65).
- `CutsceneAttachMarker` / `CutsceneAttachMarkerBlob`, `CutsceneAttachKind { Attach, Detach }`, `CutsceneDetachSignal : IComponentData, IEnableableComponent` (A63).
- `CutsceneMarkKey` / `CutsceneMarkKeyBlob`, `CutsceneMoveToMark : IComponentData, IEnableableComponent`, `CutsceneHoldMarker.autoReleaseWhenMarksReached` (A64).
- `CutsceneEventMarker.holdUntilReleased`, `ICutsceneEventInspectorProvider`, `CutsceneFacing : IComponentData, IEnableableComponent` (A65).

Game, global namespace under `Assets/_Scripts/`:

- `CutsceneSystemGroup : GameSceneSystemGroup` between `PlayerSystemGroup` and `UtilityAISystemGroup`.
- `CutsceneRequest` one-frame signal entity; `CutsceneActor : IComponentData, IEnableableComponent` on every unit (baked disabled); `CutsceneCameraBridge` MonoBehaviour; `PlayCutsceneAction : NarrativeActionBase` (G1).
- `AnimEvents.Dialogue` registry entry (G2).

## 6. Acceptance cutscene — "Rendezvous and Depart" (the shape every spec builds toward)

```
t=0     marks issued: Player → mark P, MinionA → mark A, MinionB → mark B (beside the Cart prop)
        camera: wide shot, keyed
hold H1 rendezvous: autoRelease when all three marks reached (timeout 20 s → teleport)
t=+0    attach MinionA → Cart root (hidden), attach MinionB → Cart root (hidden),
        attach Player → Cart root (seated, visible, offset on the bench)
t=+0.5  event Dialogue (sequence "DepartureBanter", holdUntilReleased) → hold H2 "Dialogue"
t=+1    Cart root keys: drive 12 m along the road over 6 s; camera keys follow, one cut at t=+4
t=+7    detach all three at the destination (no impulse), placed on the ground beside the cart
t=+7.5  end. Actors stay where they stand; AI resumes; player input returns.
```

Assets: the cart is any prop prefab with a transform (no vehicle exists in the game yet — a crate scaled up is fine for acceptance). Minions are `NewRig.asset` actors with the live clip set. Scene: `Assets/Scenes/SubScenes/DOTSTestScene.unity`.
