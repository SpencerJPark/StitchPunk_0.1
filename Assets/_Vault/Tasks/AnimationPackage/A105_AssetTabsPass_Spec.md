# A105 — Asset tabs pass: Texture Packer, Flipbooks, Clip Sets, Rigs, Materials, Events

> **Status:** 📝 specced 2026-09-15, takes `0.57.0`; not built. **Needs A104 merged** (it builds on A104's classes and
> builders). Runs in parallel with A106 (disjoint folders).
> **Style guide (binding):** [`Docs/AnimationToolkit/EditorStyleGuide.md`](../../../../Docs/AnimationToolkit/EditorStyleGuide.md).
> Rules R01–R22, owner decisions SG-D1–SG-D8.
> **Executor:** `spec-lead` for §5; stage orchestrator for §6 (captures, merge, checkpoint).
> **Before captures:** `Library/UIAudit/before/01_TexturePacker.png` … `06_Events.png` (and A104's `after-a104/` set).

## 0. Session prompt

Written by the stage after A104's checkpoint: `Assets/_Vault/Spencer/next-session-a105-a106-prompt.md`.

## 1. Decisions

- **A105-D1 — Build with A104's layer only.** Every visual comes from `ToolkitComponents.uss` classes and
  `ToolkitChrome` builders; if a tab needs a class that does not exist, stop and list it in §7's For-integration block
  for a stage follow-up, never add a rule to a shared sheet. `Conformance_I` stays green (no inline visual styles).
- **A105-D2 — Texture Packer never requires a recipe (SG-D8, owner 2026-09-15: "recipes are just saved node set ups
  for texture packing, but shouldn't be required").**
  - **Opening state:** a working default setup — a Pack Output node **centred** in the canvas via the graph's frame-all
    after layout, and the source image list ready to drag from.
  - **Header:** reads "Unsaved setup" instead of "No recipe".
  - **Actions:** a secondary **Save as recipe** action creates a recipe from the current graph; the primary **Bake**
    bakes the unsaved setup directly.
  - **Recipe open:** the header shows its name plus a "modified" badge when the graph differs.
- **A105-D3 — Rigs target rows are chips + a detail card (SG-D6).**
  - **Row:** each Targets row is a flat 22px row: checkbox, node name (last path segment, full path in the tooltip),
    then — only when ticked — two `toolkit-badge` chips (Kind, Tag) that open the existing Kind/Tag pickers on click.
  - **Unticked rows:** dimmed name, no chips.
  - **Detail card:** selecting a row shows a **Target** card in the detail column (name, path, Kind, Tag, Faces
    direction) built with `MakeCard` + `MakePropertyRow`.
  - **Unchanged:** the existing Kind/Tag write paths, undo and confirmations.
- **A105-D4 — Copy says it once (R03).** Where a row repeats its parent's value (a clip's folder equal to its set's
  folder, a frame's size equal to the array's), the row omits it. Duplicated sentences in a detail panel collapse to one.

## 1.1 Audit — findings to fix (from the before captures; A104 fixes G1–G8 first)

**Texture Packer** (`01_TexturePacker.png`)
- TP1 — The header says "No recipe" over an empty canvas with one node pushed to the bottom-right (D2, R13).
- TP2 — Two Bake buttons: the blue top-right one and a grey one inside the node (R12). Keep the top one as primary
  and remove the node's.
- TP3 — Pack Output's Size X/Y fields clip "10" to "1C" (R01).
- TP4 — The node header is a saturated purple fill; use a neutral `toolkit-card__header` look with a status badge (R16).
- TP5 — The eye-slash icon button beside Refresh has no visible meaning; give it a tooltip and ghost style, and
  group it with Refresh in the pane header actions (R07, R21).
- TP6 — Image rows are rich rows (a thumbnail): make them flat 40px rows with thumbnail, name and meta — no box, and
  a path shortened to the file name (R09, R03).

**Flipbooks** (`02_Flipbooks.png`)
- FB1 — The **Import settings** block is collapsed to a sliver: its header is cut and its control row is squeezed to a
  few pixels (R01, R10). Rebuild it as a `MakeCard` with property rows (Compression, Filter, Mips…) and the
  "Match import settings of" field.
- FB2 — Header: "EarArray" left with "64×64 · 16 layers" floating mid-width. Make it a 16/600 detail title with a
  meta badge beside it (R02, R05).
- FB3 — Frame rows repeat "#0 64×64" on all 16 rows (R03). The row becomes thumbnail + frame name; the index only in
  the tooltip.
- FB4 — "Zoom" label, slider and the "Hover a frame to name it." hint float at different insets. Zoom goes into a
  24px toolbar on the frames grid; the hint goes to the status footer (R07, R14).
- FB5 — The catalog meta truncates ("imported, unn…"): meta becomes "16 · 64×64", with state as a badge only when
  not imported (R01, R03).

**Clip Sets** (`03_ClipSets.png`)
- CS1 — Every clip row repeats "Assets/ScriptableObjects/Animations" (R03). Show a clip's folder only when it differs
  from the set's (VatSampleWave's does).
