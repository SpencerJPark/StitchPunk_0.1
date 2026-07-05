# Cutscene & Animation Stage System — Design Spec

> **Status:** ✅ spec ready · edit the inline **← DECISION** markers, then hand back to start the build.
> **Raw source:** [`futureneedsplan.md`](futureneedsplan.md) → "§2 Narrative Event System" / "§3 Cinematic Camera" (multi-actor scripted moments); plus the 2026-07 session decision to retire the play-mode animation preview.

---

**Skills Needed** (project skills in `.claude/skills/` — see [Skills index](../../Memories/Code/Skills.md)):
- `dots-blob-library` — `CutsceneSO` → `CutsceneLibrarySO` → `CutsceneLibraryBlob` pipeline (§4, Phase 4)
- `dots-system-scaffold` — `CutsceneBindingSystem`, `CutscenePlaybackSystem` (§5, Phase 4)
- `dots-feature-group` — new top-level `CutsceneSystemGroup` in `SystemGroups.cs` (§5, Phase 4)
- `dots-test` — EditMode fixtures for the shared clip sampler + root-motion interpolation math (Phases 1/4)

---

## 1. Purpose & v1 scope

Two halves, one asset:

**(A) Animation Stage (editor-only, Phases 1–3).** A true edit-mode editor — no play mode, no hybrid scene. The existing `AnimationClipEditorWindow` timeline UI is kept and its preview backend is replaced with a **`PreviewSceneStage`** (Prefab-Mode style isolated scene) that instantiates **N rig prefabs simultaneously** and drives their authoring GameObjects directly: `Transform.localPosition/rotation/scale` for pose, `MaterialPropertyBlock("_ImageIndex")` for flipbook frames. Sampling reads `AnimationClipSO` directly (never the blob) so edits are live with zero baking. The play-mode hybrid (AnimationEditorScene + EditorAnimationSystem etc.) is **deleted** in Phase 2.

**(B) Cutscene runtime (Phase 4).** The window's save document is `CutsceneSO` from day one: actor slots (rig prefab + start pos/facing relative to an anchor), per-actor clip cues, per-actor root-motion keyframes, and a timed event track (Attach/Detach, PlayClip, Camera, Dialogue, Sound). At runtime it bakes to an enum-indexed blob and a Burst `CutscenePlaybackSystem` puppets bound actor entities — this is how "character picks up another and throws them" and "two characters fight" get authored and played.

**v1 handles:**
- Multi-rig stage from day one (Phase 1 window opens with N actors; clip editing = 1-actor degenerate case).
- All existing clip-editing features (tracks, keyframes, scrub, per-layer preview) against the new backend.
- Cutscene playback of: clip cues, root motion, attach/detach with impulse, camera cues, dialogue cues, sound cues.
- Entry via the narrative pipeline (`PlayCutsceneAction` on `NarrativeEventSO`) or any system spawning a `CutsceneRequest` signal entity.

**Out of v1:** ragdoll/physics interplay during cutscenes (actors are fully keyframed while bound); branching/interactive cutscenes; camera *rails* (only cut/hold shots via the existing `CinematicCameraAction` semantics); recording gameplay into clips.

## 2. Architecture

**Editor half is plain managed code** — no ECS anywhere in the stage. The rig prefabs' `BodyPartAuthoring` components are the part registry (`AnimationTarget` → `Transform` map built per actor via `GetComponentsInChildren<BodyPartAuthoring>()`), so the stage previews exactly what bakers will bake without baking anything.

**Runtime half follows the request model**, entry pattern (b): a one-frame **signal entity**.

