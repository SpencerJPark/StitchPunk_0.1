# Handoff — DOTS Animation Toolkit

Paste this whole file as the first message of a new chat.

---

You are continuing a sellable UPM package at
`C:\Users\spenc\Documents\GitHub\Stitch_Punk\Packages\com.dotsanimationtoolkit` (version 0.13.0).
Your job is to work down **§4 The queue**, one task at a time, through the gate in §3.

## 1. Read first, in this order

1. `CLAUDE.md` (repo root) — project conventions.
2. `Docs\AnimationToolkit\Phase_F_RigCentric_Spec.md` — the active spec. Its §10 decisions are
   recorded architecture calls, and §12 records three amendments raised while building it (the T6
   rule number, V39, V40). The code is written; **nothing in it has compiled or run yet.**
3. `Docs\AnimationToolkit\Amendment_A55_EventAuthoring_Spec.md` — closed apart from its §4 Task 5
   visual pass.
4. `Docs\AnimationToolkit\Phase_E_TargetTags_Spec.md` — both A55 and Phase F build on its tag
   rules. §4.2.1, §4.2.2, §4.2.3 and §6.1 are owner directives, not suggestions.
5. This file's §5 and §6 — standing owner directives, and what you may not decide alone.

Read `Docs\AnimationToolkit\Phase_D_Ragdoll_Spec.md` §9 only if you touch ragdoll code.
The Phase A/B/C review docs were deleted in the 2026-08-29 cleanup — closed history, recoverable
from git if a decision ever needs tracing. What remains in `Docs\AnimationToolkit\` is live.

**Current state (2026-08-29):** Phase F, A53, A54, A55 and A56 have **compiled and passed the
gate fully green** — the first real compile of any of it, all having been written with the Editor
closed. **Zero compile errors. EditMode 697/697, PlayMode 240/240.** A56
(`Amendment_A56_TimelineBinding_Spec.md`) made the timeline row the binding surface: `tag → part`,
both halves pickers, whole-row tag moves with merge, rig re-tagging from the row, tags mandatory at
track creation, `T `/`S `/`B ` prefixes removed.

The golden hash has been re-recorded, and four fixtures that outlived the change were repaired (see
commit `ad8c90cc` for which and why — one of them, `V24_DoesNotFireForADeclaredRoot`, had been
passing vacuously on an empty list). `Conformance_D` was narrowed under
`Amendment_A57_ConformanceD_HostPathScan.md`: it banned the literal `Assets/`, which is every Unity
project's root and not this game's, so the generated-constants destination could not be spelled. It
now judges the segment after the root, and was re-proved to still catch a planted host path.

**Still outstanding, and not test-visible:** every existing `ActorAuthoring` lost its `clipSet`,
every `ClipSetAsset` its `rig`, every `ClipAsset` its `rig` — all three fields are gone, and Phase F
ships no migration by owner decision. **Re-point each actor's Rig and Clip Sets by hand.** The
migration, smoke and shader-demo folders and all seven toolkit scenes were deleted on 2026-08-29
(owner's call, gate green first), so re-running a builder is no longer an option — recovery is git.
`Assets/ScriptableObjects/Animations/NewClipSet.asset` still carries a serialized `rig:` pointing at
the deleted `HumanoidRig.asset`; that field no longer exists on the type, so it resolves to nothing
and Unity drops it on the next save. `NewRig.asset` beside it is the live rig.

One capability went with the demo folder: **nothing in this project writes `_ToolkitCameraForward`
any more.** The package declares and reads that global but has never written it — by design, it is
the host's job — and `ToolkitCameraBinder` was the only writer here. The toolkit's screen-aligned
billboard mode therefore degrades silently to spherical until a host writer exists. Nothing consumes
it today (the game's own `BillboardSystem` is a separate path), so this is a gap, not a break.

## 2. How to work here

**Write less prose than the surrounding code does.**

- **Doc comments: one or two lines, and only where the *why* is not obvious.** Say why the code is
  shaped this way when a reader would otherwise change it back. Never restate the signature, never
  narrate what the body does, and no multi-paragraph `<remarks>` essays — parts of this package
  have them and they are not the model to copy. If a method needs a paragraph to explain itself,
  the method is wrong.
- **Test only what matters.** The behaviour the feature exists for, and any regression you fix.
  That is the ceiling — roughly two tests per task, often zero for pure UI wiring.
  **Before keeping a test, revert the fix and watch it fail.** If it passes either way, delete it;
  it costs maintenance and proves nothing. A guard clause whose behaviour is obvious from reading
  it does not get a fixture. Watch for `LogAssert.NoUnexpectedReceived()` — Unity fails only on
  unexpected *errors*, never warnings, and it inspects logs received *before* the call, so it is
  the most common way to write a test that cannot fail.
- **Small increments, recompile often.** Sessions here have been cut off mid-edit; one left the
  package uncompilable. Save and gate after each coherent piece, not at the end of a task.
- **Do not spawn subagents.** Three processes driving one live Unity Editor caused MCP lock
  contention that grew `Logs/Editor.log` to 2.2 GB and broke test runs. Work sequentially.

**Hard conventions (from CLAUDE.md — non-negotiable):**

- Never `var`. Never single-letter names. Explicit types; names read like documentation.
- Never `.Run()` a job — `.Schedule()` / `.ScheduleParallel()` into `state.Dependency`.
- **UI Toolkit only** in editor sources. `Conformance_E` bans IMGUI and the test enforces it, so
  `AdvancedDropdown` is unavailable.
- **`Authoring/` must never reference `UnityEditor`** — it ships to players and `Conformance_C`
  scans raw file text including comments. Editor-only machinery lives in the Editor assembly.
- An `EnabledRefRW`/`RO` parameter is named *component name* + `Enabled`.

## 3. Verification gate

Unity MCP only works while the Editor is open. If `mcp__UnityMCP__*` is unreachable, say so and
stop rather than claiming a change compiles.

**Cadence (owner directive, 2026-08-28): the full suites do NOT run after every edit.** The owner
closed the Editor over exactly this — sessions re-running ~700 tests per change. Per edit: steps
1–2 (the compile gate) plus only the fixtures the change touches, by `test_names`/`group_names`.
Steps 3–4 in full run **once, at the commit point** for the task. And stop growing the suite —
most UI wiring gets zero tests (§2).

1. `mcp__UnityMCP__refresh_unity` (`compile: "request"`, `wait_for_ready: true`)
2. `mcp__UnityMCP__read_console` (`types: ["error"]`) — some tests log errors on negative paths
   deliberately; judge by `error CS####` / Burst `BC####`