- CS2 — Name and Folder labels start at different x and their fields at different x (R08). Use property rows.
- CS3 — "Ticked only" sits alone at the far right of the Clips header. It becomes a small toggle in the Clips card's
  header actions, beside the count badge "10 of 11" (R07).
- CS4 — "Ticks apply to the set immediately. Ctrl+Z undoes." floats under the card: move it to the status footer (R14).

**Rigs** (`04_Rigs.png`)
- RG1 — 34 target rows, each with grey Kind/Tag buttons and a truncated "Visual/Health…" name with no tooltip
  (R01, R09, SG-D6 → D3).
- RG2 — The Folder row's picker button is clipped by the column edge (R01).
- RG3 — "Targets" plus the loose hint "34 node(s) in 'NewRig'." become a card header with a count badge (R03, R09).
- RG4 — The "Use in Clip Editor" primary sits in the middle column header: it moves to the asset-bar position per
  R12, or stays in the working pane header if the tab has no asset bar. Record which in §7.

**Materials** (`05_Materials.png`)
- MT1 — The asset bar's "Target" label sits 150px from its "Pelvis" dropdown (R07).
- MT2 — The detail repeats itself: "Shader Shader Graphs/2DShader", then "Used by no rig target" and "No rig target
  uses this material, so no contract applies." (R03).
- MT3 — The detail is loose labels at three indents. Rebuild as cards: **Shader** (name, GPU instancing badge),
  **Usage** (rig targets using it, or an empty line), **Contract** (only when a target uses it) (R05, R09).
- MT4 — "Inspector" becomes a ghost button with an icon (R12).

**Events** (`06_Events.png`)
- EV1 — Two panes show only a top-left "Select an event on the left." (R13). Use centred `MakeEmptyState`s, the
  middle with the New action.
- EV2 — The footer "4 of 64 maskable keys used · 0 pulse-only" wraps and breaks "pulse-\nonly" (R01). It becomes two
  badges in the Keys pane header or a one-line status footer.
- EV3 — The row meta "maskable · key 16" goes right-aligned as meta "16 · maskable" (R03; names stay the title).

## 2. Files (all under `Packages/com.dotsanimationtoolkit/Editor/`)

- **Texture Packer:** `TexturePacker/TexturePackerPanel.cs`, `TexturePacker/PackOutputNodeView.cs`,
  `TexturePacker/TexturePackerGraphView.cs`, `TexturePacker/ImageCatalogColumn.cs`,
  `TexturePacker/RecipeCatalogColumn.cs`. `PackOutputNodeView`, `SourceImageNodeView` and `TexturePackerGraphView`
  are on the `Conformance_I` allowlist; converting their inline styles lets them leave it (shrink only).
- **Flipbooks:** `Flipbooks/FlipbooksPanel.cs`, `Flipbooks/FlipbookFramesColumn.cs`, `Flipbooks/FlipbookCatalogColumn.cs`.
- **Clip Sets:** `ClipEditor/Authoring/ClipSetsPanel.cs`, `ClipEditor/Authoring/ClipPickerListElement.cs`.
- **Rigs:** `ClipEditor/Authoring/RigsPanel.cs`, `ClipEditor/Authoring/RigTargetRowBuilder.cs`, new
  `ClipEditor/Authoring/RigTargetDetailCard.cs`.
- **Materials:** `Materials/MaterialsPanel.cs`, `Materials/MaterialInspectorColumn.cs`.
- **Events:** `Events/EventsPanel.cs`, `Events/EventKeyCatalogColumn.cs`, `Events/EventKeyInspectorColumn.cs`,
  `Events/EventUsageColumn.cs`.
- **Stage only:** as the lead contract, plus `EditorStyleConformanceTests.cs` allowlist shrinks (the lead reports
  them), `Documentation~/texture-packer.md` and `flipbooks.md` screenshots text if it describes the old layout.