```
NarrativeEventManager (PlayCutsceneAction)         any system
        └────────────┬──────────────────────────────────┘
                     ▼  spawns
        CutsceneRequest signal entity { cutsceneId, anchorEntity, actor bindings }
                     ▼
        CutsceneBindingSystem  — validates actors at marks (walk-to-marks happens
          BEFORE via existing MoveNPCAction groups), tears down AI
          (ActionInterruptRequest + enables CutsceneActor tag), creates one
          CutscenePlayback entity, destroys the request
                     ▼  per frame
        CutscenePlaybackSystem — elapsed += dt; per bound actor:
          clip cues        → AnimationUtils.SetLayer (Override layer)
          root-motion keys → LocalTransform (position + facing, anchor-relative)
          events crossed   → Attach/Detach (ECB reparent to ItemSocket BodyPart),
                             PlaySound signal entity, dialogue cue, camera cue
                     ▼  at duration end
        cleanup: restore parents, disable CutsceneActor, revive AI (blank-slate Idle),
        destroy CutscenePlayback → completion observable by NarrativeEventManager
```

Runs in a new **`CutsceneSystemGroup : GameSceneSystemGroup`** placed **after `StateMachineSystemGroup`, before `MovementSystemGroup`** — cutscene owns transforms before movement/animation run; clip cues use the **`Override` animation layer**, which out-prioritizes whatever `UnitAnimationAssignmentSystem` writes to Base/Direction, so animation systems need **no** cutscene-awareness.

**← DECISION:** group placement — after StateMachine/before Movement (recommended, transforms win) vs. inside `AnimationAssignmentSystemGroup` (keeps all layer writers together but root motion then fights `MovementSystemGroup`).

## 3. Entry points

- **One-shot signal** — `CutsceneRequest : IComponentData` on a spawned one-frame entity: `{ CutsceneId cutsceneId; Entity anchor; FixedList64Bytes<Entity> actors; }` (LoggingSystem pattern: read all → act → `DestroyEntity(query)`). Actor order matches the SO's slot order; binding is **explicit** — the caller (NarrativeEventManager via `NarrativeIds.Entities`) resolves who plays which slot. **← DECISION:** max actors per cutscene; `FixedList64Bytes<Entity>` holds 7 — default cap **4** (slots beyond that are props attached mid-scene anyway).
- **Persistent playback state** — `CutscenePlayback : IComponentData` on its own entity (id, elapsed, bound actors, per-actor cue cursors). Exists only while a cutscene runs; its destruction is the completion signal.
- **Actor marker** — `CutsceneActor : IComponentData, IEnableableComponent` on unit entities (baked disabled by `UnitBakingUtil`, enabled during playback). Gates: `WinnerSelectionSystem` skips assignment, awareness systems skip emission (same style as the existing `IsCombatAction` guards).

## 4. Data model

**`CutsceneSO`** (new, `_Scripts/Data/SOs/`) — the *single* document for both the editor window and runtime baking:
- `cutsceneId : CutsceneId` (new enum, `_Scripts/Data/Enums/CutsceneEnums.cs`)
- `duration : float`
- `actors : List<CutsceneActorSlot>` — `{ string slotName; GameObject rigPrefab; Vector3 startOffset; float startFacing; List<ClipCue> clipCues; List<RootKey> rootKeys; }`
  - `ClipCue { float time; AnimationType animation; AnimationLayerType layer; bool looping; }`
  - `RootKey { float time; Vector3 position; float facing; InterpolationMode interpolation; }` (anchor-relative; reuses the existing `InterpolationMode` enum)
- `events : List<CutsceneEvent>` — `{ float time; CutsceneEventType type; int actorIndex; int targetActorIndex; AnimationTarget socket; Vector3 impulse; int dialogueSequenceId; SoundId sound; float cameraHold; Vector3 cameraOffset; }` (flat struct, fields used per type — same pragmatic shape as `BehaviorCommand`)
  - `CutsceneEventType { Attach, Detach, Dialogue, Sound, Camera }`

`rigPrefab` is **editor-only** (runtime binds live entities); it is skipped at bake. Everything else bakes via `dots-blob-library`: `CutsceneLibrarySO` (`_CutsceneLibrary.asset`) → `CutsceneLibraryBlob` (enum-indexed by `CutsceneId`, `BlobArray` per track) → `CutsceneLibrary` singleton, baked by `CutsceneLibraryBakingSystem` in `PostBakingSystemGroup`. Clip references stay `AnimationType` enums — the actual keyframes already live in the `AnimationLibrary` blob; the cutscene never duplicates them.

