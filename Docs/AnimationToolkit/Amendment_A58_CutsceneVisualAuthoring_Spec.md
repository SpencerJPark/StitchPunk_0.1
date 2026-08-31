# Amendment A58 — Cutscene Visual Authoring

Raised 2026-08-30 after the owner's first hands-on session with Phase G. Owner verdict, verbatim
in intent: *"I need to visually see what I am doing exactly or else this isn't a useful tool at
all. I need to see where I am animating and see the animations and SEE what I am doing."*
This amendment supersedes Phase G spec §3's "timeline + inspector only" line and G3's recorded
preview cuts. Everything else in Phase G — data model, timeline UI, bake, runtime player — stands.

**Amended 2026-08-30 by `Amendment_A59_EmbeddedSceneViewport_Spec.md`:** §2.1's "embedded
scene-rendering viewport stays out of scope" is revoked by the owner — the tab gains its own
viewport rendering the open scene. The docked Scene-view workflow this doc describes keeps
working but is no longer the primary shape.

## 1. The defect this amendment exists to kill

Scrubbing the built editor moves root positions and hand-keyed parts, but **clip blocks preview
nothing** — a bound actor slides around the scene in its rest pose. Since "keyframe, keyframe,
walking clip on loop" is the tool's founding sentence, the preview shipped without the feature the
tool is for.

**The cut was rationalized on a false premise.** G3's record says clip preview "needs the baked
clip registry a real actor bake produces." It does not: the Direction Sets pane builds a
`ClipRegistryBlob` in-editor from a plain `(rig, clipSets)` pair via `ClipRegistryBuilder.Build`
and samples poses through `ClipSampler.SamplePose` with no play mode and no actor bake — and every
cutscene slot pins exactly a rig + clip sets. The capability existed in the same package the whole
time. Lesson for the record: **a scope cut whose justification claims impossibility must cite the
thing that was tried** — this one would not have survived a grep for `ClipRegistryBuilder.Build`
call sites.

## 2. Owner product calls (2026-08-30)

1. **The Scene view stays the viewport — but preview becomes real.** Scrub or play, and bound
   actors visibly play their clips: walk cycles cycling with correct loop phase, seam overlaps
   cross-fading, root keys carrying the actor, part-track overrides layering on top, facing
   applied, the camera shot previewing. The docked-windows workflow (Hierarchy + Scene view +
   Cutscene window) is the intended shape — what was missing is the animation, not a fourth
   window. An embedded scene-rendering viewport stays out of scope (revisit only if the docked
   flow still feels wrong after real use with animation working).
2. **Slots stage the scene.** A slot gains an actor-prefab reference; **Place in Scene**
   instantiates it, binds it, and hands it over as a normal hierarchy object. Binding an
   already-placed GameObject still works. An empty scene must be stageable into a full cutscene
   without leaving the tool.
3. **A cast panel joins the tab.** Left column: one row per slot with binding state and Place /
   Bind / Select / Frame actions. It is the cutscene's own view of the scene — selection syncs to
   Unity's Hierarchy/Inspector/gizmos, it does not replace them.
4. **Editor Play, and holds really hold.** A transport plays the cutscene in-editor; a hold
   marker pauses the clock (loops keep cycling, camera holds) until Continue is clicked — a
   faithful rehearsal of runtime pacing. A skip-holds toggle exists for quick full runs.

## 3. Design

### 3.1 Animated clip preview (the core)

`CutscenePreviewController` gains per-slot sampling registries:

- One `ClipRegistryBlob` per actor slot's `(rig, clipSets)` bind, built with
  `ClipRegistryBuilder.Build` on preview enter, cached, rebuilt only on membership/rig change —
  the Direction Sets pane's `RebuildRegistryIfClipsChanged` guard is the model (a scrub must
  never rebuild). Disposed on preview exit (Persistent blob, same discipline as everywhere else).
