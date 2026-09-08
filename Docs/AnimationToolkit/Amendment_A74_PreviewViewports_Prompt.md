# Session prompt — Amendment A74 (paste this whole block into a fresh session)

You are running **Amendment A74 — Preview Viewports** on the DOTS Animation Toolkit package in this
repo. The spec is `Docs/AnimationToolkit/Amendment_A74_PreviewViewports_Spec.md`. Read it in full,
then its §3 "Read first" list in order, then `Assets/_Vault/Tasks/NewPlans/Cutscene_Roadmap.md` §4
— that protocol is binding. The spec's §2 decisions (A74-D1…D13) are settled; do not re-ask the
owner whether the Actor Editor should get gizmo modes (it does not), whether the VAT preview may
reuse the Clip Editor's controller (it may not — its own `PreviewRenderUtility`), or whether the
ghost should pose the scene object (it poses a preview-scene copy).

**You are the orchestrator.** You are the only process that touches `mcp__UnityMCP__*`: you
compile, run tests, drive the Editor and commit. The tasks are sized for **Sonnet subagents that
edit files only** — each gets the spec path, its task's text, and its "Read" line; each must stay
well under 100k tokens (one or two files, named line ranges, no browsing). Run the spec's waves:

- **Wave 1, five subagents at once:** T1, T2, T5, T6, T9 (all `[parallel-safe]`; T2 codes against
  §4.1's `IPreviewCameraRig` text). Wait for all five, then **one** compile gate and their four
  fixtures by `test_names`.
- **Wave 2:** T3 (one subagent). Gate + its fixture.
- **Wave 3, two at once:** T4 and T7. Gate + T4's fixture.
- **Wave 4:** T8. Gate.
- **T10 yourself**, then stop at **T11**, the ⏸ owner checkpoint, with the message the spec gives.

Commit each task alone with an `A74-Tn:` prefix (one commit per wave is acceptable when the wave
gated together — name every task in the message), staging paths explicitly, never `git add -A`.
The tree already carries four modified files and a stray `.meta` that are not yours; leave them.
Push when green.

## State you are building on

- **0.18.0 (A72)** is the last shipped version: every tab hosts one `TransportCoreElement`, colours
  come from `ToolkitPalette`, icon buttons from `ToolkitIcons`, boxed lists use `toolkit-box`.
- **A73** (profile-driven cutscenes, 0.19.0) may or may not have landed before you. Check
  `package.json`; A74 takes the next unused minor. Nothing in A74 touches the cutscene code.
- Suite baselines: A72's closing HANDOFF §4 paragraph (toolkit EditMode 760, PlayMode 277, the
  standing `Conformance_A` asmdef drift is not yours). Re-measure at T0; counts must not drop.

## The traps that will cost you a session if you rediscover them

- **`Conformance_E`** fails the build on any `Handles.`, `OnGUI`, `GUILayout` in
  `Editor/**/*.cs`. `GUIStyle.none` in `PreviewRenderUtility.BeginPreview` is fine — it is not
  on the list.
- **Static-class suffixes** (`Conformance_G`): the sample-asset writer is `VatSampleTentacleUtility`
  in `Editor/ClipUtilities/` — `Utility` is only legal in that folder.
- **One root `KeyDownEvent` registration, ever** — `CreateGUI` re-runs after a domain reload and
  the root keeps its callbacks. `VatBakeWindow` registers in `CreateGUI` once; guard with a flag.
- **`resolvedStyle` / `layout` are stale in the same `execute_code` call that built the UI.** Every
  T10 read happens in a *second* call.
- **`execute_code` is CodeDom C# 6** — no `using` lines, fully-qualified names, no `out var`.
- **Captures scale by `pixelsPerPoint`** or you get the bottom-left corner.
- **The Actor Editor borrows the window's one controller.** If the Clip Editor tab's camera comes
  back moved after a visit to the Actor tab, `RestorePose` is missing the focus or distance.
- **`LookAround` is not `Orbit` with another sign** — capture the camera position first, then put
  the focus back `distance` ahead of it. `PreviewOrbitCameraRigTests` catches this; do not "fix"
  the test.
- **A `Button` with a child loses its text measure** — icon + word goes through
  `ToolkitIcons.MakeIconTextButton` / `SetButtonIconAndText`, never `button.text` beside an `Image`.
- **A subagent that "cannot find" a member reads the file, not the spec again.** Names were
  verified on 2026-09-08; if one drifted, grep, follow the code, and note the drift in §7.

## When you finish

Update the spec's status line, `Docs/AnimationToolkit/HANDOFF.md` §4 (one paragraph: what landed,
what is owed — the owner's checkpoint), the vault note line named in T10.6, and stop with the T11
message. Do not start anything else.
