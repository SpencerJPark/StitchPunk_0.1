# A105 — Asset tabs pass: Texture Packer, Flipbooks, Clip Sets, Rigs, Materials, Events

> **Status:** ✅ **BUILT 2026-09-19** as `0.58.0`, merged to trunk and integrated the same day (EditMode 968/968). Reconciled against trunk first — see §7 Phase 0; §1.1 is the residual only.
> A104 is merged, and eight later trunk passes plus A108 (`0.57.0`) closed many of this spec's original findings
> without updating it. **§1.1 below is the residual only** — the struck findings are already on trunk and must not be
> rebuilt. Runs in parallel with A106 and A107 (disjoint folders, seam in the session prompt).
> **Style guide (binding):** [`Docs/AnimationToolkit/EditorStyleGuide.md`](../../../../Docs/AnimationToolkit/EditorStyleGuide.md).
> Rules R01–R22, owner decisions SG-D1–SG-D8.
> **Executor:** `spec-lead` for §5; stage orchestrator for §6 (captures, merge).
> **Before captures:** `Library/UIAudit/before-phase6-close/01_TexturePacker.png` … `06_Events.png`
> (taken 2026-09-19 with `NewRig` + `NewClipSet` selected; the old `before/` set shows a UI that no longer exists).

## 0. Session prompt

`Assets/_Vault/Spencer/next-session-phase6-close-prompt.md` (2026-09-19). The original was
`next-session-a105-a106-prompt.md`, whose lead contract and gate syntax still apply.

## 1. Decisions

- **A105-D1 — Build with A104's layer only.** Every visual comes from `ToolkitComponents.uss` classes and
  `ToolkitChrome` builders; if a tab needs a class that does not exist, stop and list it in §7's For-integration block
  for a stage follow-up, never add a rule to a shared sheet. `Conformance_I` stays green (no inline visual styles).
- **A105-D2 — Texture Packer never requires a recipe (SG-D8).** **Still fully open** — nothing on trunk implements it.
  - **Opening state:** a working default setup — a Pack Output node **centred** in the canvas via the graph's frame-all
    after layout, and the source image list ready to drag from.
  - **Header:** reads "Unsaved setup" instead of "No recipe".
  - **Actions:** a secondary **Save as recipe** action creates a recipe from the current graph; the primary **Bake**
    bakes the unsaved setup directly.
  - **Recipe open:** the header shows its name plus a "modified" badge when the graph differs.
- **A105-D3 — Rigs target rows are chips + a detail card (SG-D6).** **Partly built — build only the gap:**
  - ~~Row is a flat 22px row: checkbox, node name, full path in tooltip.~~ Done (`RigsPanel.cs:598-624`).
  - ~~A "Target" card exists in the detail column with Name/Node/Kind/Tag.~~ Done inline (`RigsPanel.cs:331-376`).
  - **Open:** ticked rows carry no `toolkit-badge` Kind/Tag chips, and the Target card has no **Faces direction** row.
    Add both. The card stays where it is — do **not** extract a `RigTargetDetailCard.cs`; the inline builder is fine
    and moving it would churn `RigsPanel` for no visual gain.
  - **Unchanged:** the existing Kind/Tag write paths, undo and confirmations.
- **A105-D4 — Copy says it once (R03).** Where a row repeats its parent's value, the row omits it. Duplicated
  sentences in a detail panel collapse to one.

## 1.1 Audit — residual findings (reconciled 2026-09-19)

Struck = already on trunk, verified by code evidence and cross-checked against the before-captures. Do not rebuild.

**Texture Packer** (`01_TexturePacker.png`)
- **TP1 — OPEN.** Header says "No recipe" over an empty canvas with the Pack Output node parked bottom-right
  (`TexturePackerPanel.cs:451`, `TexturePackerGraphView.cs:46` — node fixed at (620,180), no frame-all). D2, R13.
- **TP2 — OPEN.** Two Bake buttons: the asset-bar one (`TexturePackerPanel.cs:94`) and one inside the node
  (`PackOutputNodeView.cs:102-103`). Keep the asset-bar one as primary, remove the node's (R12).
- **TP3 — OPEN in code, passes at this window width.** `PackOutputNodeView.cs:54-56` is still a plain
  `Vector2IntField` with no min-width, but the capture reads "10" correctly at 1279pt. Give the field a min-width so
  it cannot clip at a narrower window; do not spend a layout pass on it (R01).
