# Handoff — DOTS Animation Toolkit

Paste this whole file as the first message of a new chat.

---

You are continuing a sellable UPM package at
`C:\Users\spenc\Documents\GitHub\Stitch_Punk\Packages\com.dotsanimationtoolkit` (version 0.17.0).
**§4 The queue is currently empty** — the cutscene roadmap that occupied it for several sessions
closed with A68. Work whatever the owner raises next through the gate in §3.

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

**Current state (2026-09-07):** cutscenes are a shipped feature (v0.15.0) — see
`Documentation~/cutscenes.md` for the concept model and authoring workflow and
`Documentation~/cutscene-api.md` for the full runtime/authoring member reference. `Samples~/Cutscene`
is a compile-checked host sample. The roadmap that built this (`Assets/_Vault/Tasks/NewPlans/Cutscene_Roadmap.md`,
amendments G0 through A69) is closed; §4 is empty. `CHANGELOG.md`'s `## [0.15.0]` section is the
one-paragraph-per-item summary of everything that shipped.

**0.16.0 shipped the actor profile (Amendment A70, T1-T10 all landed and gated)** — layers moved off
the rig onto `ActorProfileAsset`, animations are played by name (`PlaybackApi.PlayAnimation`), a
directional entry resolves per-facing, and ragdoll can be triggered from an animation; see
`Documentation~/actor-profiles.md`. Next: **Amendment A71**, the Actor Editor tab
(`Docs/AnimationToolkit/Amendment_A71_ActorEditor_Spec.md`).