3. `mcp__UnityMCP__run_tests` EditMode `["DotsAnimationToolkit.Tests.EditMode"]` → poll
   `get_test_job` (`wait_timeout: 90`)
4. `mcp__UnityMCP__run_tests` PlayMode `["DotsAnimationToolkit.Tests.PlayMode"]`
   (`init_timeout: 120000`) → poll (`wait_timeout: 90`)

**Check the discovered total, not just pass/fail.** `resultState: "Passed"` with `total: 0` is the
shape of a suite that silently stopped compiling. Counts must not drop.

**For anything that saves, prove the write persists**: drive the path with
`mcp__UnityMCP__execute_code` against a real asset, save, reload from disk, assert. "The field
displays" is not proof. Delete scratch assets and confirm `git status` afterwards.

## 4. The queue

**A60 + A59-T1 — Cutscene UI overhaul and the in-tab viewport. Built 2026-08-30, gated, smoke-
verified live; THE ACTIVE ITEM IS THE OWNER'S VISUAL PASS.** After the owner judged the A58-era
tab "unusable … pretty bad ui wise", `Amendment_A60_CutsceneUiOverhaul_Spec.md` records the
method (capture the window via `GUIView.GrabPixels` reflection BEFORE and AFTER editing — screen
reads lie under occlusion), the research (Timeline/Sequencer conventions adopted wholesale), and
the changes: A59-T1's scene viewport inside the tab (URP `SingleCameraRequest`, Shot/Free camera,
overlay-hosted scene states), the outliner-style timeline (full-width ruler, accent strips,
merged dead rows, styled blocks/keys), the icon transport with a time readout, and SessionState
restore of the open cutscene across domain reloads (the tab used to come back empty). Its §4
lists the backlog in order: owner's eyes first, then A59-T2 click-select, A59-T3 in-viewport
gizmo, cast/inspector polish, frozen header column. Full suites are owed once at the commit
point — not yet run.

**A59 — Embedded Scene Viewport. Specced 2026-08-30; T1 built same day under A60 (see above);
T2–T4 remain.** The
owner's third correction on the same axis: even with A58's animated preview, the cutscene tab
itself shows no scene — "it needs the scene there to actually work."
`Amendment_A59_EmbeddedSceneViewport_Spec.md` revokes A58 §2.1's embedded-viewport deferral and
records why it was mis-priced twice (both deferrals assumed a preview world; the open scene +
already-posed real objects mean the tab only needs a utility camera rendering into an RT — URP's
`SingleCameraRequest` was verified present in this project's package cache). Tab becomes cast |
viewport | inspector over timeline. Work its §5 queue: T1 render+play (owner's eyes immediately
after, before T2), T2 click-select, T3 in-viewport gizmo, T4 docs. Its §6 names the one
load-bearing unknown to prove first, in isolation, before building layout around it.

**A58 — Cutscene Visual Authoring. Specced and built 2026-08-30; T1–T5 all landed and gated the
same day. THE ACTIVE ITEM IS NOW THE OWNER'S EYES.** The owner's first hands-on with Phase G found
it unusable: clip blocks previewed nothing, so you animated blind.
`Amendment_A58_CutsceneVisualAuthoring_Spec.md` supersedes Phase G §3's "timeline + inspector only"
and G3's preview cuts; its §1 records *why* the gap shipped (a scope cut justified by a false
"needs a real actor bake" claim — `ClipRegistryBuilder.Build` was always callable in-editor, as the
Direction Sets pane proves) and its **§7 records what was built, the two deviations from §3, and
what is still owed**.