**Editor-side sampler:** `AnimationClipSampler` (new static class, `Editor/AnimationEditor/`) — the SO-sampling + easing + layer-compositing logic extracted verbatim from today's `EditorAnimationSystem` (`SampleClipSO` / `SampleKeyframesSO` / `ApplyEasing` / `ApplyTrackToPose`). Pure static managed code, unit-testable EditMode.

## 5. Systems

| System | Group | Reads | Writes |
|---|---|---|---|
| `CutsceneBindingSystem` | `CutsceneSystemGroup` (OrderFirst) | `CutsceneRequest` entities, `CutsceneLibrary` blob | creates `CutscenePlayback`; enables `CutsceneActor` + `ActionInterruptRequest` on actors; destroys requests. Main-thread ECB (structural). |
| `CutscenePlaybackSystem` | `CutsceneSystemGroup` | `CutscenePlayback`, blob, `BodyPart` buffers (socket lookup) | `AnimationLayer` buffers (Override layer via `AnimationUtils`), `LocalTransform` (root motion), `Parent` (attach/detach via ECB), spawns `PlaySound` entities, fires dialogue/camera cues, cleanup at end. |

Editor-only (not systems): `AnimationStage : PreviewSceneStage` + reworked `AnimationClipEditorWindow` driving it from `EditorApplication.update`, sampling at a fixed editor frame rate. **← DECISION:** editor preview frame-rate source — hardcoded 24 (matches `GameDataAuthoring.animationFrameRate` default; recommended) vs. an EditorPrefs setting.

## 6. MonoBehaviour bridge

Only reused bridges — no new manager. `NarrativeEventManager` gets **`PlayCutsceneAction : NarrativeActionBase`** `{ CutsceneSO cutscene; List<int> actorEntityIds; int anchorEntityId; bool waitForCompletion; }` — it resolves `NarrativeIds` → entities, spawns the `CutsceneRequest`, and (when waiting) polls for the `CutscenePlayback` entity's destruction, exactly like `MoveNPCAction` polls `Movement.isMoving`. Dialogue cues route through the same override mechanism `DialogueTriggerAction` uses; camera cues reuse `CinematicCameraAction`'s hold/offset semantics. **← DECISION:** dialogue/camera cues fired from the ECS playback system via a small cue buffer the manager drains, vs. keeping those two cue types manager-side by splitting the narrative group (recommended: cue buffer — keeps one clock).

## 7. Integration points

- **Animation:** writes the `Override` layer only; `AnimationTimeSystem`/`AnimationSamplingSystem` untouched. Walk-to-marks uses existing `MoveNPCAction`.
- **AI:** teardown via existing `ActionInterruptRequest` path (`BehaviorInterruptSystem`); `CutsceneActor` gate added to `WinnerSelectionSystem` + awareness `ClearOptionsSystem` neighborhood (one-line guards).
- **Items/characters as props:** attach = set `Parent` to the socket entity found in the target's `BodyPart` buffer (`BodyPartFlags.ItemSocket`, e.g. `ItemRightHand`); detach = remove parent + write impulse (**← DECISION:** impulse as `PhysicsVelocity` vs. reusing the `Ragdoll2DLaunch`/thrown-item path from `ItemSystemGroup`).
- **Sound:** spawns existing `PlaySound` signal entities.
- **Save:** none — cutscene state is transient; a save during playback is disallowed (playback entity has no `IPersist`).
- **Deleted** (Phase 2): `AnimationEditorScene.unity`, `AnimationEditorSubScene.unity`, `EditorAnimationSystem.cs`, `EditorApplyAnimatedPoseSystem.cs`, `AnimationPreviewController.cs` (+Editor), `AnimationEditorSceneTagAuthoring.cs`, `EditorAnimationTimeControlAuthoring.cs`.

