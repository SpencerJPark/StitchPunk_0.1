# Amendment A71 — Actor Editor: layers, animations, direction and ragdoll, mixed live

**Status:** ✅ spec written 2026-09-07, nothing built. Owner-requested; depends on **A70** (the
profile asset, blob, builder, `ActorProfileApi`, `ActorFacing`, ragdoll triggers). Package version
after this lands: **0.17.0**.
**Scope:** `Packages/com.dotsanimationtoolkit/Editor/` only, plus the one `ClipPreviewController`
entry point in §3.4 and tests/docs. No runtime change beyond what A70 shipped. No game code.
**Execution protocol:** `Cutscene_Roadmap.md` §4, prefix `A71-Tn:`. HANDOFF §2 rules: UI Toolkit
only (`Conformance_E`), one `<summary>` per file, `Request…Rebuild` never from a value-changed
callback (`AnimationToolkit.md` in the vault — the drag-kill trap), no `GUIUtility.hotControl`
writes from a tick.

---

## 1. Why this exists (owner's words, 2026-09-07)

> "I renamed the direction tab to Actor Editor … on the left side I can add animation layers, under
> those I can set what animation an actor can do … add the option for there to be a direction
> dimension … this is supposed to be the testing area for actor animation and how they will all
> mix. So I can manipulate direction or swap out different layer commands … I should also be able
> to test ragdoll in this area too … a character dying which would trigger the death face and a
> ragdoll and then having the character reanimate."

The 2D Direction Sets pane already has the viewport, the head-on camera, the direction slider
through `FacingResolver`, and a clip queue over five east-side slots. It previews **one** clip. The
Actor Editor previews a **profile**: every layer composited, triggered like the game triggers them,
turning through the profile's directions, dropping and restoring the ragdoll on the entries that
say so.

## 2. Read first (in this order, and only these)

1. `HANDOFF.md` §2, §3, §5, §6; `Assets/_Vault/Memories/Code/AnimationToolkit.md` (the
   rebuild-from-callback trap, the throttled preview, the tag surfaces).
2. `Amendment_A70_ActorProfile_Spec.md` §3 in full — every type the panel edits.
3. `Editor/ClipEditor/DirectionSets/DirectionSetsPanel.cs` (986 lines — the pane you are replacing:
   `SetSource`, `SetTicking`/`Tick`, the orbit capture/restore at ~381–402, the facing/status block
   at ~857–905), `DirectionSetClipQueueView.cs` (kept), `DirectionSetContext.cs` (deleted),
   `DirectionSetAssetOpener.cs` (deleted).
4. `Editor/ClipEditor/ClipEditorWindow.cs`: `BindTabs` (~1557), `ApplyActiveTab` (~1610),
   `Show2DDirectionSetsTab` (~1714), `OnRagdollPreviewToggleChanged` (~1525);
   `ClipEditorWindow.uxml` lines 10–15 and 149; `ClipEditorTab.cs` (the owner's uncommitted rename
   diff is in the working tree — build on it).
5. `Editor/ClipEditor/Preview/ClipPreviewController.cs`: `SamplePose` (~931 — one clip per call,
   `rigMirror.ApplyPose(targetId, in pose)` per target), `TryEnableRagdollPreview` /
   `DisableRagdollPreview` (~160–195), `StepRagdollPreview` (~1588), `Render`, `SetClipSet` (~265,
   single set), `HasRegistry`, `FrameRig`.
6. `Editor/ClipEditor/Cutscene/CutscenePreviewController.cs` ~850–920 — the one editor path that
   already resolves a facing **with mirror** for a slot; reuse its mirror application or say in §7
   why not.
7. `Runtime/Sampling/ClipSampler.cs` `CompositeLayers` (~463), `MapTime`, `ResolveLoopMode`;
   `Runtime/Systems/PlaybackTimeSystem.cs` (how a layer advances, finishes, deactivates a `Once`
   clip, promotes the queue); `EventEmissionSystem.cs` + `Tests/EditMode/EventWrapMathTests.cs`
   (marker-crossing math for at-event triggers).
8. `Editor/ClipEditor/Components/VocabularyPicker.cs` + `VocabularyPickerConfig`;
   `Editor/ClipEditor/Cutscene/CutsceneEditorPanel.cs` cast-panel slot rows (~1527 for prefab
   placement — not needed here, but the row/inspector idiom is the one to match).