What the tab does now: clip blocks play in the Scene view (loop phase, seam crossfades, sprite
frames, part-track overrides, facing variant + mirror), a transport plays the cutscene in-editor
with holds that really hold while the actors' clips keep cycling under them, and a cast panel
places actor prefabs into an empty scene and binds them. `CutsceneBlockTiming`
(`Runtime/Sampling/`) is the one copy of the block-timing rules both the preview and
`CutsceneTimelineSystem` read — do not grow a second.

**Owed, and the only thing left: the owner runs it and looks.** Every task was proved live through
`execute_code` against real scene objects (exact numbers in the commit messages) and the gate is
green — EditMode 712/712, PlayMode 243/243 — but no human has watched a cutscene play. The
acceptance question is the founding sentence: keyframe, keyframe, walking clip on loop, and *see*
it. Do that before treating A58 as closed.

**Phase G — Cutscene Editor. Specced 2026-08-29; G1–G7 all built and gated the same day (owner
directed "do the spec" — read as sign-off).** The `tab-cutscene-editor` placeholder is now a
multi-actor, scene-hosted cutscene timeline (clip blocks + keyframes, camera and event lanes, hold
points) baked to a blob with an ECS runtime player. Full owner Q&A and the G1–G7 build order are in
`Phase_G_Cutscene_Spec.md` — §2–§4 are owner product calls, §7 the recorded delegated decisions
(nine now, G-D1 through G-D9, the last four decided while building rather than up front — see each
phase's own entry below for why). **Feature-complete, gated, undemoed** — see G7's own line for
what that last word means before treating this as shippable.

**G1 — data model — done.** `CutsceneAsset` (`Authoring/Assets/CutsceneAsset.cs`): named
actor/prop slots (stable 32-bit `slotId`, same generator RigAsset rows use), clip blocks, a
`CutsceneTransformKey` (a `TransformKey` reshaped to absolute seconds rather than a clip's
normalized duration fraction — a cutscene's length is elastic, so nothing here does
`time * frameRate` math), facing-angle override keys (decision G-D3), tag-addressed per-part
keyed tracks (no raw-target-id fallback — a slot can be recast to a different rig, spec §5), a
camera lane (keys + cut markers), an event lane (`CutsceneEventMarker` is a class, not a struct
like `EventMarker`, specifically so its `fireOnSkip` field initializer can default it *on*, per
G-D4), hold markers, and per-scene string-keyed GameObject bindings (Conformance_C: `Authoring/`
stays `UnityEditor`-free). **G-D5 decided**: hold ids are a plain `string`, not an
`IVocabularyRegistry` vocabulary — nothing resolves a hold id against a shared, dense-indexed
vocabulary the way a tag or event key is; a host compares it for equality exactly once, against a
control component it wrote itself, so the registry machinery (dropdown-only selection, duplicate
guard, codegen) would be pure overhead. Compile gate green, zero errors/warnings. Proved via a
real disk round-trip (`execute_code`, scratch asset created/saved/reloaded/deleted, every lane
type checked, `git status` clean after) rather than an in-memory fixture — the class this
package's own lessons (§9 point 4) warn an in-memory suite cannot catch.
`EnsureStableIds`/`RigAsset`'s exact "public because code-built assets hit no lifecycle hook
between populating a list and saving it" contract carries over unchanged.

**G2 — the tab — done.** `Editor/ClipEditor/Cutscene/`: `CutsceneEditorPanel` replaces the
placeholder in `ShowCutsceneTab`, wired the same way `NewRigPanel`/`DirectionSetsPanel` are (a
cover pane over the dock, built on first show, never torn down). Slot headers double as the slot
list (no separate list view); selecting one, a clip block, or any lane marker drives the right-hand
inspector, which is `PropertyField`s bound to a `SerializedObject` throughout — every add/move/
resize/delete is a real Undo step. New seconds-based `CutsceneTimelineGeometry`/
`CutsceneTimelineRulerElement`/`CutsceneTimelinePlayheadElement` sibling the Clip Editor's own
normalized-time versions (G-D2: elastic length has no duration to normalize against) rather than
reusing them. Two reusable lane elements — `CutsceneMomentLaneElement` (any point-in-time list:
root/facing/part-track/camera/event/hold) and `CutsceneClipBlockLaneElement` — serve every lane
kind; drag is visual-only until release, one `SerializedProperty` commit per drag. Scene remember/
open/warn flow and per-scene `GlobalObjectId` bindings are `CutsceneSceneBindingUtility`
(editor-only; the asset itself still carries only strings). Double-clicking a `CutsceneAsset`
opens it via `CutsceneAssetOpener`, mirroring `DirectionSetAssetOpener`.

**v1 scope cuts, recorded rather than silent** (see the panel's own class remarks): the header
column scrolls horizontally with the lanes instead of staying frozen (dual synced-scroll columns
are real work a first pass skipped); one item drags at a time, no box-select, no multi-key drag;
an "add" always inserts a bare default (zero position, identity rotation, scale one, 60° FOV) and
leaves filling it in to the inspector, never a captured live pose — that capture is G3's non-
destructive scene-view preview, not built yet. None of these are correctness gaps; every
add/move/resize/delete the spec calls for works and was proved live against a real window (see
below), not just compiled.

Compile gate green, zero errors/warnings. No dedicated test fixtures — this is UI wiring
(HANDOFF §2's "often zero tests" case) — but every mutation path was proved against a real,
on-screen `ClipEditorWindow` via `execute_code` + reflection rather than trusted from a read: two
slots added and id-minted, a clip block added/resolved, transform keys inserted with correct
defaults and re-sorted on insert, delete, remove-slot, and a scene binding set/found/resolved/
unbound round-trip — all through the exact private methods the UI calls, with the window actually
open. Every selection branch (both slot kinds, every lane kind, camera/event/hold) was driven
through `SelectItem`/`SelectSlotHeader` and rebuilt the inspector with no exception. `git status`
clean after (scratch assets deleted).

**G3 — Scene-view preview + keying — done, scoped down from the full spec (recorded, not hidden).**
`CutscenePreviewController` (new): non-destructive scrub posing per decision G-D1 — entering
preview captures every bound GameObject's (and every bound `RigTargetAuthoring` child's) local
transform, scrubbing writes root motion + part-track overrides onto the *real* scene GameObjects
(never a mirror, unlike the Clip Editor's own `PreviewRenderUtility` preview), and exiting restores
every capture exactly. `CutsceneEditorPanel` activates/deactivates it the moment the current scene
does/doesn't match `sceneGuid` (`SyncPreviewActivation`, called from every rebuild), and exits it on
tab-hide (`ShowCutsceneTab` now calls `panel.OnHidden()`), on `EditorSceneManager.sceneSaving`
(G-D1's "must never survive into a saved scene"), and on `DetachFromPanelEvent`/loading a different
cutscene. Selecting a slot or a part track also sets `Selection.activeGameObject` to the resolved
GameObject/child — because preview poses real scene objects, Unity's own Move/Rotate/Scale gizmo
already works on it, so **no custom gizmo drawing was needed at all**, which is most of why G3
landed lighter than the spec's own "hardest one" risk note. A "Key" toolbar button reads the
selected slot's (or part track's) *live* transform at the playhead and upserts a
`CutsceneTransformKey` via `SerializedProperty` (`CutscenePreviewController.TryKeyRoot`/
`TryKeyPartTrack`, overwrite-within-1/120s rather than duplicate). `CutscenePoseSampler` (new,
editor-only) interpolates `CutsceneTransformKey`/`CutsceneCameraKey` lists directly — reusing
`ClipSampler.Ease` for the actual easing math rather than reimplementing it — because there is no
baked blob to sample from until G5. A slot inspector shows the resolved facing angle at the
playhead (`CutscenePoseSampler.TryResolveFacingAngle`: last override key at-or-before the playhead,
else derived from a finite-difference of root position) as a **read-only number**, not an actual
sprite flip — flipping needs the baked runtime pipeline that does not exist before G6.

**Scoped out of G3, recorded as owed, not silent:**
- **Clip-lane playback is not previewed.** What a clip block would show at this instant needs
  `ClipSampler` against a baked `ClipRegistryBlob`, and there is none until G5; scrubbing moves the
  root and any keyed parts, but an actor's clip-driven parts just sit at their scene rest pose.
- **No Auto Key.** Only the manual "Key" button exists — continuous polling for live gizmo drags
  risks a feedback loop (`ApplyPose` writing the very transform being watched for a change) that
  was not worth the risk this pass. The interaction is: move with Unity's gizmo, press Key.
- Facing preview is a number, not a visual flip (above).

Verified live: built a rig + tagged rig-target child + cutscene in memory (no disk writes needed —
`CutscenePreviewController` takes a scene GUID string and a `GlobalObjectId`, neither cares whether
either side is saved), entered preview, sampled root motion and a channel-masked part track at t=0
and t=1 and checked exact numbers (including that an *unmasked* channel — z position — stayed at
its captured rest value rather than leaking the sampled value), exited and checked exact restore,
then drove `TryKeyRoot` and confirmed the written key's time and position through the real
`SerializedObject`. Compile gate green, zero errors/warnings.

**G4 — camera lane — done.** Keys, cut markers and Align-to-Scene-View authoring already existed
from G2/G3; G4 added the forward direction — **scrubbing now moves the Scene view's own camera** to
the cutscene's shot (`CutscenePreviewController.ApplyCameraPose`, gated by a new "Preview Shot"
toolbar toggle, default on). Cut markers are cut-aware per new decision **G-D7**
(`CutscenePoseSampler.SampleCameraWithCuts`): a marker splits the lane into independent
interpolation windows rather than blending across it, holding the last key before a window opens
if the window owns no key of its own. Placing the Scene view camera at an exact world position
(rather than orbiting a pivot, which is all `SceneView.LookAt` natively does) needed the pivot
solved backwards: **confirmed empirically against this Editor version** (not from memory/docs —
`SceneView`'s camera-distance-from-`size` relationship is undocumented and version-sensitive),
`cameraDistance = size / sin(fov · 0.5)`, then `pivot = position + rotation · forward ·
cameraDistance`; `size` itself is arbitrary since only the ratio matters. **A second, real trap
found live**: `SceneView.camera.transform` does not refresh synchronously after `LookAt` — it
updates on the view's own repaint, so a caller reading it back immediately (as a test, or as any
code that does not yield a frame) sees the *previous* pose. `ApplyCameraPose` now calls
`sceneView.Repaint()` itself so this is never visible from outside the class.

Verified live, both pieces: the cut-window math (four probed times across two windows split by one
cut, including the exact instant of the cut itself and its `isCut` flag) via direct reflection
calls, and the actual Scene view placement (build a camera key, drive `ApplyCameraPose`, read
`sceneView.camera.transform`/`cameraSettings.fieldOfView` back on the *next* tool call) — position
and rotation landed with zero error, FOV read back exactly. Compile gate green, zero
errors/warnings.

**G5 — bake (CutsceneBlob) — done.** `Runtime/Blobs/CutsceneBlob.cs` (new blob types) +
`Authoring/Build/CutsceneBlobBuilder.cs` (beside `ClipRegistryBuilder`). Carries no clip registry
of its own — a clip block's `clipId` resolves at play time against whichever `ClipRegistryBlob` the
*bound actor* already carries from its own actor bake ("rides the same registry blobs the actors
already use", spec §5), which is what keeps the runtime player (G6) a consumer of existing playback
machinery rather than a second pipeline. One enum, `CutsceneSlotKind`, had to move from `Authoring`
to `Runtime/Components/AnimationToolkitEnums.cs` mid-task — the blob needs it and `Authoring` nests
*inside* the `DotsAnimationToolkit` namespace precisely so it can see runtime enums, never the
reverse; this is the same reason `TargetKind`/`AnimTechnique` already live there.

**Decision G-D8** (recorded in the spec): a clip block is assigned to exactly one segment, by its
start time, and is *never* clipped across a hold even when its authored span crosses one — the
segment split makes elastic time containable for lookups, it does not describe playback itself.
Splitting a looping block at every hold and re-describing the remainder would restart its loop
phase at each release, which is exactly the "pop back to frame 0" spec §2's "looping clips keep
cycling" forbids. Every other lane item (a key, a cut, an event) is a single instant and is bucketed
by its own time under the same half-open-interval rule (`AssignToSegment`): segment *i* owns
`[boundary[i], boundary[i+1])`, except the final segment, which is closed at both ends.

Verified live against two real bakes (`execute_code`, no disk writes needed — a `BlobAssetReference`
is disposed in the same call): (1) a hold at t=2 with a clip block `[1, 4)` spanning it, transform
keys either side, a part track with a resolvable tag and one with an unresolvable one, and an event
authored exactly at the hold's own time — checked segment count/durations/hold ids, that the clip
block landed whole (unclipped, duration still 3) in segment 0 with its start correctly rebased, that
the boundary-time event landed in segment 1 (the segment that *opens* there) rebased to 0, and that
exactly two warnings fired (the unresolved clip id, the unresolved tag id) — zero false positives
from the resolvable references. (2) A no-hold cutscene bakes to exactly one segment spanning the
full content. Compile gate green, zero errors/warnings.

**G6 — runtime player — done.** `Runtime/Components/CutsceneComponents.cs` (`CutscenePlay`,
`CutsceneControl`, `CutscenePlaybackState`, `CutsceneActorBinding`, internal
`CutsceneSlotRuntimeState`, `CutsceneHoldRelease`, the `CutsceneCameraPose` singleton),
`Runtime/Sampling/CutsceneBlobSampler.cs` (the Burst-jobbable twin of the editor's
`CutscenePoseSampler`), `Runtime/Api/CutscenePlaybackApi.cs` (`CreatePlayRequest`, `RequestSkip`),
and two systems: `CutsceneTimelineSystem` (`AnimationToolkitLogicSystemGroup` — time, clip-block
Play commands through the *existing* `AnimationCommand` API, root/prop transforms, the camera
singleton, events, hold pause/release, skip) and `CutscenePartOverrideSystem`
(`AnimationToolkitPresentationSystemGroup`, `UpdateAfter(TransformSampleSystem)` /
`UpdateBefore(TransformApplySystem)` — the per-part Override layer, writing `TargetPose` in the one
window before it reaches a renderer). Neither is `[BurstCompile]` or job-scheduled: a handful of
cutscenes run at once, nothing like the per-part sampling hot path, and the logic reaches across a
cutscene's own state, every bound actor's buffers, and one world camera singleton in a way one
`IJobEntity` query cannot express — plain `SystemAPI` calls in `OnUpdate` are not the banned
`.Run()` pattern (there is no job object here to call it on).

**A real architecture gap surfaced while wiring this up, fixed rather than routed around**: the
baked `ClipRegistryBlob` has no tag→dense-index map at runtime (a clip's own tag-bound tracks are
resolved to a `targetIndex` once, at the *clip's* bake, and the map itself is never carried
forward) — so a cutscene part-track's tag has nothing to resolve against for an arbitrary bound
actor. Rather than extend `ClipRegistryBlob`/`ClipRegistryBuilder` (heavily gated, cross-cutting,
outside this phase's blast radius) or invent a second resolution path, **decision G-D9** resolves
the tag to a dense index once, at *cutscene* bake time, against the slot's own rig, using the exact
canonical (ascending stable id) ordering `ClipRegistryBuilder` already uses — so it agrees with
whatever index the bound actor's own `RigPartRef` buffer assigns, with no changes to either. The
honest cost, recorded in the spec: recasting a slot to a different rig is honored live by the G3
editor preview but not by the baked runtime path, which needs a rebake until a follow-up amendment
gives the runtime registry its own tag map.

**A second correctness fix landed alongside it**: both pose samplers (editor and runtime) were
slerping rotation between two keys through quaternions. `ClipSampler`'s own remarks say exactly why
that is wrong for this package — a slerp takes a different path between the same two keys than the
per-component Euler lerp the curve editor shows, so the preview would have quietly disagreed with
what plays. Both `CutscenePoseSampler` and `CutsceneBlobSampler` now lerp Euler components and
convert to a quaternion once, only where the final consumer needs one (a root's `LocalTransform`,
a camera pose) — never where the consumer already wants radians (`TargetPose.rotation`), matching
`TransformApplySystem`'s own "convert at the last step" shape exactly.

**Skip/play-through parity — verified by a real PlayMode test**, which is the test HANDOFF called
out in advance as the one that matters: `Tests/PlayMode/CutsceneTimelineSystemTests.cs` builds a
`CutsceneBlob` by hand (matching this suite's existing convention of never bake-testing through
PlayMode fixtures), runs one to completion by advancing world time in five 0.5s steps and a second
by requesting an immediate skip, and asserts the final root `LocalTransform.Position` and fired
`AnimEventOutput` count are identical between the two — not merely close. A second test confirms a
skip actually stops the actor's clip layer (the "release the actors" half of spec §6's end/skip
contract). Both pass. The full suites were run once at this commit point (HANDOFF §3's own cadence)
and caught two real `PackagingConformanceTests` violations before they could ship: **Conformance_C**
flagged the literal substring `UnityEditor` inside two doc comments in `CutsceneAsset.cs` explaining
why the type is *not* referenced there (the scanner reads raw text, comments included — the same
trap A57 already documents); **Conformance_D** flagged `Authoring/Assets/CutsceneAsset.cs` in
`CHANGELOG.md` as a "host asset folder path" (the pattern is `Assets/` + an identifier, and
`CutsceneAsset` matched it same as any host folder name would). Both were reworded, not suppressed.
Final state: EditMode 709/709, PlayMode 242/242 (240 prior + the 2 new fixtures here), console
clean.

**Not built, and out of scope for this phase** (spec §8 already excludes some of these; the rest
are this phase's own honest cuts, all recorded above at the decision that caused them): Auto Key in
the editor preview; a visual (sprite-flip) facing application at runtime — only a read-only
resolved-angle number in the editor and no runtime-side facing at all, because nothing in this
package drives facing outside host movement code for it to hook into; recast-to-a-different-rig
support for the baked runtime path (G-D9); a frozen (non-scrolling) timeline header column.

**G7 — prop slots + docs — done. Phase G (G1–G6) is feature-complete.** Prop slots turned out to
already work end to end — they were built into the data model, editor, preview, bake and runtime
player incrementally since G1, gated by `CutsceneSlotKind.Prop` at every layer, rather than bolted
on at the end. G7's real work was closing the one untested path: a dedicated PlayMode fixture
(`PropSlot_WithNoRigOrCommandBuffer_StillGetsRootMotion`) proving a Prop entity with no
`AnimationCommand`/`RigPartRef` at all — just a transform — still gets root motion through the same
`ApplyPose` path an Actor slot uses. `Documentation~/cutscenes.md` (new, linked from `index.md`)
covers the concept model, authoring workflow, scene binding/preview, baking, the runtime API, and
recorded gaps in the same voice the rest of `Documentation~/` uses. EditMode 709/709, PlayMode
243/243 (240 prior Phase F baseline + 2 from G6 + 1 Prop fixture here). Compile gate green.

**Owed before this closes for real: the owner's eyes on a live cutscene**, same as every other
visual surface in this package (HANDOFF §8's own standing note) — nothing here has been looked at
running against a real actor in a real scene. Nothing remains on the Phase G queue.

**Clip Editor tabs + viewport overlay — landed 2026-08-29, owner visual pass owed.** The top bar
gained five exclusive tabs — `tab-clip-editor`, `tab-cutscene-editor` (a placeholder pane that says
so), `tab-new-rig`, `tab-direction-sets`, `tab-vat-bake` — sitting beside the clip set and rig
fields, which stay on the bar. `SetActiveTab(ClipEditorTab)` is the single writer of both the enum
and every toggle's lit state, so never call a `Show…Tab` method directly. Billboard, Ragdoll and new
Move/Rotate/Scale buttons moved off the bar into `viewport-overlay`, a frame-sized
`picking-mode: Ignore` column inside `viewport-frame` — the controls that only mean anything while
looking at the 3D area. Four things to know before touching it:

- **The overlay container must stay frame-sized.** The findings panel's `max-width`/`max-height` are
  percentages resolving against it — shrink it to hug its rows and 60% of the viewport becomes 60% of
  a toolbar. It absorbed the old `validation-overlay-slot`, which is gone.
- **`UpdatePreview` is gated on `activeTab == ClipEditor`.** The 2D Direction Sets pane drives the
  *same* `ClipPreviewController` into its own `Image`, and one `PreviewRenderUtility` cannot serve
  two viewports in a frame.
- **The direction pane borrows the camera and gives it back** (`OrbitYaw`/`OrbitPitch`, new
  properties). `FrameRig()` sets focus and distance and does **not** touch the angles, so without
  this the Clip Editor's orbit follows you into a viewer that is supposed to be fixed front-on.
- **`SetGizmoMode` is the single writer of `gizmoMode`**, called by both W/E/R and the three buttons.

`VatBakePanel` gained a bound mode (`SetSource`) rather than losing its Clip Set and Rig fields:
`VatBakeWindow` is its second host and would otherwise have had no way to say what to bake.

**2D Direction Sets — new pane, landed 2026-08-29, owner visual pass owed.** The package gained
`DirectionSetAsset` (`Authoring/Assets/`, promoted out of the host game) and the Clip Editor gained
a third cover pane beside VAT Bake and New Rig, driven by the `direction-sets-toggle`
`ToolbarToggle`. Code in `Editor/ClipEditor/DirectionSets/`. What it is: a clip queue over a
direction set's five east-side slots, one viewport, and a 0–360° slider that turns the character
through `FacingResolver` — the runtime path, not an imitation of it. The one design point worth
knowing before touching it: **there is exactly one `ClipPreviewController` over one synthetic
`ClipSetAsset` holding every clip in the set**, so changing facing is a different `clipId` into
`SamplePose` and never a registry rebuild. Rebuild only on clip membership or rig change — the
guard in `RebuildRegistryIfClipsChanged` is what keeps a slider sweep from hitching. Hosts feed it
units through `IDirectionSetContextProvider`; with no provider registered the Unit Context dropdown
hides and the pane works standalone. **Owed: the owner's eyes on it** — the acceptance cases are in
`Assets/_Vault/Tasks/Verification/verify-directionsetspanel.md`. `DirectionSetCoverageTests` and
the two new `ClipEditorLayoutTests` cases are green.

**A56 — the timeline row is the binding surface.** Written 2026-08-28, never compiled. Tasks and
decisions in `Amendment_A56_TimelineBinding_Spec.md`; its §5 lists the verification owed: compile
gate, the two `TimelineBindingLogicTests` fixtures, save/reload proof for auto-tagging and merge,
then the visual pass (owner's eyes). Verify it together with Phase F below — one Editor session
covers both.

**Phase F — rig-centric binding.** F1-F6 are written and the gate is green (§1). What is left is
the part no test can reach:

1. ~~**Run the gate in §3.**~~ Done 2026-08-29 — see §1.
2. **Prove the Clip Editor's rig survives a domain reload and a re-dock**, and that it does *not*
   reach any asset. It is window state now (`activeRig` / `sessionRig` / `CarriedState.rig`), so the
   failure to look for is the rig coming back empty after editing a tag — and the other failure is
   swapping the open clip set changing the rig, which is the bug the owner reported.
3. **Run the Clip Editor.** Spec §13: ~40 `clipSet.rig` sites moved, mechanically. §9 lesson 2
   applies — read nothing, run it.
4. **Compile-check `Samples~`** via a temp assembly; both builders were rewired, and Samples~ is
   excluded from Unity compilation so it rots silently.
5. Then commit, task by task, staging paths explicitly.

**A55 — event authoring reaches tag parity.** Full task definitions in
`Amendment_A55_EventAuthoring_Spec.md` §4; §3 is the gap table each task closes.

1. **Task 1 — Add Event opens the picker.** Done: `OpenAddEventPicker`/`AddEventAtPlayhead(uint)`
   split, `InsertKey` gained `explicitEventKey`, `ResolveNewEventKey`/`FindFirstRegistryEntry`
   deleted, `ClipEditorTransport` rebound.
2. **Task 2 — Event registry rows match tag registry rows.** Done: `AnimEventKeyRegistryEditor`
   hand-builds rows; new `AnimEventBindingUtility`.
3. **Task 3 — Lane headers become an authoring surface.** Done, minus lane ordering — cut per the
   spec's own standing decision (`ClipKeyClipboard` threading was judged not worth it; lane
   position carries no meaning, only the label does).
4. **Task 4 — Docs.** Done: `Phase_B_Architecture.md`, `CHANGELOG.md`, `animation-events.md`,
   `clip-editor.md`, this file.
5. **Task 5 — Visual pass.** **Not done — no live Editor session was available.** The owner must
   run the eight-step pass in the spec's §4 Task 5 before this closes. Do the verification gate
   in §3 of this file first; nothing in A53/A54/A55 has had a real compile yet.

Work top to bottom. Commit each task separately (stage paths explicitly; never `git add -A`).

## 5. Standing owner directives — binding, do not lose

- **Names, never numbers**, in downstream game code and in every editor surface. Game code uses
  generated constants (`TargetTags.Jaw`, `AnimEvents.Footstep`). Sole exception: an unresolvable id
  after a delete. (§4.2.3)
- **"I shouldn't have to manually assign any assets for this."** Both vocabularies auto-create under
  `ProjectSettings/`. No asset creation, no wiring.
- **The tag and event lists are editable from the clip editor.**
- **The tag is a keyed track's one identifier (2026-08-28, A56).** A keyed row with no tag must be
  impossible to create; track creation tags the part automatically. "(no tagged part)" remains a
  legal display state — a clip may name a tag the open rig doesn't wear — but "(untagged)" may not
  come back. Kind prefixes (`T `/`S `) stay gone from row names.
- **New rigs are created fresh — build no migration paths** for old rigs with empty fields.
- **Events are authored loosely; downstream systems read and redirect.** They will eventually drive
  sound, ragdoll triggers, damage, shader alt-views and dialogue. **Do not build downstream
  consumers unless asked.**
- **T2 is lenient (§6.1):** a tag-bound track whose tag is absent from the rig is *skipped with a
  warning*, not an error, so one clip covers a roster of differing rigs. Safe only because of the
  three mitigations — dropdown-only selection, the case-insensitive duplicate guard, and a warning
  naming clip + track + tag + rig that surfaces in the validation badge.
- **T3 stays an error**, reported differently from T2: a tag missing from a *rig* is a roster fact;
  a tag missing from the *registry* is a dangling reference.
- The owner eventually wants to hear sound while scrubbing. Note the Clip Editor's scrub path poses
  through `ClipSampler` and never runs `EventEmissionSystem`/`EventWindowSystem` (ECS, play-time
  only), so that needs its own crossing detection comparing playhead-before against playhead-after.
  **Not on the queue — do not start it.**

## 6. Do not decide these alone

- **The bone-reparent guard.** Hierarchy drag-to-reparent permits dragging skinned bones;
  `RigStructureEditor.ValidateReparent` guards cycles and self-parenting only.
  `worldPositionStays: true` makes the drag look fine, and the corruption surfaces later with no
  error — when the clip plays, or baked into every VAT frame. `ClipPreviewController.IsSkinnedBone(int)`
  is public and makes the guard cheap. Recommendation on file: guard it. The owner asked for
  hierarchy dragging, so excluding bones is their call.
- **Q1 — the Spatial3D twist axis.** `twistLimitDegrees` has no defined axis; D2 provisionally used
  the child's rest-local +Y. Owner said "ignore for now"; it blocks D8 only.

If you hit a spec/reality conflict, **escalate a written amendment** — never quietly edit a doc so
it agrees with your code. That habit sank three earlier gates.

## 7. Known gaps and caveats

- `ClipEditorWindow.CountTracksForTarget` matches by raw `targetId`, so it **undercounts tag-bound
  tracks** in delete confirmations. `ClipSpriteEditing.CollectTracksForTarget` shares the flaw but
  has no production callers.
- The ragdoll preview derives `restRelativeRotation`/`parentAnchorOffset` from the on-screen pose,
  not the authored rest pose, so toggling it on mid-animation can show a first-frame limit
  correction the runtime would not produce.
- The Unity Physics probe casts along gravity only — a wall a body drifts sideways into is missed.
- `PreviewRigMirror` has no notion of `HierarchyPath`; such addresses do not resolve on a
  pure-cutout preview.
- The ±45° default hinge limits on ragdoll bodies were invented by an agent and never judged by eye.
  Prime suspect if a drop looks wrong.
- Restart the Editor to rotate `Logs/Editor.log` if it has grown huge.
- `Docs/AnimationToolkit/shader-contract.md` and `Documentation~/shader-contract.md` are near-identical
  mirrors that differ only in host-specific paths. They will drift; neither is marked authoritative.

## 8. Not yet judged by eye

Test-clean but visually unverified, because the owner has been away from the PC: the ragdoll drop,
Rig Edit gizmos, the New Rig wizard, the amber event pin shape, and event stacking. Anything
on-screen needs the owner to look — never report a visual result as verified.

## 9. Lessons this package keeps re-teaching

1. **Closure is a property of the code, not of the note saying the code changed.** Verify against
   the shipped tree — never a CHANGELOG, a review doc's closure table, or a previous session's
   summary. Two of three reworks asserted closures the diff did not contain.
2. **Reading the diff is not enough either. Run the thing.** The worst bug found here was invisible
   to three independent static reviewers and took ninety seconds of execution to surface.
3. **Features have repeatedly shipped with tooltips and docs describing behaviour that did not
   work**, passing tests that checked wiring existed rather than that the feature did what it
   claimed. Every toggle that writes a pose must be asked: does it un-write?
4. **A suite that builds all its inputs in memory has no coverage of the serializer** — and the
   serializer is part of the authoring contract. Two shipping-blocking defects appeared the instant
   something built real, saved assets instead of fixtures.
