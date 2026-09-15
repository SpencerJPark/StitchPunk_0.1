# A106 — Preview tabs pass: Retarget, VAT Bake, Capture, Ragdoll, Stats, Health

> **Status:** 📝 specced 2026-09-15, takes `0.58.0`; not built. **Needs A104 merged.** Runs in parallel with A105
> (disjoint folders).
> **Style guide (binding):** [`Docs/AnimationToolkit/EditorStyleGuide.md`](../../../../Docs/AnimationToolkit/EditorStyleGuide.md).
> Rules R01–R22; owner decisions SG-D1–SG-D8.
> **Executor:** `spec-lead` for §5; stage orchestrator for §6.
> **Before captures:** `Library/UIAudit/before/08_Retarget.png`, `09_VatBake.png`, `11_Ragdoll.png`,
> `13_Capture.png`, `14_Stats.png`, `15_Health.png`, plus the owner's `Assets/_Vault/Tasks/Claude/Screenshot 2026-09-15 145216.png` (Ragdoll).

## 0. Session prompt

Written by the stage after A104's checkpoint: `Assets/_Vault/Spencer/next-session-a105-a106-prompt.md`.

## 1. Decisions

- **A106-D1 — Build with A104's layer only.** A missing class is reported in §7, never added to a shared sheet.
- **A106-D2 — Every preview tab has the same frame.**
  - **Asset bar:** one 36px row — fields left, facts as badges, the primary action right (R12).
  - **Columns:** 32px pane headers.
  - **Viewport:** a `ViewportFrameElement` with the standard rail (R20) and a centred `MakeEmptyState` overlay when
    there is nothing to show (R13).
  - **Transport:** one `TransportCoreElement` row directly under the viewport.
  - **Status:** one footer status row (R14).

  Warnings leave the asset bar and go to the footer with a tone dot.
- **A106-D3 — Settings columns are cards of property rows** (`MakeCard` + `MakePropertyRow`). A section heading
  followed by loose fields is gone, and so is a full-width primary at the bottom of a settings column.
- **A106-D4 — Health's counts are filters.** "2 Errors / 3 Warnings / 1 Note" become a segmented filter (All · Errors ·
  Warnings · Notes, with counts), so they read as controls, not buttons (R11, R17). A finding's code moves from its
  title into a meta badge (R03).

## 1.1 Audit — findings to fix (A104 fixes G1–G8 first, including the magenta)

**Retarget** (`08_Retarget.png`)
- RT1 — The Tracks pane is a single loose row "✓ UpperRightArm → RightUpperArm" above 800px of nothing. Make it a
  table card: header "Clip track · Rig part · Status", flat rows, a status badge per row, and `MakeEmptyState` when
  the clip has no tracks (R09, R13).
- RT2 — The transport is two lone buttons centred under the preview, one filled green (R16, R20). Use the standard
  `TransportCoreElement` row; playing state is the play icon, not a green fill.
- RT3 — The Roster footer's chips with block-bar glyphs become badges in the footer status row: "NewRig 1/1" ok tone,
  "VatSampleTentacleRig 0/1" warning tone (R14, R21).

**VAT Bake** (`09_VatBake.png`)
- VB1 — An orange warning sentence fills the right half of the asset bar (R14). Move it to the footer, with a warning
  badge in the bar.
- VB2 — The left column reads "Bake / Settings / … / Output / … / Bake": two "Bake" headings and loose headers (R03,
  R09). Use cards **Settings** and **Output**; no column title.
- VB3 — "Fallback Samples / Second" pushes its field right of "Flavor"'s (R08). Property rows; the label becomes
  "Sample rate" with the full name in the tooltip.
- VB4 — A full-width blue Bake mid-column (R12). Bake is the primary in the asset bar, right.
- VB5 — Output Folder is an empty text field with no picker. Use `PathPickerRowElement`.
- VB6 — The empty preview says "No VAT texture set to preview." at the bottom above a disabled transport and an
  empty Clip dropdown (R13, R17). Show a centred empty state ("Nothing baked yet" · why · Bake), with the transport
  and Clip picker hidden until a set exists.

**Capture** (`13_Capture.png`)
- CP1 — The asset bar is two misaligned rows with truncated fields "NewClipSet (Clip S", "NewRig (Ri" (R01, R07). Make
  it one row: Source segmented control, then the fields that source needs, each with a minimum width; overflow wraps
  as a whole aligned row.
- CP2 — Width and Height sit on one row with the second label mid-row (R08). Use one "Size" property row with a
  joined W × H field pair, and Preset as its own row.