- **TP4 — OPEN.** The node header is a saturated purple fill (`PackOutputNodeView.cs:20,44`,
  `HeaderColor (0.34,0.20,0.44)`); use a neutral `toolkit-card__header` look with a status badge (R16).
- ~~TP5 — the eye-slash icon button has no meaning.~~ **DONE** (`ImageCatalogColumn.cs:65-78`: tooltip added, grouped
  with Refresh in the header actions, ghost style).
- ~~TP6 — image rows are rich boxed rows.~~ **DONE** (`ImageCatalogColumn.cs:162-194`: flat `MakeListRowSlot` row,
  24px thumbnail, name + meta, folder shortened with the full path in the tooltip). Row height is 28px, not the
  spec's 40px — accepted, it matches every other list in the window.

**Flipbooks** (`02_Flipbooks.png`)
- ~~FB1 — the Import settings block is collapsed to a sliver.~~ **DONE** (`FlipbooksPanel.cs:138`: `MakeCard` with
  Filter / Wrap / Mips / Linear / "Match import settings of" property rows).
- **FB2 — PARTIAL.** Title and meta are adjacent now (`FlipbooksPanel.cs:119-122,682`) but the meta is a plain
  `Label`, not a `MakeBadge`. Make it a meta badge beside a 16/600 detail title (R02, R05).
- **FB3 — PARTIAL.** Rows have thumbnail + name (`FlipbookFramesColumn.cs:164-228`) but still print the `#0` index
  on the row. Move the index to the tooltip only (R03).
- **FB4 — OPEN.** "Zoom" label, slider and the "Hover a frame to name it." hint float at different insets
  (`FlipbooksPanel.cs:72-80`, `FlipbookPreviewElement.cs:44-48`). Zoom into a 24px toolbar on the frames grid; the
  hint into the status footer (R07, R14).
- **FB5 — OPEN.** The catalog meta is one concatenated string that truncates
  (`FlipbookCatalogColumn.cs:117-126`). Meta becomes "16 · 64×64", with state as a badge only when not imported
  (R01, R03).

**Clip Sets** (`03_ClipSets.png`)
- ~~CS1 — every clip row repeats the set's folder.~~ **DONE** (`ClipPickerListElement.cs:291-292`, with
  `ComputeHomeFolderPath` at `:142-159`).
- **CS2 — OPEN.** Name is a raw `TextField("Name")` and Folder a separate `PathPickerRowElement`
  (`ClipSetsPanel.cs:173,186-190`); labels and fields start at different x. Use `MakePropertyRow` for both (R08).
- **CS3 — PARTIAL.** "Ticked only" moved into the title row (`ClipPickerListElement.cs:56-67`) but the count badge
  sits in its own row below the header rather than beside the toggle (`:95-100`). Put "10 of 11" in the header
  actions beside the toggle (R07).
- **CS4 — OPEN.** "Ticks apply to the set immediately. Ctrl+Z undoes." is still added under the picker
  (`ClipSetsPanel.cs:235-243`); move it into the existing status footer row (R14).

**Rigs** (`04_Rigs.png`)
- ~~RG1 — 34 boxed target rows with grey Kind/Tag buttons and no tooltip.~~ **DONE** (`RigsPanel.cs:598-624`).
- ~~RG2 — the Folder row's picker button is clipped.~~ **DONE** (`ClipEditorWindow.uss:604-608`).
- **RG3 — OPEN.** "Targets" is a plain `MakeHeading` with a loose "34 node(s) in 'NewRig'." hint beneath
  (`RigsPanel.cs:317-321`). Make it a card header with a count badge (R03, R09).
- ~~RG4 — the "Use in Clip Editor" primary is in the middle column header.~~ **DONE / recorded**
  (`RigsPanel.cs:250-259`). The tab has no asset bar, so R12's "working pane header" branch applies. Settled.
