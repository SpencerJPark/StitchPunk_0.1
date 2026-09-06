# Session prompt — Amendment A66 (paste this whole block into a fresh session)

You are running **Amendment A66 — Cutscene Editor Polish I: Selection, Clipboard, Auto Key, Curves**
on the DOTS Animation Toolkit package in this repo. The spec is
`Docs/AnimationToolkit/Amendment_A66_CutsceneEditorPolish1_Spec.md`. Read it in full, then its §2
"Read first" list in order, then `Assets/_Vault/Tasks/NewPlans/Cutscene_Roadmap.md` §4 — that
protocol is binding. Its §3 design and §4 decisions (A66-D1/D2/D3) are settled; do not re-ask the
owner about the selection model, the clipboard's separation from `ClipKeyClipboard`, or Auto Key
detecting on `hotControl` release. This is **Editor assembly only** — no runtime, no game code.

Work §5's tasks in order, T1 through T6. **T4 and T5 are marked `[parallel-safe]` with each other**:
spawn one subagent each, hand it the spec path and its task text, wait, then run one compile gate
over both. **Subagents never call any `mcp__UnityMCP__*` tool** — only you compile, run tests and
commit. Commit each task alone with an `A66-Tn:` prefix, staging paths explicitly, never
`git add -A`. Push when the task is green.

## State you are building on

**A69 (Code Style Unification) lands before you.** Pull first. It renames the package's static
classes to one suffix per role and merges the runtime API into `CutsceneApi` / `PlaybackApi` /
`BillboardApi` / `ClipRegistryApi`, and it strips the package's doc-comment volume from 25% of lines
to under 6%. Consequences you must plan around:

- **Every name in the A66 spec was written on pre-A69 code.** If a type or method the spec names does
  not exist, grep for the current name, follow the code, and log the drift in the spec's §7 build
  log. Never edit the spec so it agrees with your code.
- **You are the first feature spec written under the new comment rule, which is the whole reason A69
  was run first.** One `<summary>` per file on the primary type, three lines maximum. No `<remarks>`,
  no `<para>`/`<strong>`/`<em>`, no "amendment A", "Phase" or "§" citations in non-test sources. A
  field comment only for a sentinel, a unit, or an ordering trap. A `//` only for a *why* the code
  cannot say. If a comment is the only thing making a name understandable, rename the thing.
  `Conformance_F`/`G`/`H` enforce this and will fail you.
- **Take your suite baselines from A69's closing HANDOFF §4 paragraph, not from anywhere older.** A69
  adds conformance fixtures and changes counts. For reference, the last pre-A69 numbers (end of G2,
  2026-09-06) were toolkit EditMode 718 discovered / 717 passed — the one failure being the
  long-standing `Conformance_A_AsmdefReferenceLists_MatchSection13Exactly` asmdef drift, which is not
  yours and must not be chased — toolkit PlayMode 261/261, `StitchPunk.Tests` 59/59,
  `StitchPunk.Tests.PlayMode` 7/7.

**Five owner checkpoints are already queued** — G1, A63, A64, A65 and G2 — each with its own scene or
setup, all awaiting the owner's eyes, none needing rework. Do not rebuild or "verify" them, and add
no sixth beyond A66's own.

## The traps that will cost you a session if you rediscover them

- **`Conformance_E` bans IMGUI outright in this package's Editor sources**: `OnGUI`, `GUILayout` and
  `Handles.` are greped for, comments included, with no exemption list. Everything A66 adds — box
  select, the curve host, the Auto Key toggle — is UI Toolkit. `HandleUtility` does *not* match the
  regex; only `Handles.` does. A spec that prescribes `Handles` is prescribing a build break (A64
  learned this the expensive way).
- **Never rebuild a pane from a value-changed callback.** Go through `RequestInspectorRebuild` /
  `RequestTimelineRebuild`. A direct rebuild kills the drag that raised the event, and rebuilding
  from a *bound* field's change event re-binds and re-raises forever — measured at 600 rebuilds in a
  few idle seconds. A re-entrancy flag does not catch it because binding is deferred; the working
  discriminator is `previousValue != newValue`, already written as
  `CutsceneEditorPanel.ShouldIgnoreBindingEcho`. Use it on every field you add.