- At the playhead, per slot: resolve which clip block(s) cover this time (at a seam overlap,
  both), compute each block's local clip time honoring `loop` and loop phase, sample via
  `ClipSampler`, cross-fade the overlap with the existing layer-blend math, then compose
  part-track override keys on top (Override composition, as now). Write the result onto the bound
  GameObject's tagged `RigTargetAuthoring` children — the pipe G3 already built for override keys;
  this amendment feeds it the full pose instead of only the overrides.
- **Sprite tracks preview too** where the bound child carries a `SpriteRenderer` (index and flip
  writes captured/restored like transforms). If a sprite-track feature proves genuinely
  unreachable on scene GameObjects, that gap is *recorded in this doc with what was tried* — §1's
  lesson is binding.
- **Facing is applied, not just displayed**: resolve the angle (existing
  `TryResolveFacingAngle`), run it through `FacingResolver` against the slot's `DirectionSetAsset`
  to pick variant/flip, and apply it in the preview. The read-only angle number stays as
  diagnostics.

### 3.2 Editor play transport

Play / pause / stop / loop / speed on the cutscene toolbar, ticked from `EditorApplication.update`
(the `ClipPreviewController` pattern). At a hold marker the transport pauses and the toolbar shows
**Continue** (and which hold id is gating); the skip-holds toggle runs through them. Stop returns
to the pre-play playhead. Preview-shot camera behavior unchanged (toggle, default on).

### 3.3 Cast panel + staging

- New left column in `CutsceneEditorPanel` (`TwoPaneSplitView` with the timeline): per-slot rows —
  state dot (● bound / ○ unbound / ⚠ binding broken), name, kind — and actions: **Place** (enabled
  when the slot has a prefab and no live binding; instantiates at the Scene view pivot with full
  Undo, then binds via the existing `CutsceneSceneBindingUtility` path), **Bind** (object field,
  as now), **Select** (drives `Selection.activeGameObject`), **Frame** (`SceneView.Frame` on the
  bound object).
- `CutsceneSlot` gains `actorPrefab` (`GameObject`; a plain asset reference is `Authoring/`-legal).
  Prop slots get it too — a door prefab places the same way.
- Selection syncs both ways: picking the bound GameObject in Unity's Hierarchy highlights its cast
  row and slot group in the timeline.

### 3.4 Workspace ergonomics

Opening a cutscene keeps the remember/open-scene flow and now also ensures a Scene view exists and
is visible (`SceneView.lastActiveSceneView` or open one), and frames the cast on first preview
enter. No custom layout management beyond that — the owner docks windows once, Unity remembers.

## 4. Decisions (recorded; flag before Task 1 lands if wrong)

- **A58-D1** Preview sampling stays on the editor-side `CutscenePoseSampler`/`ClipSampler` path —
  it does not spin up an ECS world. The parity risk between this and the runtime
  `CutsceneBlobSampler`/`AnimationCommand` path is real and is owned by one EditMode fixture:
  identical clip-block timing inputs (loop phase at a probe time, seam blend weight) must produce
  the same numbers from both implementations' shared math. Extract that math into one place
  (`CutsceneBlockTiming`, plain struct methods) rather than testing two copies against each other.
- **A58-D2** Registry blobs built for preview are per-slot and preview-scoped: enter builds,
  exit disposes, nothing caches across preview sessions. Cutscenes are vignettes (Phase G §1);
  rebuild cost is a non-issue next to the leak risk.
- **A58-D3** Place-in-scene is a normal scene edit (Undo-able, dirties the scene, persists on
  save) — deliberately *not* part of the non-destructive preview capture/restore. The preview
  poses objects; placement creates them. G-D1's restore contract applies only to poses.
