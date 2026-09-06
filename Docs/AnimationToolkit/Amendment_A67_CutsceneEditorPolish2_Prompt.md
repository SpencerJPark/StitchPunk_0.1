# Session prompt — Amendment A67 (paste this whole block into a fresh session)

**Do not start this until A66 has landed.** A67 builds directly on A66's selection set and Auto Key;
without them there is nothing for a viewport click to select into and nothing for a gizmo drag to key.

You are running **Amendment A67 — Cutscene Editor Polish II: Viewport Picking, In-Viewport Gizmo,
Frozen Headers** on the DOTS Animation Toolkit package. The spec is
`Docs/AnimationToolkit/Amendment_A67_CutsceneEditorPolish2_Spec.md`. Read it in full, then its §2
"Read first" list in order, then `Assets/_Vault/Tasks/NewPlans/Cutscene_Roadmap.md` §4 — that
protocol is binding. It closes the A59/A60 backlog: click-select in the tab's viewport, an
in-viewport gizmo, cast/inspector compaction, the frozen header column, and navigation parity with
the Clip Editor viewport. **Editor assembly only.** The spec's own budget says **no fixtures** — this
is UI wiring, and every task is proved live and then by the owner's eyes.

Everything in `Amendment_A66_CutsceneEditorPolish1_Prompt.md`'s two sections **"The traps that will
cost you a session if you rediscover them"** and **"Driving the Editor over MCP"** applies here
unchanged — read them before writing anything. The three that bite hardest in a viewport amendment:

- **`Conformance_E` bans `Handles.`, `OnGUI` and `GUILayout` outright** in this package's Editor
  sources, comments included, with no exemption list. An in-viewport gizmo therefore cannot use
  `Handles.PositionHandle`. What works, and is already this package's own idiom, is a line-topology
  `Mesh` drawn with `Graphics.DrawMeshNow` after `material.SetPass(0)`, picked with
  `HandleUtility.GUIPointToWorldRay` against a `Plane` — `HandleUtility` does not match the regex.
  `CutsceneMarkSceneOverlay.cs` is the worked example; `PreviewSceneGizmos.cs` explains the same
  constraint from the other side. A64 had to redesign mid-session over exactly this.
- **Picking and dragging cannot be machine-verified from a background Editor.** An unfocused Editor
  never repaints its Scene view, so a `SceneView.duringSceneGui` handler is never called —
  `SceneView.RepaintAll()`, `sceneView.Repaint()` and even `RepaintImmediately()` all leave it
  uncalled. Anything that only runs inside scene GUI is therefore **unprovable over MCP**: say so
  rather than reporting it as verified. The tab's own `CutsceneViewportElement` is a UI Toolkit
  element and *is* drivable — check which surface a task actually lives on before claiming either way.
- **Never rebuild a pane from a value-changed callback**, and guard every bound field with
  `CutsceneEditorPanel.ShouldIgnoreBindingEcho`. The frozen header column means two synced scroll
  views; a scroll handler that rebuilds is the same trap wearing a different hat.

Write under A69's comment rule — one `<summary>` per file on the primary type, three lines maximum,
no `<remarks>`, no spec citations in non-test sources. `Conformance_F`/`G`/`H` enforce it.

Take your suite baselines from A66's closing HANDOFF §4 paragraph. Commit each task alone with an
`A67-Tn:` prefix, staging paths explicitly, never `git add -A`; push when green. Full suites once at
the end, checking discovered totals did not drop. Update the spec's status line and build log,
`CHANGELOG.md`, and HANDOFF §4.

Then **stop at the spec's ⏸ owner checkpoint** and hand back with exactly what to open, press and
look at — and be explicit about which of it you could drive and which needs a focused Editor and the
owner's hands, because in this amendment that line falls in the middle of the work.