- CP3 — The Background (Transparent/Colour) and Format (PNG Sequence/GIF) radio groups become segmented controls;
  the Colour field shows only when Colour is on (R11, R17).
- CP4 — A full-width blue Capture at the bottom of Settings (R12). It moves to the asset bar, right.
- CP5 — "Time" is a label with a 20px slider under the preview (R07). Use a transport row with a full-width scrub
  slider.
- CP6 — The output hints "Saved as NewClip" and "Writes to Assets/Generated/…" become muted meta inside the Output
  card rows (R05).

**Ragdoll** (`11_Ragdoll.png`, owner screenshot)
- RD1 — The add-body `PopupField` has `style.flexGrow = 1` (`RagdollBodiesColumn.cs:57`) and fills 400px as a grey box
  (R10). It becomes a ghost "+" in the Bodies pane header that opens the target picker as a menu; the ListView is the
  only growing child.
- RD2 — The body row label uses `toolkit-box__title` (`MakeBodyRow`); switch it to `toolkit-list-row__title`, so rows
  show their full height (R01, with A104-D6).
- RD3 — The inspector's "Default Linear Damping" and "Default Angular Damping" push their fields right (R08). Use
  cards **Body** (when selected) and **Rig settings**, with property rows labelled "Linear damping" and
  "Angular damping" and the full names in tooltips.
- RD4 — The viewport controls are two rows: transport + "Ground only", then "Pose from clip" with a floating
  checkbox, "No clip bound" and a slider with no track (R07). Make one 32px toolbar: transport · scenery segmented ·
  spacer · "Pose from" clip field.
- RD5 — "Viewport" sits 12px above "Bodies"/"Inspector" (R06), and "Inspector", "Select a body.", "Rig settings" start
  at three x positions (R05). Use 32px pane headers and one inset.
- RD6 — "No clip set assigned." sits under the viewport as a loose line. Put it in the status footer with a warning
  dot (R14).

**Stats** (`14_Stats.png`)
- ST1 — Out of Play mode every value is "—", so the tab reads as broken. Show the cards at rest with muted values and
  one centred banner card "Enter Play mode to read the world" (R13).
- ST2 — The Events/frame card has an empty sparkline area with "now 0 peak 0" floating in it. The sparkline draws
  its baseline and axis at rest; now/peak become meta badges in the card header (R13, R03).
- ST3 — Snapshot is an enabled blue primary in the footer while nothing is playing (R12, R17). It becomes the asset
  bar's primary, disabled until Play.

**Health** (`15_Health.png`)
- HL1 — The error/warning/note counts look like buttons (R17): D4 segmented filter.
- HL2 — Finding titles carry codes, e.g. "H11 V38: clip set doesn't bind…" (R03). The title is the sentence; codes go
  in a meta badge.
- HL3 — "Affected" is a full-width grey button with centred "▸ VatSampleTentacleClips". Use flat rows (asset icon,
  name, ghost Select) (R09, R12).
- HL4 — "How to fix" is two full-width grey buttons with hints under them. Use secondary buttons left-aligned, each
  hint beside its button (R12, R07).
- HL5 — The severity is plain red text top-right: make it a badge (R16).
- HL6 — The detail copy repeats itself: "has no baked rig yet and is unbaked: Not baked: this clip set has no VAT
  texture set." (R03). Find the H06 message builder (grep "is unbaked") and make it one sentence.
- HL7 — "Scan project" sits left as the primary; it moves right per R12 and the search field moves left.

## 2. Files (all under `Packages/com.dotsanimationtoolkit/Editor/`)

- **Retarget:** `Retarget/RetargetPanel.cs`, `Retarget/RetargetTrackTableElement.cs`,
  `Retarget/RosterCoverageStripElement.cs`, `Retarget/RetargetPreviewElement.cs`.
- **VAT Bake:** `VatBaking/VatBakePanel.cs`, `VatBaking/VatPreviewElement.cs`.
- **Capture:** `Capture/CapturePanel.cs`, `Capture/CaptureViewportElement.cs`.
- **Ragdoll:** `Ragdoll/RagdollBodiesColumn.cs`, `Ragdoll/RagdollInspectorColumn.cs`,
  `Ragdoll/RagdollViewportElement.cs`, `Ragdoll/RagdollPanel.cs`.
- **Stats:** `Stats/StatsPanel.cs`, `Stats/SparklineElement.cs`.
- **Health:** `Health/HealthPanel.cs`, `Health/HealthFindingListElement.cs`, `Health/HealthFindingDetailElement.cs`,
  and the H06 message source (T0 grep).