9. Tests: `Tests/EditMode/ClipEditorLayoutTests.cs` (element-name assertions at 37–39, 84, 162),
   `DirectionSetCoverageTests.cs`, `RagdollPreviewParityTests.cs` (the parity obligation pattern).

## 3. Design

### 3.1 The tab

`ClipEditorTab.DirectionSets` → `ClipEditorTab.ActorEditor` (same position, the owner's order:
New Rig · Clip Editor · VAT Bake · **Actor Editor** · Cutscene Editor). UXML: `tab-direction-sets`
→ `tab-actor-editor`, `direction-sets-pane` → `actor-editor-pane`. `Show2DDirectionSetsTab` →
`ShowActorEditorTab`; the pane hosts `ActorEditorPanel` (`Editor/ClipEditor/ActorEditor/`), built
lazily, nothing torn down on hide, `SetTicking` on show/hide — the existing cover-pane contract.
`ClipEditorLayoutTests` updated in the same commit. Double-clicking an `ActorProfileAsset` opens
the window on this tab with it loaded (`ActorProfileAssetOpener`, the `DirectionSetAssetOpener`
pattern); `DirectionSetAsset` gets a plain inspector through a `DirectionSlots` property drawer
built on `DirectionSetClipQueueView` (the queue view becomes a reusable element over a
`DirectionSlots`, not over a `DirectionSetAsset`).

### 3.2 Layout (three columns under a header row)

```
[ Profile ▾ MaleCitizen.profile ]  [ badge ✓/⚠/✖ ]        [ Reset ]  [ ▶ Play  ‖ Pause ]  [ Direction ───●─── SE ] [ 137° → SouthEast, mirrored ]
┌───────────────────────┬────────────────────────────────────────┬──────────────────────────────┐
│ LAYERS                │                                        │ INSPECTOR (selection)         │
│ ▣ Base   ★Idle   ●    │                                        │ Animation: MeleeContinuous    │
│   ├ Idle        ▶ ■ ● │            viewport                    │  Name        [picker]         │
│   └ Walk        ▶ ■   │        (window's preview)              │  Direction   [x] dimension    │
│ ▣ Action              │                                        │   queue: SE ▣ NE ▢ S ▢ N ▢ E ▢│
│   ├ MeleeCont.  ▶ ■   │                                        │   target: Two · coverage Two  │
│   ├ Death       ▶ ■   │                                        │  Loop/Speed/Blend In          │
│   └ Resurrection ▶ ■  │                                        │  Ragdoll  [Start ▾] at [event]│
│ + Layer               │                                        │  Layer     Action             │
│ ▣ Face … ▣ Eyes …     │ status: Ragdoll on (Death) · Eyes: Blink t=2.4s                        │
│ ▣ Override            │                                        │                              │
└───────────────────────┴────────────────────────────────────────┴──────────────────────────────┘
```