## 3. Read

Leads ground ranges at T0 by grepping the strings quoted in §1.1 (every quoted string is in the tab's files).

Always read:
- the style guide §2–§4;
- A104's §7 For-integration block (the class and builder list);
- `Shared/ToolkitChrome.cs` (post-A104) whole.

## 4. Fixtures

- **F1 `ClipPickerModelTests.FolderColumn_OmitsTheFolder_WhenItMatchesTheSetsFolder`** (CS1). This is real logic that
  decides what a row shows. It lives beside `ClipPickerModel` if the decision is there; otherwise it's a small static
  `ClipRowFolderResolver`.
  - **Revert-to-fail:** always return the folder.
- UI changes carry no fixtures (R22: captures prove them). Every gate still runs `EditorStyleConformanceTests`,
  `ClipEditorLayoutTests` and `PackagingConformanceTests`.

## 5. Tasks — lead

- [ ] **T0 — Ground.**
  - Claim `a105`; grep every quoted string in §1.1 to its file and line.
  - Read A104's For-integration block.
  - Log drift in §7.
- [ ] **W1 — `[parallel-safe]`, one worker per line, ≤2 files each:**
  - T1 — TP2–TP6: `PackOutputNodeView.cs`, `ImageCatalogColumn.cs`.
  - T2 — TP1/D2 (default setup, Unsaved header, Save as recipe): `TexturePackerPanel.cs`, `TexturePackerGraphView.cs`.
  - T3 — FB1, FB2, FB4: `FlipbooksPanel.cs`.
  - T4 — FB3, FB5: `FlipbookFramesColumn.cs`, `FlipbookCatalogColumn.cs`.
  - T5 — CS1–CS4 + F1: `ClipSetsPanel.cs`, `ClipPickerListElement.cs` (+ the fixture file; if that makes three,
    split F1 to a T5b).
  - T6 — RG2, RG3, RG4: `RigsPanel.cs`.
  - T7 — RG1 row (D3 chips): `RigTargetRowBuilder.cs`.
  - T8 — MT1–MT4: `MaterialsPanel.cs`, `MaterialInspectorColumn.cs`.
  - T9 — EV1, EV3: `EventsPanel.cs`, `EventKeyCatalogColumn.cs`.
  - T10 — EV1 panes + EV2: `EventKeyInspectorColumn.cs`, `EventUsageColumn.cs`.
- **Gate W1:** F1 + `EditorStyleConformanceTests` + `ClipEditorLayoutTests` + `PackagingConformanceTests`. F1's revert commit
  fails exactly F1.
- [ ] **W2:**
  - T11 — D3 detail card: new `RigTargetDetailCard.cs`, wired in `RigsPanel.cs`. Sequential after T6/T7.
  - T12 — docs worker: `texture-packer.md` and `flipbooks.md` sentences that describe the recipe-required flow or
    the old layout.
- **Gate W2:** same set.
- [ ] **T13 — Close text.** §7 For-integration:
  - CHANGELOG `## [0.57.0] — Asset tabs pass`;
  - `Conformance_I` allowlist entries that can now be removed;
  - any class A104 lacked;
  - vault traps and a HANDOFF paragraph.

  Then `status a105 ready`.

## 6. Tasks — stage orchestrator

- [ ] **S1 — Merge** `a105` (after A106 if both are ready; the order is free, only the CHANGELOG stacks); compile;
  suites; allowlist shrink.
- [ ] **S2 — After captures** of tabs 01–06 → `Library/UIAudit/after-a105/`. In §7, mark every TP/FB/CS/RG/MT/EV id
  pass or fail against its capture. **Judge hard:** list anything still below the reference composition in the style
  guide, even if its id passes.
- [ ] **S3 — Drives:**
  - Texture Packer opens without a recipe and bakes an unsaved setup into `Assets/A105Scratch/` (deleted after).
  - A Rigs chip opens its picker; the detail card writes through undo on a scratch rig copy.
- [ ] **S4 — Close:** CHANGELOG `0.57.0`, pin, HANDOFF, roadmap tick, status line.
- [ ] **S5 — ⏸ owner checkpoint** (real stop while present):
  - **Show:** before/after of all six tabs.
  - **Q1.** Texture Packer's unsaved-setup flow — is "Save as recipe" where you'd look for it?
  - **Q2.** Rigs chips — right density for 34 rows?

## 7. Build log

*(T0 grounding, gates, revert-to-fail, drift, For integration, S2 audit table, drives, close.)*