- **RG5 — NEW (stage, from the capture).** The Targets list body is the same tone as its column: the middle column
  reads as one flat grey while the left catalog is correctly two-tone. Give the list body `toolkit-list-surface`
  (the owner's headline requirement — a tab that reads as one note has failed).
- **RG6 — NEW (stage, from the capture).** "Select a rig, or press New to make one." is a plain top-left sentence,
  not a designed empty state (R13). Use `MakeEmptyState` with New as its action.

**Materials** (`05_Materials.png`)
- ~~MT1 — the "Target" label sits 150px from its dropdown.~~ **DONE** (`MaterialsPanel.cs:78-83`).
- **MT2 — PARTIAL.** The shader label/value split is fixed, but "Used by no rig target" and "No rig target uses this
  material, so no contract applies." still both render (`MaterialInspectorColumn.cs:104,147`). Say it once (R03).
- **MT3 — PARTIAL.** One "Material" card exists (`MaterialInspectorColumn.cs:92-149`) but Usage and Contract are
  still loose rows in the scrollview. Split into **Shader** / **Usage** / **Contract** cards, Contract only when a
  target uses it (R05, R09).
- ~~MT4 — "Inspector" should be a ghost icon button.~~ **DONE** (`MaterialInspectorColumn.cs:35-41`).

**Events** (`06_Events.png`)
- **EV1 — PARTIAL.** Both panes use `MakeEmptyState` (`EventKeyInspectorColumn.cs:48`, `EventUsageColumn.cs:53`) but
  both pass a `null` action. Wire the middle pane's New action (R13).
- **EV2 — PARTIAL.** The footer is one status label (`EventKeyCatalogColumn.cs:307-312`) that can still wrap. Make it
  two badges in the Keys pane header (R01).
- **EV3 — OPEN.** Row meta still reads "maskable · key 16" (`EventKeyCatalogColumn.cs:250-251`, same order in
  `EventKeyInspectorColumn.cs:63-65`). Right-align as "16 · maskable"; names stay the title (R03).
- ~~A104-F3 — key rows sit four row-heights apart.~~ **DONE** (`EventKeyCatalogColumn.cs:209`: `MakeListRowSlot`,
  `fixedItemHeight` 22).

## 2. Files (all under `Packages/com.dotsanimationtoolkit/Editor/`)

- **Texture Packer:** `TexturePacker/TexturePackerPanel.cs`, `TexturePacker/PackOutputNodeView.cs`,
  `TexturePacker/TexturePackerGraphView.cs`. `PackOutputNodeView` and `TexturePackerGraphView` are on the
  `Conformance_I` allowlist; converting their inline styles lets them leave it (shrink only).
  `ImageCatalogColumn.cs` and `RecipeCatalogColumn.cs` are **done** — do not open them.
- **Flipbooks:** `Flipbooks/FlipbooksPanel.cs`, `Flipbooks/FlipbookFramesColumn.cs`, `Flipbooks/FlipbookCatalogColumn.cs`.
- **Clip Sets:** `ClipEditor/Authoring/ClipSetsPanel.cs`, `ClipEditor/Authoring/ClipPickerListElement.cs`.
- **Rigs:** `ClipEditor/Authoring/RigsPanel.cs` (no new file — see D3).
- **Materials:** `Materials/MaterialInspectorColumn.cs`.
- **Events:** `Events/EventKeyCatalogColumn.cs`, `Events/EventKeyInspectorColumn.cs`, `Events/EventUsageColumn.cs`.
- **Stage only:** `EditorStyleConformanceTests.cs` allowlist shrinks (the lead reports them in §7),
  `Documentation~/texture-packer.md` and `flipbooks.md`.

## 3. Read

Ground ranges at T0 by grepping the strings quoted in §1.1 — every quoted string and every `file:line` above was
verified on trunk `8a19368d` on 2026-09-19, so a grep that misses means the file moved, not that the finding is wrong.

Always read: the style guide §2–§4; A104's §7 For-integration block; `Shared/ToolkitChrome.cs`.

## 4. Fixtures

- **F1 `ClipPickerModelTests.FolderColumn_OmitsTheFolder_WhenItMatchesTheSetsFolder`** (CS1). **Note:** CS1's logic
  already shipped but is **untested** — it lives inline as `ComputeHomeFolderPath` + `clipIsOutsideHomeFolder`
  (`ClipPickerListElement.cs:142-159,291-292`). This is real logic that decides what a row shows, so it still earns
  its fixture; extract a static `ClipRowFolderResolver` if that is what makes it testable.
  - **Revert-to-fail:** always return the folder.
- UI changes carry no fixtures (R22: captures prove them). Every gate runs `EditorStyleConformanceTests`,
  `ClipEditorLayoutTests` and `PackagingConformanceTests`.

## 5. Tasks — lead

- [ ] **T0 — Ground.** Claim `a105`; confirm the §1.1 `file:line` anchors still resolve; log any drift in §7.
- [ ] **W1 — `[parallel-safe]`, one worker per line, ≤2 files each:**
  - T1 — TP2, TP3, TP4: `PackOutputNodeView.cs`.
  - T2 — TP1 + D2 (default setup, "Unsaved setup" header, Save as recipe, frame-all):
    `TexturePackerPanel.cs`, `TexturePackerGraphView.cs`.
  - T3 — FB2, FB4: `FlipbooksPanel.cs`.
  - T4 — FB3, FB5: `FlipbookFramesColumn.cs`, `FlipbookCatalogColumn.cs`.
  - T5 — CS2, CS3, CS4: `ClipSetsPanel.cs`, `ClipPickerListElement.cs`.
  - T6 — RG3, RG5, RG6 + D3's chips and Faces row: `RigsPanel.cs`.
  - T7 — MT2, MT3: `MaterialInspectorColumn.cs`.
  - T8 — EV1, EV3: `EventKeyCatalogColumn.cs`, `EventKeyInspectorColumn.cs`.
  - T9 — EV2 + EV1's usage pane: `EventUsageColumn.cs` (+ the Keys header badges, if in `EventKeyCatalogColumn.cs`,
    coordinate with T8 rather than double-editing).
  - T10 — F1 fixture + any extraction it needs.