- **Layers column**: a UI Toolkit `TreeView` over `profile.layers[i].animations[j]`. Layer rows
  show name, `defaultActive` toggle, the starter (★) picked from that layer's animations, and a
  live ● when the layer is Active in the composer. Base and Override rows carry no delete and do
  not drag; **+ Layer** inserts above Override; other layers reorder by drag (a semantic edit —
  the row tooltip says so). Animation rows: name, ▶ (trigger `PlayAnimation`), ■ (trigger
  `StopAnimation`), ● while its layer plays it. **+ Animation** on a layer row opens the
  `VocabularyPicker` for Animation Names (the picker's Create row mints and persists — HANDOFF §5).
- **Viewport**: the window's `ClipPreviewController`, camera captured/restored exactly as the
  direction pane does today, billboard on, head-on. Status line below.
- **Inspector**: profile (rig, clip sets, `turnDirections`), layer (name, `defaultActive`,
  starter), animation (name, `hasDirections`, clip **or** the direction queue + `targetDirections`
  + derived coverage readout, loop, speed, blend-in, ragdoll trigger + at-event picker from the
  Event Names registry, read-only "layer"). All writes through `Undo.RecordObject(profile)` +
  `EditorUtility.SetDirty`; the tree polls the asset each tick (the direction pane's
  `observedSlots` idiom) so an inspector edit or undo lands without event plumbing.
- **Header**: profile `ObjectField`; picking one sets the window's **Rig** field to `profile.rig`
  (the other tabs read it) and leaves the window's Clip Set field alone; validation badge
  (`ActorProfileValidation` P1–P7 + `ClipValidation.ValidateBind(rig, clipSets)`); **Reset**
  (every layer back to its starter or inactive, ragdoll off, direction to SE); transport;
  direction slider 0–360° with the readout "angle → facing, mirrored".

### 3.3 `ActorPreviewComposer` — the layers, advanced like the runtime

`Editor/ClipEditor/ActorEditor/ActorPreviewComposer.cs`, a plain class (no ECS) owning:

- the profile blob (`ActorProfileBuilder.Build`, rebuilt when the asset changes),
- a registry built from `(profile.rig, profile.clipSets)` through `ClipRegistryBuilder.Build`
  (the controller gains `SetClipSets(IReadOnlyList<ClipSetAsset>)` — today it takes one),
- a `NativeArray<PlaybackLayer>` sized `profile.layers.Count`, seeded from starters,
- `Direction facing` and the last applied facing,
- ragdoll state (`on`, which entry started it).

Per tick: advance every Active layer the way `PlaybackTimeSystem` does — **through the same static
step function**. If `PlaybackTimeSystem` keeps that logic inline, extract `PlaybackTimeMath.Advance
(ref PlaybackLayer, ref ClipBlob, float deltaTime)` (name per the `Math` suffix rule) into
`Runtime/Sampling/` and have both call it; a preview that finishes a `Once` clip a frame earlier
than the game is the kind of drift `RagdollPreviewParityTests` exists to forbid, so
`ActorPreviewParityTests` pins it. Then detect marker crossings per layer (`EventWrapMath`) for
at-event ragdoll triggers. Then `facing != applied` → re-pick directional layers in place (the
`ActorFacingRepickSystem` rule). Then pose: `previewController.SampleCompositedPose(in layers,
mirrorX)` (§3.4). Then, if the ragdoll is on, the controller's existing `StepRagdollPreview` runs
after the pose write and wins its nodes — which is exactly the game's "apply stomps, ragdoll
re-writes" order (`Gotchas.md`), so a Face-layer clip keeps animating parts that are not ragdoll
bodies while the body lies there. That is the owner's death-face-plus-ragdoll shot.

Triggers: ▶ = `ActorProfileApi.TryResolve(ref blob, key, facing, …)` then the same state change
`CommandApplySystem.ApplyPlay` makes (clip, `animationKey`, blend from `blendIn`, loop, speed); a
`Start` trigger at play → `previewController.TryEnableRagdollPreview` (refusal reason to the status
line — a rig with no bodies reads "Ragdoll: no bodies on NewRig (P6)"); `Stop` →
`DisableRagdollPreview` (restores the captured pose) and then the entry plays. ■ = `StopAnimation`
semantics (only if that layer is playing that key). Scrubbing is per-layer through the tree row's
small time field; the transport's Play/Pause is global.

### 3.4 One new entry on `ClipPreviewController`

```
public bool SampleCompositedPose(in NativeArray<PlaybackLayer> layers, bool mirrorX)
```

Same body shape as `SamplePose` — `RestoreBillboardedNodes`, `RebuildRestPosesIfNeeded`, loop over
`sortedTargetIds`, but `ClipSampler.CompositeLayers(ref registry, in layers, targetIndex, in rest,
out pose)` instead of `SamplePose`, then mirror when `mirrorX` (negate `localPosition.x`,
`rotation.z`, `scale.x`, skipping parts under a `facesDirection` ancestor — the
`TransformSampleSystem` line ~176 rule; reuse `CutscenePreviewController`'s implementation if it
has one), then `rigMirror.ApplyPose`; socket markers and bone tracks after, as today. `SamplePose`
is untouched (the Clip Editor tab keeps it).

### 3.5 What leaves

`DirectionSetsPanel.cs`, `DirectionSetContext.cs` (`IDirectionSetContextProvider`,
`DirectionSetContextEntry`), `DirectionSetAssetOpener.cs`, the `Unit Context` dropdown, the
`SetContextProvider` static. The host's provider is deleted in G5 — until then the game's
`UnitDirectionSetContextProvider.cs` fails to compile, which A70 already caused; note it in §7 and
move on. `DirectionSetClipQueueView.cs` moves to `Editor/ClipEditor/ActorEditor/` and takes a
`DirectionSlots`.

### 3.6 Cutscene cast panel

One button per Actor slot: **Fill from profile** — an `ActorProfileAsset` picker that writes
`slot.rig` and `slot.clipSets` (not the direction set; a cutscene block's variants stay its own).
Small, and it is how a staged cutscene actor stops drifting from the profile that drives it in-game.

## 4. Decisions (recorded; revert notes inline)

- **A71-D1 The Actor Editor previews the profile's own registry, not the window's Clip Set.** A
  profile lists several sets; the window's picker holds one. Revert: restrict a profile to one set.
- **A71-D2 Picking a profile sets the window's Rig, never its Clip Set.** The rig is shared by
  every tab (New Rig, Clip Editor gizmos, VAT Bake); the clip set is the Clip Editor's own edit
  target. Revert: push `clipSets[0]` too.
- **A71-D3 The composer advances layers with the runtime's own step function, extracted if
  needed.** A second implementation is a parity bug waiting to happen (HANDOFF §9 lesson 3).
- **A71-D4 Ragdoll in the preview uses the window's existing ragdoll preview**, toggled by the
  profile's triggers instead of the toolbar toggle; the toolbar toggle is hidden on this tab so
  there is one authority. Revert: show both and let the last click win.
- **A71-D5 Triggers happen in the editor with the same resolve the game uses**
  (`ActorProfileApi.TryResolve`, `ActorProfileBuilder`). No editor-side re-implementation of the
  fold.
- **A71-D6 The Unit Context seam is deleted, not generalised.** The profile *is* the context.

## 5. Tasks

After each: compile gate → the task's fixtures → tick → commit `A71-Tn:`. Full suites at T9.

- [x] **T1 — Rename and re-home.** `ClipEditorTab.ActorEditor`, UXML/USS names, `ShowActorEditorTab`,
  `ActorEditorPanel` shell in a new folder with the header row and three empty columns,
  `DirectionSetClipQueueView` moved and re-targeted at `DirectionSlots`, the `DirectionSlots`
  property drawer for `DirectionSetAsset`, the old pane/context/opener deleted,
  `ActorProfileAssetOpener`. *Fixtures:* `ClipEditorLayoutTests` element lists; one test that the
  drawer binds to `slots`. *Gate:* the tab shows an empty panel with a profile field.
- [x] **T2 — `SetClipSets` + `SampleCompositedPose` on the controller.** [parallel-safe with T3]
  *Fixture:* `ClipPreviewCompositeTests` (EditMode, in-memory registry as `LayerCompositionTests`
  builds one) — two layers with `Override` tracks on the same target: the composited pose equals
  `ClipSampler.CompositeLayers`'s answer, and `mirrorX` negates `localPosition.x`.
- [x] **T3 — `ActorPreviewComposer` + `PlaybackTimeMath` extraction.** [parallel-safe with T2]
  *Fixtures:* `ActorPreviewComposerTests` — a `Once` layer deactivates when its clip ends; a
  facing flip re-picks a Four-coverage entry and keeps `time`; an at-event `Start` trigger fires
  once per crossing and not on the frame after; `ActorPreviewParityTests` — the composer and
  `PlaybackTimeSystem` (through `PlaybackTestActor`) agree on `time`/`flags` after N steps for a
  looping and a `Once` clip. Revert each fix and watch the test fail before keeping it.
- [x] **T4 — Layers column.** `TreeView`, bookend rules, + Layer / + Animation (through
  `VocabularyPicker`), drag-reorder between the bookends, ● live indicators, ▶/■ wired to the
  composer, per-row time field. Polling refresh; no rebuild from a callback.
- [x] **T5 — Inspector column.** Profile / layer / animation blocks per §3.2, direction queue with
  the coverage readout (the `TryGetEffectiveDirections` text the old pane showed), ragdoll trigger
  + at-event picker, validation badge composed from P1–P7 and `ValidateBind`.
- [x] **T6 — Header, transport, direction slider, Reset, status line.** Slider through
  `FacingResolver.ResolveClipFacing(angle→Direction, profile.turnDirections, …)` with the
  "137° → SouthEast, mirrored" readout; the toolbar ragdoll toggle hidden on this tab.
- [x] **T7 — Ragdoll mix.** `Start`/`Stop` entries drive `TryEnableRagdollPreview` /
  `DisableRagdollPreview` from the composer; layers keep advancing under the drop; the status line
  names the entry that started it; a rig without bodies refuses with the P6 text. *Gate:* on a
  scratch profile over a rig **with** bodies (RG's, or a two-body scratch rig built in the test),
  ▶ Death drops and a Face-layer sprite key keeps stepping on a non-body part; ▶ Resurrection
  restores the exact captured pose (float-equal, the `RagdollToggleTests` assertion) and then plays.
- [x] **T8 — Cast panel "Fill from profile".** One button, one write, no fixture.
- [ ] **T9 — Docs, CHANGELOG, version, full suites.** `Documentation~/actor-profiles.md` gains
  the editor half (with the §3.2 sketch); `clip-editor.md` tab list; `cutscenes.md` cast-panel
  line; `index.md`. CHANGELOG `## [0.17.0]`; `package.json` + the version assertion. Full EditMode
  + PlayMode; the game assemblies stay red until G5 (record which fixtures). HANDOFF §4 paragraph.
- [ ] **⏸ T10 — Owner checkpoint.** Build a scratch profile over `NewRig` + `NewClipSet` (Base ★
  Walk; an `Action` layer with one directional entry holding Walk in `southEast` only; a `Face`
  layer empty) and open it. The owner: adds a layer between the bookends, adds an animation to it
  through the picker (typing a new name once), toggles its direction dimension and fills a second
  slot, sweeps the direction slider and watches the pick and the mirror, presses ▶ on the Action
  entry and sees it play over Base and hand back when it ends, sets a ragdoll `Start` on an entry
  and presses ▶ (with RG landed the body drops and Base keeps cycling under it; without it the
  badge says P6), presses ■, presses Reset. Every "that should work differently" is §7 feedback;
  the layout itself is the owner's to change at this stop — his UI preferences are the one thing
  these specs do not pre-decide.

## 6. Owner checkpoint

T10 above. It is the first time the owner sees the profile model; expect layout changes and record
them as A71 follow-ups rather than re-opening A70's data model.

## 7. Build log

- **T1** (`d2d3d647`) — `ClipEditorTab.ActorEditor`, `tab-actor-editor`/`actor-editor-pane`,
  `ShowActorEditorTab`, `ActorEditorPanel` shell, `ActorProfileAssetOpener`; direction-sets pane
  and context seam removed. Built on the owner's uncommitted tab-reorder diff (`eee615b3`).
- **T2** (`0738e96f`) — `ClipPreviewController.SetClipSets`/`SampleCompositedPose`; the west-facing
  mirror negates the same four components `TransformSampleSystem` does (`localPosition.x`,
  `rotation.y`, `rotation.z`, `scale.x`), not a uniform scale-by-minus-one.
  `ClipPreviewController` already implements the new `IActorPosePresenter` seam the composer reads
  through — one interface, not a spec deviation, added to keep the composer decoupled from the
  editor-preview concrete type.
- **T3** (`1e59b2cc`) — `PlaybackTimeMath`/`PlaybackCommandMath` extracted into
  `Runtime/Sampling/`; `PlaybackTimeSystem`/`CommandApplySystem` call the same static functions the
  composer does. **Deviation:** `ActorPreviewComposerTests`/`ActorPreviewParityTests` live in
  `Tests/EditMode/`, not PlayMode, because the PlayMode asmdef may not reference the Editor
  assembly the composer lives in.
- **T4/T6/T7** (`56c0b9a6`) — layers column, header (badge/transport/direction slider/Reset),
  ragdoll mix. **Deviation:** layer reorder is Up/Down buttons (`ActorEditorLayersColumn.MoveLayer`)
  rather than drag — cheaper to make undo-safe and to keep off the drag-kill-rebuild trap than a
  `TreeView` drag handler.
- **T5** (`adc2a964`) — inspector column: profile/layer/animation blocks, direction queue with
  coverage readout, ragdoll trigger + at-event picker, validation badge.
- **T8** (`93099555`) — cutscene cast panel "Fill from Profile" button, one write of
  `slot.rig`/`slot.clipSets` from an `ActorProfileAsset` picker.
- Game fixtures left red for G5: unchanged from A70 — `UnitDirectionSetContextProvider.cs` (game)
  still fails to compile, now because the seam it implements no longer exists at all rather than
  because it targeted a removed field.
- T10 owner checkpoint: not yet run.
