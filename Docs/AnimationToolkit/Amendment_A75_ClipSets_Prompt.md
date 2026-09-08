# Session prompt — Amendment A75 (paste this whole block into a fresh session)

You are running **Amendment A75 — Clip Sets tab** on the DOTS Animation Toolkit package in this
repo. The spec is `Docs/AnimationToolkit/Amendment_A75_ClipSets_Spec.md`. Read it in full, then its
§3 "Read first" list in order, then `Assets/_Vault/Tasks/NewPlans/Cutscene_Roadmap.md` §4 — that
protocol is binding. The spec's §2 decisions (A75-D1…D12) are settled; do not re-ask the owner
whether Clip Sets is a tab or a button (a tab; the button goes), whether ticks apply immediately in
edit mode (they do, with undo), or where the save folder is remembered (`EditorPrefs`, validated on
read).

**You are the orchestrator.** You are the only process that touches `mcp__UnityMCP__*`: you
compile, run tests, drive the Editor and commit. The tasks are sized for **`worker` subagents that
edit files only** (spawn `worker`, never `general-purpose`; `verifier` for read-only checks) — each
gets the spec path, its task's text, its "Read" line, the §4 block it builds, and the hard rules
(no `var`, no single-letter names, explicit types). Each must stay under ~100k tokens: at most two
files, named line ranges, no browsing, "at turn 30 stop editing and write your ≤30-line report",
"never call any `mcp__UnityMCP__*` tool". Run the spec's waves:

- **Wave 1, three subagents at once:** T1, T2, T3 (all `[parallel-safe]`, disjoint files). Wait
  for all three, then **one** compile gate and their six fixtures by `test_names`.
- **Wave 2, three at once:** T4, T6a, T6c. T4 reads T2's real file. Gate + `ClipEditorLayoutTests`.
  The window still binds five tabs at this point; the sixth toggle sits unbound until wave 4 —
  expected, and the layout test already agrees with the UXML.
- **Wave 3:** T5 (one subagent). Gate + `ClipSetsPanelTests`.
- **Wave 4:** T6b (one subagent). Gate + `ClipEditorLayoutTests`.
- **T7 yourself**, then stop at **T8**, the ⏸ owner checkpoint, with the message the spec gives.

Commit each wave with an `A75-Tn:` prefix naming every task in the message, staging paths
explicitly, never `git add -A`. On 2026-09-08 the tree carried ~40 uncommitted files from another
session (the 0.21.0 New Rig source preview and the owner's asset edits); if they are still there,
they are not yours — leave them. Push when green.

## State you are building on

- **0.21.0 (New Rig source preview)** was the uncommitted head when this was written, over
  **0.20.0 (A74)**: three viewports share `PreviewCameraNavigation`, the VAT Bake tab has a live
  preview, and `NewRigPanel` is a two-column form + preview pane. A75 takes the next unused minor
  (`0.22.0` unless `CHANGELOG.md` has moved). Nothing in A75 touches a viewport or a camera.
- **A72's shared chrome** is what the new panel wears: `toolkit-pane-header` / `toolkit-pane-title` /
  `toolkit-pane-actions`, `toolkit-box` rows, `ToolkitIcons.MakeIconTextButton`, `ToolkitPalette`
  colours. Inline styles are layout only.
- Suite baselines: A74's closing HANDOFF §4 paragraph (EditMode 777 with one standing
  `Conformance_A` asmdef-drift failure that is not yours, PlayMode 283); the New Rig preview work
  may have moved them. Re-measure at T0 — counts must not drop from what you measure.

## The traps that will cost you a session if you rediscover them

- **`Conformance_E`** fails on any `Handles.`, `OnGUI`, `GUILayout` in `Editor/**/*.cs`.
  `ToolbarSearchField` and `EditorUtility.OpenFolderPanel` are fine — UI Toolkit and a dialog.
- **`Conformance_G`**: the two new logic classes are `public sealed class`, not static, so no
  suffix rule and no allowlist entry. Only `ClipAssetUtility` (already legal) gains methods.
- **`Conformance_F`**: one `<summary>` per file, on the primary type, three lines at most, no
  `<remarks>`, no spec citations in shipped sources. Subagents copying this spec's prose into
  comments is the most likely way to fail it.
- **A recycled `ListView` row must not capture the loop variable.** `bindItem` runs on reused
  elements; store the entry index in `userData` and read it in the toggle's callback, or ticking
  row 3 writes row 1's clip.
- **`ClipEditorTab` values shift** (D1). `tabToggles` is indexed by the enum, so it must be sized
  6 in the same commit that binds the tab, or `BindTab` throws on the last one.
- **The window's `clipSet` is never written by the panel.** Everything goes through
  `clipSetField.value = …` so `OnClipSetChanged` runs — the same comment the deleted
  `CreateClipSet` carried.
- **`AssetDatabase` writes must be proven on a reloaded reference** (HANDOFF §3). T7's third call
  loads the probe set fresh from its path after `Refresh()`; "the picker shows a tick" is not proof.
- **`execute_code` is CodeDom C# 6** — no `using` lines, fully-qualified names, no `out var`.
  `resolvedStyle` / `layout` are stale in the call that built the UI — read in a second call.
- **Captures scale by `pixelsPerPoint`** or you get the bottom-left corner. Look at the PNG.
- **A subagent that "cannot find" a member reads the file, not the spec again.** Names were
  verified on 2026-09-08; if one drifted, grep, follow the code, and note the drift in §7.

## When you finish

Update the spec's status line, `Docs/AnimationToolkit/HANDOFF.md` §4 (one paragraph: what landed,
what is owed — the owner's checkpoint), the vault note section named in T7.5, and stop with the T8
message. Do not start anything else.
