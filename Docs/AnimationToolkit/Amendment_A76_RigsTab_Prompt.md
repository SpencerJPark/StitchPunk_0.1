# Session prompt — Amendment A76 (paste this whole block into a fresh session)

You are running **Amendment A76 — Rigs tab** on the DOTS Animation Toolkit package in this repo. The
spec is `Docs/AnimationToolkit/Amendment_A76_RigsTab_Spec.md`. Read it in full, then its §3 "Read
first" list in order, then `Assets/_Vault/Tasks/NewPlans/Cutscene_Roadmap.md` §4 — that protocol is
binding. The spec's §2 decisions (A76-D1…D12) are settled; do not re-ask the owner whether selecting
a rig should edit it (it should, immediately, with undo), whether the catalog gets a delete (it does
not, this round), whether the panel should share A75's catalog code (it mirrors it, does not extract
it), or what the three column widths start at (640px for the left pair, 280px of that for the
catalog).

**You are the orchestrator.** You are the only process that touches `mcp__UnityMCP__*`: you compile,
run tests, drive the Editor and commit. The tasks are sized for **`worker` subagents that edit files
only** (spawn `worker`, never `general-purpose`; `verifier` for read-only checks) — each gets the
spec path, its task's text, its "Read" line, the §4 block it builds, and the hard rules (no `var`,
no single-letter names, explicit types). Each must stay under ~100k tokens: at most two files, named
line ranges, no browsing, "at turn 30 stop editing and write your ≤30-line report", "never call any
`mcp__UnityMCP__*` tool". Run the spec's waves:

- **Wave 1, four subagents at once:** T1, T2, T3, T4 (all `[parallel-safe]`, disjoint files). Wait
  for all four, then **one** compile gate and their eight fixtures by `test_names`. T4 has no
  fixture — it compiles or it does not.
- **Wave 2:** T5a (one subagent). Gate. Create mode must still behave exactly as it does today;
  that is the whole acceptance bar for this wave.
- **Wave 3:** T5b (one subagent). Gate + `RigsPanelTests`.
- **Wave 4:** T6 (one subagent). Gate.
- **T7 yourself** — the rename is a grep-and-replace you do, not a worker task — then stop at **T8**,
  the ⏸ owner checkpoint, with the message the spec gives.

Commit each wave with an `A76-Tn:` prefix naming every task in the message, staging paths explicitly,
never `git add -A`. On 2026-09-08 the tree carried four modified `Editor/ClipEditor/` files from the
A75 follow-up plus an unversioned screenshot under `Assets/_Vault/Tasks/Claude/`; if they are still
there, they are the owner's — leave them. Push when green.

## State you are building on

- **0.22.0 (A75 — Clip Sets tab)** is head: `ClipSetsPanel` is a two-column catalog + editor built
  over one `TwoPaneSplitView`, with a searchable catalog, boxed rows, a check-boxed clip picker
  whose ticks write through immediately, and a delete on the row context menu. **A76 is that shape
  applied to rigs.** Read that panel before you read anything else — it is the answer to most
  "how should this look" questions, and every layout constant in it was verified live.
- **A74 (0.20.0)** owns the preview viewports and `PreviewCameraNavigation`. A76 reuses
  `RigSourcePreviewElement` **unchanged** — if you find yourself editing it, stop; the two
  amendments collide there.
- A76 takes `0.23.0` unless `CHANGELOG.md` has moved.
- Suite baselines: A75's closing HANDOFF §4 paragraph (EditMode 784, PlayMode 283). Re-measure at
  T0 — counts must not drop from what you measure, and A76 adds nine tests.

## The traps that will cost you a session if you rediscover them

- **`Conformance_D` scans raw test-file text for host asset folder paths.** A75 lost a gate cycle to
  fixtures containing `"Assets/Anim/Sets"` string literals — the scan does not know they are test
  data. If a fixture genuinely needs an `Assets/`-prefixed string, build it by concatenation, the
  way `PackagingConformanceTests` dodges its own source. A76's fixtures should not need one at all.
- **`Conformance_E`** fails on any `Handles.`, `OnGUI`, `GUILayout` in `Editor/**/*.cs`.
  `ToolbarSearchField`, `EditorUtility.DisplayDialog` and `ObjectField` are all fine.
- **`Conformance_G` allows exactly eight static-class suffixes** — Api, Builder, Sampler, Resolver,
  Math, Validation, Utility (and `Utility` only inside `Editor/ClipUtilities/`), Editing — plus a
  plain-noun allowlist. That is why the spec's two new static classes are named
  `RigTargetRowBuilder` and `RigTargetReferenceResolver`; a `…Model` or `…Sweep` would fail the
  gate, and so would `…Helper`/`…Utils`. Do not rename them, and do not add an allowlist entry.
  `RigCatalogColumn` and the `RigTargetRow` row class are `public sealed class` — instance types,
  unaffected. `RigAssetUtility` is already legal and only gains methods.
- **`Conformance_F`**: one `<summary>` per file, on the primary type, three lines at most, no
  `<remarks>`, no spec citations in shipped sources. Subagents copying this spec's prose into
  comments is the most likely way to fail it.
- **A recycled `ListView` row must not capture the loop variable.** `bindItem` runs on reused
  elements; the catalog stores the asset in the row's `userData` and reads it live, exactly as
  `ClipSetsPanel.MakeClipSetRow` explains at `:198-202`. Copy that, do not improve it.
- **`RigTargetDefinition.stableId` is `internal`.** Only `RigAsset.EnsureStableIds()` can mint one,
  and it must be called *after* the target is in the list and *before* anything reads `Id.Value`
  (A76-D5). A `SerializedObject` built before that call will write the minted id back to 0.
- **The untick guard must restore the tick with `SetValueWithoutNotify`.** A plain `value = true`
  re-enters the same callback and asks the owner the same question forever.
- **Never rebuild the row list from inside a `ChangeEvent` callback** — the row that raised it is
  mid-dispatch. Update the row in place and raise the panel's own event instead.
- **A hidden `TwoPaneSplitView` lays out at zero and comes back collapsed with no handle**
  (`ClipEditorWindow.cs:1698-1702`). The panel is built lazily on first show and the pane's hidden
  class is applied after the `Add`; keep that order.
- **`AssetDatabase` writes must be proven on a reloaded reference** (HANDOFF §3). T7 ticks a node,
  `Refresh()`es, and loads the rig fresh from its path — "the toggle is ticked" is not proof. Do it
  against a `CopyAsset` scratch rig under `Assets/A76Scratch/`, never the owner's real one, and
  delete the folder afterwards.
- **`execute_code` is CodeDom C# 6** — no `using` lines, fully-qualified names, no `out var`.
  `resolvedStyle` / `layout` are stale in the call that built the UI; read them in a second call.
- **Captures scale by `pixelsPerPoint`** or you get the bottom-left corner, and `GrabPixels` returns
  a stale frame when `EditorApplication.isFocused` is false — check it first. A75's capture step
  failed exactly this way; if it repeats, say so and record `resolvedStyle` numbers instead of
  claiming a screenshot you do not have.
- **A subagent that "cannot find" a member reads the file, not the spec again.** Names and line
  ranges were verified on 2026-09-08; if one drifted, grep, follow the code, and note the drift
  in the spec's §7.

## When you finish

Update the spec's status line, `Docs/AnimationToolkit/HANDOFF.md` §4 (one paragraph: what landed,
what is owed — the owner's checkpoint), the vault note section named in T7.6, and stop with the T8
message. Do not start anything else.