Two things carried forward from the pre-cutscene era, still true: **every existing `ActorAuthoring`
still needs its `clipSet` re-pointed by hand** where Phase F's migration-free field removal (2026-08-29)
was never followed by a bulk fix — `MaleCitizen` (the game's first toolkit actor, built for G0) is
re-pointed and working; `PlayerUnit`/`BaseUnit`'s separate body-part tree is not. And the package's
screen-aligned billboard mode still needs a host-written `AnimationToolkitCameraData` singleton
(`BillboardResolveSystem`'s `RequireForUpdate`) to do anything — the game now has one
(`AnimationToolkitCameraBridge.cs`, `Assets/_Scripts/MonoBehaviours/Managers/`, added while verifying
G3's acceptance cutscene), but that is a game-side fix, not a package one; a project with no such
writer still gets silent spherical billboarding with no warning.

## 2. How to work here

**Write less prose than the surrounding code does.**

- **Doc comments (Amendment A69, replaces the older one-or-two-line rule verbatim):** One
  `<summary>` per file, on the file's primary type, at most three lines, stating what the type is
  and the one contract a caller must know. Nothing else in the file gets a `<summary>` unless the
  name cannot carry it. No `<remarks>`. No `<para>`, `<strong>`, `<em>`, `<list>`. No citations of
  the architecture doc, amendments, phases or spec sections — customers do not have those
  documents. A `<param>` only for a sentinel or a unit (`NaN = clip default`, `seconds`, `−1 =
  none`). A field comment only for a sentinel, a unit, or an ordering/aliasing trap, as **one**
  line. An inline `//` only for a *why* the code cannot express — a trap, an ordering constraint, a
  reason a reader would otherwise "fix" — at most two lines. If a method needs a paragraph, the
  method is wrong: split it or rename it. Enforced by `Conformance_F` in
  `PackagingConformanceTests.cs`.
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
- **Static-class suffixes: §2.1 of Amendment A69.** One suffix per role — `Api` (the public surface
  a host game calls, `Runtime/Api/` only), `Builder` (bake-time authoring→blob), `Sampler`
  (pure blob+time→pose), `Resolver` (pure id/tag/angle→index), `Math` (pure numeric functions),
  `Validation` (rule checks), `Utility` (editor-only asset surgery, `Editor/ClipUtilities/` only),
  `Editing` (editor-only clip-editing operations). Banned anywhere: `Util`, `Utils`, `Helper`,
  `Helpers`, `Query`, `Manager`, `Common`, `Misc`, `Ext`, `Extensions`. A static class that is none
  of these roles is named as a plain noun for the thing it models. Enforced by `Conformance_G`/
  `Conformance_H`.

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

**Queued (2026-09-08): Amendment A74 — Preview Viewports.** Spec
`Docs/AnimationToolkit/Amendment_A74_PreviewViewports_Spec.md`, session prompt
`Amendment_A74_PreviewViewports_Prompt.md`, index
`Assets/_Vault/Tasks/NewPlans/PreviewViewports_Roadmap.md`. The Actor Editor viewport gets the
Clip Editor's camera (orbit, pan, look + fly, dolly, zoom, F, double-click reset) and its rail
minus Move/Rotate/Scale; the VAT Bake panel gets a live viewport that plays the baked
`runtimeMesh` through the shipped VAT graph with a transport, clip picker and a translucent
source ghost, plus a Sample Tentacle button because the project has no VAT-bound content. The
gesture state machine leaves `ClipEditorWindow.CameraNavigation.cs` for a shared
`PreviewCameraNavigation` over an `IPreviewCameraRig`. Independent of A73; takes the next unused
minor. Eleven tasks in four subagent waves, one ⏸ owner checkpoint at the end. Nothing built.

**Queued (2026-09-08): Amendment A73 — Profile-Driven Cutscenes**, then the game plan **G6**.
Spec `Docs/AnimationToolkit/Amendment_A73_ProfileDrivenCutscenes_Spec.md`, session prompt
`Amendment_A73_ProfileDrivenCutscenes_Prompt.md`, game half
`Assets/_Vault/Tasks/NewPlans/CutsceneProfileCutover_System.md`. The owner found the Cutscene
Editor still on raw clip ids, a per-slot direction set and one request-wide layer while A70/A71 put
layers, names and direction on the profile. A73 makes a slot a profile: one timeline row per
profile layer with blocks by animation name and ■ stop keys, auto locomotion (the profile's
standing/moving entries from real displacement), facing that is auto unless a Fixed key pins it
(an Auto key hands it back; a mark's arrival facing latches), the cutscene writing `ActorFacing`
(A73-D2 amends A70-D6 for cutscene-driven actors), marks with a **+** button and *Wait Until
Reached*. Breaking, 0.19.0, schema 6. Two Sonnet sessions (T1–T3 runtime, T4–T8 editor + docs), the
owner checkpoint at the end of the second; G6 re-points the game's seven cutscene assets by script
and gates `UnitAnimationAssignmentJob` on `CutsceneActor`. Nothing here has been built.

**The Actor Editor roadmap is built (2026-09-07): A70 (0.16.0), A71 (0.17.0) and the game cutover G5
are in, gated green (EditMode 823 / PlayMode 291; the one standing `Conformance_A` asmdef drift
remains). Three ⏸ owner checkpoints are open and nothing is queued behind them:**

- **A71-T10** — open `Assets/ScriptableObjects/Animations/MaleCitizen.profile.asset` (double-click)
  and walk the spec's §5 T10 list; the layout is the owner's to change.
- **G5-P10** — `TestArea.unity`, Play: idle sway, blink, walk, a rotter's punch via
  `DebugZombifyMenu`, a death with the death face + ragdoll, a resurrection; F9 cutscenes still play
  (on the top layer now).
- **RG-T4/T7/T10** (`Assets/_Vault/Tasks/NewPlans/RagdollTuning_System.md`) — judge the eleven
  bodies' hinge limits in the Clip Editor's Ragdoll preview, then the in-game drop and launch feel.
  One toolkit question from the machine sample: a settled ragdoll never flags `Sleeping`.

Index: `Assets/_Vault/Tasks/NewPlans/ActorEditor_Roadmap.md`. Both content recipes live in
`Assets/_Scripts/Editor/ContentAuthoring/` and are re-runnable.

**Built (2026-09-07): Amendment A72, editor visual unification — 0.18.0.** T0–T12 are in
(commits `A72-T1/T2`, `A72-T3`, `A72-T4/T5/T8`, `A72-T6/T7`, `A72-T9/T10/T11`, `A72-T12`).
Gated: 0 compile errors; EditMode 760/760 bar the standing `Conformance_A` asmdef drift,
PlayMode 277/277 (those are the counts the runner reports for the two package assemblies —
earlier entries quoted 823 / 291 from a different tally; the attribute count in `Tests/EditMode`
is 760, so nothing dropped). **One ⏸ owner checkpoint is open: the visual pass.** BEFORE and
AFTER captures of the three tabs are in `Library/A72Captures/before-*.png` / `after-*.png`; the
owner judges cohesion, the boxed layers and whether the eye reads as `defaultActive`. Nothing is
queued behind it. Two spec items landed as documented fallbacks: the Cutscene Editor's `F` centres
on the playhead rather than the earliest selected item (`CutsceneItemAddress` is positional, so
"earliest selected time" needs a per-lane switch nobody has asked for yet), and the layer eye's
test drives `ToggleLayerDefaultActive` directly because an unattached `VisualElement` drops
`SendEvent` (no panel, no dispatcher). Traps found on the way are in
`Assets/_Vault/Memories/Code/AnimationToolkit.md` ("Shared editor chrome").
(`Docs/AnimationToolkit/Amendment_A72_EditorVisualUnification_Spec.md`; index
`Assets/_Vault/Tasks/NewPlans/EditorVisualUnification_Roadmap.md`.)

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
- `facesDirection` is a **mirror point**, and a mirror point flips its whole subtree (owner rule
  2026-09-06). A part under one is tagged `PartMirrorFromAncestor` at bake and skips its own mirror,
  so ticking the flag on a descendant as well is redundant rather than cancelling. The slot
  inspector says so when it finds one. Ticking *nothing* is still the failure that turns nothing,
  and the bake warns about that.
- `ActorProfileBuilder` skips P2 (animation-name registry membership) at bake — building a blob has
  no access to the editor-only vocabulary provider that check needs. The Actor Editor's validation
  badge (A71) is where a profile's P2 violations actually get reported.

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
