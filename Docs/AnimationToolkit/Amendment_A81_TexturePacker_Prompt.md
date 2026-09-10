# Session prompt — Amendment A81 (paste this whole block into a fresh session)

You are running **Amendment A81 — Texture Packer tab** on the DOTS Animation Toolkit package in this
repo. The spec is `Docs/AnimationToolkit/Amendment_A81_TexturePacker_Spec.md`. Read it in full, then
its §3 "Read first" list in order, then `Assets/_Vault/Tasks/NewPlans/Cutscene_Roadmap.md` §4 — that
protocol is binding. The spec's §2 decisions (A81-D1…D21) are settled; do not re-ask the owner
whether the tab is first (it is), whether the game-side folder is deleted (it is, by you, in T16),
whether the sidebar rows are a list or a grid (boxed list rows with 48px thumbnails), whether recipes
get a catalog (a segmented `Images | Recipes` sidebar), or which extras are in (double-click add,
channel-row drop auto-wire, presets + channel chips, drop-to-replace on a source node — and not a
standalone window, auto-repack, or a rig filter), or how recipes are written (New prompts for a
name; the Recipes tab's Save button is the **only** writer — a bake never touches the recipe; an
unsaved marker and a discard prompt guard the difference).

**You are the orchestrator.** You are the only process that touches `mcp__UnityMCP__*`: you compile,
run tests, drive the Editor and commit. The tasks are sized for **`worker` subagents that edit files
only** (spawn `worker`, never `general-purpose`; `verifier` for read-only checks) — each gets the
spec path, its task's text, its "Read" line, the §4 block it builds, and the hard rules (no `var`,
no single-letter names, explicit types; one `<summary>` per file, ≤3 lines, no `§` or amendment
citations in shipped code). Each must stay under ~100k tokens: at most two files, named line ranges,
no browsing, "at turn 30 stop editing and write your ≤30-line report", "never call any
`mcp__UnityMCP__*` tool". **Spawn from the repo root** — the read-guard resolves `.claude/hooks/`
from cwd and broke an entire A76 wave when cwd had drifted into the package folder.

Run the spec's plan:

- **T0 yourself:** baseline gate, and **drive the existing game-side window once** — it has an
  unticked verification checklist from July and no recipe asset in the project, so nobody knows
  what works. Record what you find in §7.
- **T1 yourself:** write `Editor/TexturePacker/PackRequest.cs` (§4.1) before spawning anything, so
  every worker codes against types that exist on disk.
- **One wave, thirteen workers at once:** T2, T3, T4a, T4b, T5, T6, T7, T8, T9, T10, T11, T12, T13,
  T14 (all `[parallel-safe]`, all disjoint files). Wait for all. Read every report. A capped worker
  is never resumed: read its diff, and spawn a fresh `worker` for only what is missing.
- **T15 and T16 yourself** (the five small edits in §4.11, then `git rm` the game folder and the
  vault doc edits), then **one** compile gate and the three named fixtures.
- **T17 yourself:** full suites once, the drive in the spec, the capture you look at, docs, commits.
- Stop at **T18**, the ⏸ owner checkpoint, with the message the spec gives.

Commit per wave with an `A81-Tn:` prefix naming every task, staging paths explicitly, never
`git add -A`. Push when green.

## State you are building on

- **0.27.0 (A80)** is head: every tab shares one `ActiveAssetSelection`; the top bar is tabs and the
  validation badge. The packer does **not** join the shared selection.
- **Five catalogs will exist after this build** — do not extract a shared one; that is the handoff's
  separate #1 item.
- A81 takes `0.28.0` unless `CHANGELOG.md` has moved.
- Suite baselines: HANDOFF §4's A80 paragraph (EditMode 820, PlayMode 283). Re-measure at T0; A81
  adds two EditMode tests.

## The traps that will cost you a session if you rediscover them

