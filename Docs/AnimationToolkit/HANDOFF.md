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

**Phase G — Cutscene Editor. Specced 2026-08-29; G1 built and gated the same day (owner directed
"do the spec" — read as sign-off).** The `tab-cutscene-editor` placeholder becomes a multi-actor,
scene-hosted cutscene timeline (clip blocks + keyframes, camera and event lanes, hold points) baked
to a blob with an ECS runtime player. Full owner Q&A and the G1–G7 build order are in
`Phase_G_Cutscene_Spec.md` — §2–§4 are owner product calls, §7 the recorded delegated decisions.

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
between populating a list and saving it" contract carries over unchanged. **G2 — the tab — next.**

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