- **A58-D4** Auto Key stays out of scope (G3's cut stands — move with the gizmo, press Key). It
  is UX polish; this amendment spends its budget on seeing.
- **A58-D5** (decided while building T4) Facing re-picks a block's clip **only when the block
  already names a member of the slot's direction set**. A block naming the SouthEast walk is
  asking for "the walk", and turning the actor should re-pick the variant; a block naming a
  one-off clip the set has never heard of — a wave, a stumble — is asking for that clip exactly,
  and swapping it for a walk because the actor happens to face north-east would be silent
  nonsense. The mirror flag applies either way: the actor is still facing that direction.

## 5. The queue

1. **A58-T1 — clip-block preview** (§3.1, minus facing): registries, block timing + loop phase,
   seam crossfade, full-pose write to tagged children, sprite tracks. The shared-math fixture
   (A58-D1). *This task alone unblocks the owner.* — **done** (`6c604153`)
2. **A58-T2 — play transport with real holds** (§3.2). — **done** (`bd745080`)
3. **A58-T3 — cast panel + place-from-prefab** (§3.3, the `actorPrefab` field, save/reload proof
   for the new field). — **done** (`9e09b372`)
4. **A58-T4 — facing applied in preview** (§3.1 last bullet) + workspace ergonomics (§3.4).
   — **done** (`79303013`)
5. **A58-T5 — docs**: `Documentation~/cutscenes.md` authoring section rewritten around the
   visual workflow; CHANGELOG. — **done**

Gate per HANDOFF §3 cadence; owner visual pass after T1 (early — the whole amendment exists
because a visual gap survived a green gate) and again at the end.

## 6. Risks

- Sprite-track preview on arbitrary bound hierarchies is the least-proven piece (the Clip
  Editor's own sprite preview goes through its mirror, not scene objects). If it fights back,
  land transform tracks first and record the sprite gap per §1's citation rule — do not hold T1's
  transform preview hostage to it.
- The preview tick now writes many transforms per frame during play; keep per-tick allocations at
  zero (reuse pose buffers) so a 30s vignette doesn't churn the editor.
- `GUIUtility.hotControl` stays untouched from the tick (the A54 lesson — buttons and drags die
  window-wide, symptom looks unrelated).

## 7. What was built, and what it does not do

Every task landed. EditMode 712/712 (709 prior + 3 in `CutsceneBlockTimingTests`), PlayMode
243/243, console clean. Each task was driven live against real scene objects rather than trusted
from a compile — the numbers are in the commit messages.

**Two deviations from §3, recorded with what was checked rather than quietly implemented
differently** (§1's rule):

- **Sprite tracks preview through the material property block, not a `SpriteRenderer`.** §3.1
  said "where the bound child carries a `SpriteRenderer`". Nothing in this toolkit renders a part
  that way: `NewRigPanel`'s own note says every part kind goes through a mesh renderer, "never a
  `SpriteRenderer`", and `SpriteMaterialSystem` publishes frames as the `_ImageIndex` /
  `_AtlasFrame` per-instance properties, which is also how `PreviewRigMirror` shows a frame. The
  preview writes those two properties on the bound child's `Renderer` (capturing and restoring the
  block, so a part that had none is left with none). Verified live: slice 3 → 7 across a sprite
  track, no block left behind on exit. A `SpriteRenderer` path would have been a second sprite
  pipeline with nothing at run time behind it.
- **The facing mirror is applied; the alt-view frame step is not.** `FacingResolver` gives the
  variant and the mirror flag, and both are applied. The remaining half of runtime facing is
  `PartFacing.viewOffset` — which frame a direction shows — and the toolkit bakes that as 0 and
  leaves it to the host (`RigTargetBaker`'s own comment). There is no package-side rule mapping a
  direction to a frame offset, so the preview has nothing to derive one from. Recorded as a gap in
  `Documentation~/cutscenes.md`, not silently skipped.

**Found and fixed while verifying**: the registry rebuild guard compared clip-set references only,
so dragging a clip into a set while the cutscene tab was open left the registry one clip short and
a block naming that clip previewed nothing, with no error anywhere. It now compares the clips
inside each set too. Key edits *inside* a clip are still not compared and do not need to be — they
happen on the Clip Editor tab, and switching tabs exits the preview and drops the registries.

**Still owed: the owner's eyes.** Nothing here has been looked at running against a real actor in
a real scene — which is the whole reason this amendment exists (§1: a visual gap survived a green
gate). The acceptance question is the founding sentence: keyframe, keyframe, walking clip on loop,
and *see* it.