## 8. Proposed file manifest

**New:** `Editor/AnimationEditor/AnimationClipSampler.cs`, `Editor/AnimationEditor/AnimationStage.cs`, `Data/SOs/CutsceneSO.cs`, `Data/SOs/CutsceneLibrarySO.cs`, `Data/Enums/CutsceneEnums.cs`, `Data/Structs/CutsceneBlobs.cs`, `Components/Cutscene/CutsceneComponents.cs`, `Authoring/Cutscene/CutsceneLibraryAuthoring.cs`, `Systems/PostBakingSystemGroup/CutsceneLibraryBakingSystem.cs`, `Systems/CutsceneSystemGroup/CutsceneBindingSystem.cs`, `Systems/CutsceneSystemGroup/CutscenePlaybackSystem.cs`
**Edited:** `AnimationClipEditorWindow.cs` (preview backend → stage; actor panel), `SystemGroups.cs` (+`CutsceneSystemGroup`), `NarrativeEventSO.cs`/`NarrativeEventManager.cs` (+`PlayCutsceneAction`), `WinnerSelectionSystem.cs` (+gate), `UnitBakingUtil.cs` (+`CutsceneActor` disabled)
**Deleted:** the seven hybrid files/scenes listed in §7.
**Assets:** `_CutsceneLibrary.asset`, first test asset `Cutscene_ThrowTest.asset` (2 actors + 1 prop attach/detach).

## 9. Build phases

1. **Sampler + Stage (editor foundations).** Extract `AnimationClipSampler`; build `AnimationStage` (multi-actor instantiate, part-map, pose+`_ImageIndex` apply, rest-pose restore); temporary toolbar button opens the stage with 1 actor playing a hardcoded clip. *Proves: edit-mode preview parity with the hybrid scene.*
2. **Window rework + hybrid deletion.** `AnimationClipEditorWindow` drives the stage (clip pick, scrub, play, layer solo, actor add/remove with `CutsceneSO` as document); delete the seven hybrid files + two scenes; EditMode tests for sampler.
3. **Cutscene authoring depth.** Root-motion keys + event track editing in the window (timeline rows per actor); anchor-relative placement gizmos; author `Cutscene_ThrowTest.asset`.
4. **Runtime playback.** `dots-blob-library` pipeline; `dots-feature-group` for `CutsceneSystemGroup`; binding + playback systems; `PlayCutsceneAction`; AI gates; verify throw-test in `DOTSTestScene`.

## 10. Verification

- **Ph1/2 (Spencer, Editor):** open window with no play mode → blink clip previews identically to the old hybrid (eyebrow bob + eye frame swap); editing a keyframe updates the stage same-frame; two actors preview different clips simultaneously; console stays silent.
- **Ph3 (Spencer, Editor):** scrub `Cutscene_ThrowTest` — actor A's root arc and the prop attaching to `ItemRightHand` at the keyed time are visible in the stage.
- **Ph4 (play `DOTSTestScene`):** debug key spawns the `CutsceneRequest` on two placed citizens → they play the throw sequence, AI stays silent during it (`UtilityActions` empty in Entities window), both return to Idle wandering after; save blocked during playback logs a warning.

## Open decisions (collected)

- [ ] §2 — `CutsceneSystemGroup` placement (after StateMachine / before Movement, vs inside animation assignment).
- [ ] §3 — max bound actors (default 4; `FixedList64Bytes` ceiling 7).
- [ ] §5 — editor preview frame rate: hardcoded 24 vs EditorPrefs.
- [ ] §6 — dialogue/camera cue routing: ECS cue buffer drained by `NarrativeEventManager` (recommended) vs manager-side group split.
- [ ] §7 — detach impulse mechanism: `PhysicsVelocity` vs reuse thrown-item path.
- [ ] §9 Ph2 — include "pose part with scene handles → write back as keyframe" capture in Phase 2, or defer to Phase 3.