- **`GraphView` calls `StretchToParentSize()` on itself** — absolute, insets 0. Added beside a header
  it draws over the header. Host it in its own `graphHost` element (A81-D19).
- **`ConnectPorts` bypasses `graphViewChanged`**, so the single-capacity replacement the vault
  documents does not run for programmatic wires. `AddSourcesWiredIntoChannel` calls
  `DisconnectExistingEdges` first, or the G row ends up with two edges.
- **`port.connected` is stale inside `graphViewChanged`** and **`RemoveElement(edge)` does not
  disconnect ports** — `Editor.md`'s GraphView section; the moved code already handles both; do not
  "simplify" them away.
- **`Conformance_D` scans `*.md` and `*.json` too.** `Assets/Textures` in the docs page or a
  fixture string fails the gate; `Assets/…` or `Assets/` alone passes (empty segment). The baker's
  `"Assets/"` validation literal is fine for the same reason.
- **`Conformance_G`:** `TexturePackBaker` is an **instance** class (no suffix rule), `TexturePackMath`
  and `TexturePackPortBuilder` carry allowed suffixes, `TexturePackRecipeAssetUtility` must live in
  `Editor/ClipUtilities/`, and `PackChannelIndex` + `TexturePackRecipeAssetOpener` go on the
  plain-noun allowlist in T15. A worker that names something `…Helper`, `…Util` or `…UI` fails the
  gate.
- **`Conformance_F`:** the game-side files carry big `// ====` banner comments and a `<summary>` on
  nearly every member. Workers must not carry those across: one `<summary>` per file on the primary
  type, ≤3 lines; the `//` *why* comments (invert-only-on-samples, single-capacity teardown, the
  first-creation import stamp) do come across.
- **Every radio group is `SetValueWithoutNotify`** — the sidebar mode switch, the source chips, and
  the tab strip itself. A plain `value = true` re-enters the callback.
- **A recycled `ListView` row must not capture per-bind data** — store the entry in `row.userData`
  and read it live, exactly as `RigCatalogColumn.MakeRigRow` explains. Both new columns copy that.
- **The game-side `BakeTo` writes the output path back into the loaded recipe** (`:266-270`). That
  block must not be moved across — it is exactly the "saved every time" the owner ruled out. If a
  worker's panel calls `SetDirty` on a recipe anywhere but `SaveRecipe`, send it back.
- **Three drop targets nest: source node, output channel row, canvas.** The node and the row
  handlers `StopPropagation()` after `AcceptDrag`, or the canvas handler fires too and a replace
  becomes a replace plus a duplicate node.
- **`DragAndDrop.StartDrag` must run inside a pointer event** — `PointerMoveEvent` with
  `pressedButtons == 1`, then `StopPropagation()` so the `ListView` does not also start a rectangle
  selection. The idiom is `ClipEditorWindow.RegisterReparentDrag`.
- **A hidden `TwoPaneSplitView` lays out at zero and comes back collapsed** — the panel is built on
  first show and the hidden class is applied after the `Add`; keep `ShowClipSetsTab`'s order.
- **`execute_code` is CodeDom C# 6** — no `using` lines, fully-qualified names, no `out var`;
  `resolvedStyle`/`layout` are stale in the call that built the UI. Captures scale by
  `pixelsPerPoint`, need `EditorApplication.isFocused`, and need a positive window position.
- **Reflect private members, do not add public ones for the drive.** The panel exposes `Sidebar` and
  `Graph`; everything the drive needs is reachable from those two.
- **A subagent that "cannot find" a member reads the file, not the spec again.** Names and line
  ranges were verified on 2026-09-10; if one drifted, grep, follow the code, and note the drift in §7.

## When you finish

Update the spec's status line and §7, `Docs/AnimationToolkit/HANDOFF.md` §4 (one paragraph: what
landed, what is owed — the owner's checkpoint), the vault note section named in T17.5, and stop with
the T18 message. Do not start anything else.