- **`KeyDownEvent` on the panel steals Ctrl+C from text fields inside it.** Check the event target is
  not an editable field and return early (spec §6).
- **Auto Key must be off while the transport plays** — the transport writes poses every tick and
  `hotControl` can be non-zero for unrelated UI (spec §6).

## Driving the Editor over MCP

Unity MCP is the compile gate and the Editor must be open; if `mcp__UnityMCP__*` is unreachable, say
so and fall back to static review rather than claiming anything compiles. Verify real API signatures
in `Library/PackageCache` before calling them — there is no compiler mid-task.

Notes paid for across A63–A65 and G2, all still true:

- `execute_code` runs as a **method body**, so `using` directives are a syntax error — fully qualify
  every type. Only the **CodeDom C# 6** backend is available in this project (Roslyn is not
  installed): no local functions, no `ref` returns, no target-typed `new`, and
  `World.GetOrCreateSystem<T>()` picks the wrong overload — use `GetOrCreateSystem(typeof(T))`.
  `UnityEngine.Object` vs `object` is ambiguous there.
- **A `VisualElement` that is not in a panel dispatches no change events.** A field probed standing
  alone looks completely inert — `RegisterValueChangedCallback` never fires, and it reads exactly
  like a broken binding. Parent your container into a live `EditorWindow.rootVisualElement` first,
  drive it, then remove it. Find a window with `Resources.FindObjectsOfTypeAll<EditorWindow>()`;
  **`EditorWindow.GetWindow<T>()` stalls the Editor for about a minute.**
- The Clip Editor's `cutscenePanel` field is **null until the tab has been shown, and again after
  every domain reload** — `Focus()` the window and invoke `SetActiveTab` before reflecting into it.
- `AssetDatabase.DeleteAsset` needs `safety_checks=false`. A delegate subscribed to
  `SceneView.duringSceneGui` from `execute_code` outlives the call and will fire later and mutate
  assets — remove it explicitly. Large bash heredocs fail on this shell; write patch scripts to the
  scratchpad and run them with `python`. Running PlayMode tests flips
  `ProjectSettings/EditorSettings.asset` — **never stage it**.
- A green test run taken immediately after an edit may be running the previous compiled binary: if a
  revert stubbornly passes, recompile and re-run before believing it.

## Test discipline

The spec names the ceiling, not a floor: **two EditMode fixtures total** —
`CutsceneSelectionMathTests.ShiftTimes_ClampsAtZero_AndPreservesOrder` (T1) and
`CutsceneKeyClipboardTests`' two cases (T3). T2, T4 and T5 get **no fixture** and are proved live
through `execute_code` against a real, open Cutscene Editor tab, with the numbers quoted in the
commit message. Before keeping any fixture, revert the fix and watch it fail; if it passes both ways,
delete it.

## Finish

Full suites once at the end (toolkit EditMode then PlayMode, plus `StitchPunk.Tests` /
`StitchPunk.Tests.PlayMode` if you touched anything they cover), checking discovered totals did not
drop. Update the spec's status line and §7 build log, `CHANGELOG.md`, and HANDOFF §4 with one
paragraph on what landed and what is owed. Then **stop at the ⏸ owner checkpoint** and hand back with
exactly what to open, press and look at: box-select a beat, Ctrl+C, select another slot, Ctrl+V at a
later playhead; Auto Key on, move an actor with the gizmo and watch a root key appear; open a Bézier
key and drag the curve. Say plainly which parts you machine-verified and which need the owner's eyes
— "the property was written and read back" is machine-verifiable, "it feels right to drag" is not.

**A67 follows this session** (`Amendment_A67_CutsceneEditorPolish2_Spec.md`: viewport click-select,
in-viewport gizmo, frozen header column, cast/inspector compaction, navigation parity). It depends on
A66's selection set and Auto Key, so it cannot start until this lands.