- **Gate W1:** F1 + `EditorStyleConformanceTests` + `ClipEditorLayoutTests` + `PackagingConformanceTests`.
  F1's revert commit fails exactly F1.
- [ ] **W2:**
  - T11 — docs worker: `texture-packer.md` and `flipbooks.md` sentences that describe the recipe-required flow or the
    old layout.
- **Gate W2:** same set.
- [ ] **T12 — Close text.** §7 For-integration: CHANGELOG `## [0.58.0] — Asset tabs pass`; `Conformance_I` allowlist
  entries that can now be removed; any class A104 lacked; vault traps and a HANDOFF paragraph. Then `status a105 ready`.

## 6. Tasks — stage orchestrator

- [ ] **S1 — Merge** `a105`; compile; suites; allowlist shrink.
- [ ] **S2 — After captures** of tabs 01–06 → `Library/UIAudit/after-phase6-close/`. In §7, mark every residual id
  pass or fail against its capture. **Judge hard** against the reference composition, not just the ids.
- [ ] **S3 — Drives:** Texture Packer opens without a recipe and bakes an unsaved setup into `Assets/A105Scratch/`
  (deleted after); a Rigs chip opens its picker; the Target card writes through undo on a scratch rig copy.
- [ ] **S4 — Close:** CHANGELOG `0.58.0`, pin, HANDOFF, roadmap tick, status line.
- [ ] **S5 — no separate checkpoint.** The owner asked for one verification pass at the end of the whole batch
  (2026-09-19); A105's questions go into that one write-up:
  - **Q1.** Texture Packer's unsaved-setup flow — is "Save as recipe" where you'd look for it?
  - **Q2.** Rigs chips — right density for 34 rows?

## 7. Build log

### Phase 0 — reconciliation (stage, 2026-09-19, trunk `8a19368d`)

- **Baseline:** compile clean; EditMode **962/962**; PlayMode **304/304**.
- **Before-captures:** `Library/UIAudit/before-phase6-close/`, all fifteen tabs, 15 distinct md5s. The owner
  authorised the repaint-hard recipe for an unfocused Editor; the distinct hashes are the proof it is not stale.
- **First capture pass was thrown away:** it was taken with no asset selected, so Rigs, Ragdoll and others
  photographed their empty states rather than the rows the findings describe. Re-taken after setting
  `ClipEditorWindow.selection` (private field, reflection) to `NewRig` + `NewClipSet`.
- **Six verifiers** (one per tab) reported DONE/PARTIAL/OPEN per finding with `file:line` evidence; the stage
  cross-checked every DONE against its capture.
- **Result: 11 of the 28 original A105 findings are already on trunk**, plus two new ones the captures exposed
  (RG5 flat middle column, RG6 undesigned empty state). Residual is 19 findings — above the fold threshold, so
  A105 keeps its own worktree.

*(T0 grounding, gates, revert-to-fail, drift, For integration, S2 audit table, drives, close to follow.)*