- **Stage only:** as the lead contract; docs pages `retarget-tab.md`, `capture-tab.md`, `ragdoll.md`, `stats-tab.md`,
  `health-tab.md` only where a sentence describes the old layout (docs worker, W2).

## 3. Read

- T0 greps every quoted string in §1.1 to its file and line.
- Always read: the style guide §2–§4, A104's For-integration block, `Shared/ToolkitChrome.cs` (post-A104),
  `Shared/ViewportFrameElement.cs`, `Shared/TransportCoreElement.cs`.

## 4. Fixtures

- **F1 `HealthFindingMessageTests.H06_MessageIsOneSentence_WithNoRepeatedClause`** (HL6).
  - **Asserts:** the built message contains "not baked" at most once (case-insensitive) and no ": Not baked:".
  - **Revert-to-fail:** restore the old concatenation.
- **UI:** no fixtures (R22). Every gate runs `EditorStyleConformanceTests`, `ClipEditorLayoutTests` and
  `PackagingConformanceTests`, plus `VatBakePanel`/`RagdollViewport` tests where they exist (grep
  `Tests/EditMode` for the panel names at T0).

## 5. Tasks — lead

- [ ] **T0 — Ground.** Claim `a106`; grep §1.1's strings; read A104's For-integration; log drift.
- [ ] **W1 — `[parallel-safe]`, one worker per line, ≤2 files each:**
  - T1 — RT1: `RetargetTrackTableElement.cs`.
  - T2 — RT2, RT3: `RetargetPreviewElement.cs`, `RosterCoverageStripElement.cs`.
  - T3 — VB1–VB5: `VatBakePanel.cs`.
  - T4 — VB6: `VatPreviewElement.cs`.
  - T5 — CP1–CP4, CP6: `CapturePanel.cs`.
  - T6 — CP5: `CaptureViewportElement.cs`.
  - T7 — RD1, RD2: `RagdollBodiesColumn.cs`.
  - T8 — RD3: `RagdollInspectorColumn.cs`.
  - T9 — RD4: `RagdollViewportElement.cs`.
  - T10 — ST1–ST3: `StatsPanel.cs`, `SparklineElement.cs`.
  - T11 — HL1, HL7: `HealthPanel.cs`.
  - T12 — HL2: `HealthFindingListElement.cs`.
  - T13 — HL3–HL5: `HealthFindingDetailElement.cs`.
  - T14 — HL6 + F1: the H06 message source + the new fixture.
- **Gate W1:** F1 + the conformance set. F1's revert fails exactly F1.
- [ ] **W2:**
  - T15 — RD5, RD6 + RT/VB/CP asset-bar primaries wired where a panel file owns the bar: `RagdollPanel.cs`,
    `RetargetPanel.cs`.
  - T16 — docs worker (five pages, only stale sentences; split into two workers if more than two files change).
- **Gate W2:** same set.
- [ ] **T17 — Close text:**
  - CHANGELOG `## [0.58.0] — Preview tabs pass`;
  - missing classes;
  - allowlist changes;
  - traps;
  - HANDOFF paragraph.

  Then `status a106 ready`.

## 6. Tasks — stage orchestrator

- [ ] **S1 — Merge** `a106`; compile; suites.
- [ ] **S2 — After captures** of tabs 08, 09, 11, 13, 14, 15 → `Library/UIAudit/after-a106/`. Mark every RT/VB/CP/RD/ST/HL
  id pass or fail in §7. **Judge hard** against the style guide's reference Ragdoll composition: Ragdoll must match
  it closely; list every remaining difference.
- [ ] **S3 — Drives:**
  - **Ragdoll:** add a body from the header "+" on a scratch rig copy (undo restores); the list shows 11 full-height
    rows.
  - **VAT Bake:** the empty state shows with no set.
  - **Capture:** Source segmented switches its fields.
  - **Health:** each filter segment filters the list.
- [ ] **S4 — Close:** CHANGELOG `0.58.0`, pin, HANDOFF, roadmap tick, status line.
- [ ] **S5 — ⏸ owner checkpoint** (real stop while present):
  - **Show:** before/after of the six tabs.
  - **Q1.** The Ragdoll tab against the style guide mock — anything still off?
  - **Q2.** Health's filter segments — right, or should the counts be badges that toggle?

## 7. Build log

*(T0 grounding, gates, revert-to-fail, drift, For integration, S2 audit table, drives, close.)*
