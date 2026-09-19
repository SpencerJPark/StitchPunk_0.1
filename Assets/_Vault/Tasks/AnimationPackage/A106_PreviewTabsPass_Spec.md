# A106 — Preview tabs pass: Retarget, VAT Bake, Capture, Ragdoll, Stats, Health

> **Status:** ✅ **BUILT 2026-09-19** as `0.59.0`, merged to trunk and integrated the same day (EditMode 968/968). Reconciled against trunk first — see §7 Phase 0; §1.1 is the residual only.
> A104 is merged, and eight later trunk passes plus A108 (`0.57.0`) closed several of this spec's original findings
> without updating it. **§1.1 below is the residual only** — the struck findings are already on trunk and must not be
> rebuilt. Runs in parallel with A105 and A107 (disjoint folders, seam in the session prompt).
> **Style guide (binding):** [`Docs/AnimationToolkit/EditorStyleGuide.md`](../../../../Docs/AnimationToolkit/EditorStyleGuide.md).
> Rules R01–R22; owner decisions SG-D1–SG-D8.
> **Executor:** `spec-lead` for §5; stage orchestrator for §6.
> **Before captures:** `Library/UIAudit/before-phase6-close/08_Retarget.png`, `09_VatBake.png`, `11_Ragdoll.png`,
> `13_Capture.png`, `14_Stats.png`, `15_Health.png` (taken 2026-09-19 with `NewRig` + `NewClipSet` selected).

## 0. Session prompt

`Assets/_Vault/Spencer/next-session-phase6-close-prompt.md` (2026-09-19).

## 1. Decisions

- **A106-D1 — Build with A104's layer only.** A missing class is reported in §7, never added to a shared sheet.
- **A106-D2 — Every preview tab has the same frame.** Reconciled: Retarget, Ragdoll and Stats already comply.
  **VAT Bake and Capture do not** — those two are the work.
  - **Asset bar:** one 36px row — fields left, facts as badges, the primary action right (R12).
  - **Columns:** 32px pane headers, **all on one baseline** (see RD5 — this is not satisfied by calling
    `MakePaneHeader`; it has to be true in the capture).
  - **Viewport:** a `ViewportFrameElement` with the standard rail (R20) and a centred `MakeEmptyState` overlay when
    there is nothing to show (R13).
  - **Transport:** one **shared** `TransportCoreElement` row directly under the viewport. Capture builds its own —
    replace it.
  - **Status:** one footer status row (R14). Warnings leave the asset bar and go to the footer with a tone dot.
- **A106-D3 — Settings columns are cards of property rows** (`MakeCard` + `MakePropertyRow`). A section heading
  followed by loose fields is gone, and so is a full-width primary at the bottom of a settings column. Reconciled:
  Ragdoll and Stats comply; **VAT Bake and Capture still end their settings column with a full-width primary.**
- **A106-D4 — Health's counts are filters.** Partly built: three `ToolbarToggle`s carry counts, but there is no
  unified segmented control and no "All" segment. Finish it as All · Errors · Warnings · Notes with counts (R11, R17).
  A finding's code moves from its title into a meta badge (R03).
- **A106-D5 — NEW (stage, 2026-09-19). Ragdoll must match its own reference image, not merely pass its ids.**
  The style guide's reference composition *is* this tab (`Assets/_Vault/Tasks/Claude/StyleGuideReferenceImage.png`).
  The before-capture differs from it in six ways that no existing id covers; they are RD7–RD11 below.

## 1.1 Audit — residual findings (reconciled 2026-09-19)

Struck = already on trunk, verified by code evidence and cross-checked against the before-captures. Do not rebuild.

**Retarget** (`08_Retarget.png`)
- **RT1 — PARTIAL.** The table card, its rows and `MakeEmptyState` are built
  (`RetargetTrackTableElement.cs:28-58`); per-row status is still a coloured glyph rather than a `MakeBadge`, and the
  header reads "Tracks (0)" not "Clip track · Rig part · Status". Make the status a badge and name the columns (R09).
- ~~RT2 — the transport is two lone buttons, one filled green.~~ **DONE** (`RetargetPreviewElement.cs:45-63`:
  `ViewportFrameElement` + `TransportCoreElement`, no green fill).
- ~~RT3 — the Roster footer's chips use block-bar glyphs.~~ **DONE** (`RosterCoverageStripElement.cs:24-96`:
  `MakeBadge` per chip, toned ok/warning/error).
- ~~A106-D2 / D3 for this tab.~~ **DONE** (`RetargetPanel.cs:77-159`; the tab has no settings column, so D3 is N/A).

**VAT Bake** (`09_VatBake.png`) — *the least-reconciled tab in this spec; all six ids survive.*
- **VB1 — OPEN.** The warning sentence still fills the asset bar (`VatBakePanel.cs:126-132`, set at `:491`).
  Move it to the footer with a warning badge in the bar (R14).
- **VB2 — PARTIAL.** Cards **Settings** and **Output** exist (`VatBakePanel.cs:150,175`) but the pane still carries a
  "Bake" title (`:147`), so "Bake" appears twice. Drop the column title (R03, R09).
- **VB3 — PARTIAL.** It is a property row now (`VatBakePanel.cs:160-165`) but the label is still
  "Fallback Samples / Second". Shorten to "Sample rate" with the full name in the tooltip (R08).
- **VB4 — OPEN.** A full-width Bake sits mid-column (`VatBakePanel.cs:196-200`). Bake is the primary in the asset
  bar, right (R12).
- **VB5 — OPEN.** Output Folder is a plain `TextField` with no picker (`VatBakePanel.cs:179`); no
  `PathPickerRowElement` anywhere in the file. Use it.
- **VB6 — OPEN.** The empty preview is a bottom status label with the transport and Clip dropdown always visible
  (`VatPreviewElement.cs:125-157`). Show a centred empty state ("Nothing baked yet" · why · Bake) and hide the
  transport and Clip picker until a set exists (R13, R17).

**Capture** (`13_Capture.png`)
- **CP1 — PARTIAL.** Label/field pairs wrap as a unit (`CapturePanel.cs:210-219`) but Source is still a
  `DropdownField`, not a segmented control (R11).
- **CP2 — PARTIAL.** The mid-row label bug is gone (`CapturePanel.cs:282-283`) but Width and Height are still two
  rows. Join them into one "Size" row as W × H, Preset its own row (R08).
- **CP3 — OPEN.** Background and Format are still `RadioButtonGroup`s (`CapturePanel.cs:322,333`), and the Colour
  field is greyed rather than hidden (`:593`). Segmented controls; hide Colour unless Colour is on (R11, R17).
- **CP4 — OPEN.** A full-width Capture is pinned to the bottom of Settings (`CapturePanel.cs:357-359`). It moves to
  the asset bar, right (R12) — this is also the D3 violation on this tab.
- **CP5 — PARTIAL.** A transport row exists (`CapturePanel.cs:242-254`) but it is **ad hoc**, not the shared
  `TransportCoreElement` (Capture is not among the seven files that use it), the slider is fixed at 280px, and the
  "Time" caption is kept. Use the shared element with a full-width scrub slider (R07, D2).
- ~~CP6 — the output hints float outside the card.~~ **DONE** (`CapturePanel.cs:344-355`: muted rows inside the
  Output card).
- **CP7 — NEW (from D2).** The footer status row lives inside the Settings column (`CapturePanel.cs:371`) rather than
  under the tab. Move it to the tab footer (R14).

**Ragdoll** (`11_Ragdoll.png`) — *judge against `StyleGuideReferenceImage.png`, not only the ids.*
- **RD1 — PARTIAL.** The picker no longer stretches (`RagdollBodiesColumn.cs:60-61`, `flexGrow = 0`) and an "Add"
  button is in the pane header (`:43-46`) — but the old target `PopupField` is **still there** as its own row under
  the header, so the tab now has two ways to add a body. Remove the row; the header action opens the picker as a menu.
- ~~RD2 — the body row label uses `toolkit-box__title`.~~ **DONE** (`RagdollBodiesColumn.cs:275`).
- **RD3 — PARTIAL.** The **Body** and **Rig settings** cards exist (`RagdollInspectorColumn.cs:63-91`) but the labels
  are still "Default Linear Damping" / "Default Angular Damping" with empty tooltips. Shorten to "Linear damping" /
  "Angular damping" with the full names in tooltips (R08).
- **RD4 — OPEN.** The viewport controls are still four stacked rows (`RagdollViewportElement.cs:54-105`). Make one
  32px toolbar: transport · scenery segmented · spacer · "Pose from" clip field (R07). **The reference image shows
  this row, and it is the single biggest difference from it.**
- **RD5 — OPEN, and a trap: it passes in code and fails on screen.** Every column calls `MakePaneHeader`
  (`RagdollBodiesColumn.cs:39`, `RagdollViewportElement.cs:54`, `RagdollInspectorColumn.cs:46`) yet "Bodies",
  "Viewport" and "Inspector" sit at three different heights in the capture. Find what re-offsets them (the Bodies
  column's extra header actions row is the suspect) and put all three on one 32px baseline (R06). **Prove it by
  capture, not by the builder call.**
- **RD6 — PARTIAL.** "No clip set assigned." is in the footer position (`RagdollViewportElement.cs:72,170`) but has
  no tone dot and no explanatory clause. The reference reads "No clip set assigned: bodies are shown at rest. Pick a
  clip set on Clip Sets to pose them." with an amber dot (R14).
- **RD7 — NEW.** The reference has a **"Search bodies" field** under the Bodies pane header. There is none.
- **RD8 — NEW.** The reference's body rows carry **muted mono meta** ("box", "box · root"). Ours show the name only.
- **RD9 — NEW.** The reference's Bodies **list body is a darker tone** than its column; ours is one flat grey. This
  is the owner's headline two-tone requirement, unmet on the very tab the style guide illustrates.
- **RD10 — NEW.** The reference's selected row carries the Unity selection fill; ours shows no selection state.
- **RD11 — NEW.** The reference's scenery control is a **segmented** "Ground · Walls · Stairs" in the viewport
  header; ours is a "Ground only" dropdown parked top-right (R11). Folds into RD4's toolbar.
- ~~A106-D2 / D3 for this tab.~~ **DONE** (`RagdollPanel.cs:44`; two cards of property rows, no bottom primary).

**Stats** (`14_Stats.png`) — *all three ids survive; only the frame was already right.*
- **ST1 — OPEN.** Out of Play mode every value is "—" (`StatsPanel.cs:219,317-320`) and `SetValue` (`:376-380`) never
  applies a muted class. Show the cards at rest with muted values and one centred banner card "Enter Play mode to
  read the world" (R13).
- **ST2 — OPEN.** `SparklineElement.Draw` bails out below 2 samples (`:87-91`), so the area is empty, and now/peak
  are a floating label (`StatsPanel.cs:117-118`). Draw the baseline and axis at rest; now/peak become meta badges in
  the card header (R13, R03).
- **ST3 — OPEN.** Snapshot is an enabled primary in the footer (`StatsPanel.cs:152-156`) with no `isPlaying` gate.
  Move it to the asset bar, disabled until Play (R12, R17).
- ~~A106-D2 / D3 for this tab.~~ **DONE** (`StatsPanel.cs:78,130-157` and `:95-137`).

**Health** (`15_Health.png`)
- **HL1 — PARTIAL (D4).** Three `ToolbarToggle`s carry counts (`HealthPanel.cs:105-124,197-199`) but there is no
  segmented control and no "All" segment. Finish D4.
- **HL2 — OPEN.** `titleLabel.text = finding.code + "  " + title` (`HealthFindingListElement.cs:144`). The title is
  the sentence; the code goes in a meta badge (R03).
- **HL3 — PARTIAL.** "Affected" is a flat row now (`HealthFindingDetailElement.cs:133-155`) but still a flex-grow
  `Button`, and the ghost Open only appears for some asset types. Asset icon, name, ghost Select (R09, R12).
- ~~HL4 — "How to fix" is two full-width grey buttons.~~ **DONE** (`HealthFindingDetailElement.cs:189-217`:
  fixed-width, left-aligned, each hint beside its button).
- **HL5 — OPEN.** `SeverityClassName` returns text-colour classes (`HealthFindingDetailElement.cs:74-76,310-320`).
  Make it a badge (R16).
- **HL6 — OPEN.** The repeated clause survives: `VatFreshnessValidation.cs:39` + `VatSourceHashResolver.cs:181` build
  "…has no baked rig yet and is unbaked: Not baked: this clip set has no VAT texture set." One sentence (R03).
- **HL7 — OPEN.** Scan is still first/left and the search field after the spacer (`HealthPanel.cs:94-133`). Swap
  them (R12).

## 2. Files (all under `Packages/com.dotsanimationtoolkit/Editor/`)

- **Retarget:** `Retarget/RetargetTrackTableElement.cs` only. `RetargetPanel.cs`,
  `RosterCoverageStripElement.cs` and `RetargetPreviewElement.cs` are **done** — do not open them.
- **VAT Bake:** `VatBaking/VatBakePanel.cs`, `VatBaking/VatPreviewElement.cs`.
- **Capture:** `Capture/CapturePanel.cs`, `Capture/CaptureViewportElement.cs`.
- **Ragdoll:** `Ragdoll/RagdollBodiesColumn.cs`, `Ragdoll/RagdollInspectorColumn.cs`,
  `Ragdoll/RagdollViewportElement.cs`, `Ragdoll/RagdollPanel.cs`.
- **Stats:** `Stats/StatsPanel.cs`, `Stats/SparklineElement.cs`.
- **Health:** `Health/HealthPanel.cs`, `Health/HealthFindingListElement.cs`, `Health/HealthFindingDetailElement.cs`,
  and for HL6 `VatBaking/VatFreshnessValidation.cs` + `VatBaking/VatSourceHashResolver.cs`.
- **Stage only:** docs pages `retarget-tab.md`, `capture-tab.md`, `ragdoll.md`, `stats-tab.md`, `health-tab.md`
  where a sentence describes the old layout (docs worker, W2).

## 3. Read

- T0 confirms the §1.1 `file:line` anchors still resolve (all verified on trunk `8a19368d`, 2026-09-19).
- **Look at `Assets/_Vault/Tasks/Claude/StyleGuideReferenceImage.png` before touching Ragdoll** (D5).
- Always read: the style guide §2–§4, A104's For-integration block, `Shared/ToolkitChrome.cs`,
  `Shared/ViewportFrameElement.cs`, `Shared/TransportCoreElement.cs`.

## 4. Fixtures

- **F1 `HealthFindingMessageTests.H06_MessageIsOneSentence_WithNoRepeatedClause`** (HL6). Confirmed absent on trunk.
  - **Asserts:** the built message contains "not baked" at most once (case-insensitive) and no ": Not baked:".
  - **Revert-to-fail:** restore the old concatenation.
- **UI:** no fixtures (R22). Every gate runs `EditorStyleConformanceTests`, `ClipEditorLayoutTests` and
  `PackagingConformanceTests`.

## 5. Tasks — lead

- [ ] **T0 — Ground.** Claim `a106`; confirm the §1.1 anchors; look at the reference image; log drift in §7.
- [ ] **W1 — `[parallel-safe]`, one worker per line, ≤2 files each:**
  - T1 — RT1: `RetargetTrackTableElement.cs`.
  - T2 — VB1–VB5: `VatBakePanel.cs`.
  - T3 — VB6: `VatPreviewElement.cs`.
  - T4 — CP1–CP4, CP7: `CapturePanel.cs`.
  - T5 — CP5: `CaptureViewportElement.cs` (+ the transport swap, coordinate with T4 on `CapturePanel.cs`).
  - T6 — RD1, RD7, RD8, RD9, RD10: `RagdollBodiesColumn.cs`.
  - T7 — RD3: `RagdollInspectorColumn.cs`.
  - T8 — RD4, RD6, RD11: `RagdollViewportElement.cs`.
  - T9 — ST1, ST3: `StatsPanel.cs`.
  - T10 — ST2: `SparklineElement.cs`.
  - T11 — HL1, HL7: `HealthPanel.cs`.
  - T12 — HL2: `HealthFindingListElement.cs`.
  - T13 — HL3, HL5: `HealthFindingDetailElement.cs`.
  - T14 — HL6 + F1: `VatFreshnessValidation.cs`, `VatSourceHashResolver.cs` (+ the fixture file; if that makes three,
    split the fixture to T14b).
- **Gate W1:** F1 + the conformance set. F1's revert fails exactly F1.
- [ ] **W2:**
  - T15 — RD5 (the one-baseline fix, which crosses the three Ragdoll columns): `RagdollPanel.cs` + whichever single
    column file the offset turns out to live in. Sequential after T6/T8.
  - T16 — docs worker (five pages, only stale sentences; split into two workers if more than two files change).
- **Gate W2:** same set.
- [ ] **T17 — Close text:** CHANGELOG `## [0.59.0] — Preview tabs pass`; missing classes; allowlist changes; traps;
  HANDOFF paragraph. Then `status a106 ready`.

## 6. Tasks — stage orchestrator

- [ ] **S1 — Merge** `a106`; compile; suites.
- [ ] **S2 — After captures** of tabs 08, 09, 11, 13, 14, 15 → `Library/UIAudit/after-phase6-close/`, with a rig and
  clip set selected. Mark every residual id in §7. **Put the Ragdoll after-capture beside
  `StyleGuideReferenceImage.png` and list every remaining difference** (D5).
- [ ] **S3 — Drives:** Ragdoll add a body from the header action on a scratch rig copy (undo restores);
  VAT Bake's empty state shows with no set; Capture's Source segmented switches its fields; each Health filter
  segment filters the list.
- [ ] **S4 — Close:** CHANGELOG `0.59.0`, pin, HANDOFF, roadmap tick, status line.
- [ ] **S5 — no separate checkpoint** (owner, 2026-09-19: one verification pass at the end of the batch). Questions
  for that write-up:
  - **Q1.** The Ragdoll tab against the style guide mock — anything still off?
  - **Q2.** Health's filter segments — right, or should the counts be badges that toggle?

## 7. Build log

### Phase 0 — reconciliation (stage, 2026-09-19, trunk `8a19368d`)

- **Baseline:** compile clean; EditMode **962/962**; PlayMode **304/304**.
- **Before-captures:** `Library/UIAudit/before-phase6-close/`, fifteen tabs, 15 distinct md5s. The first pass was
  discarded — it was taken with nothing selected, so Ragdoll photographed "0 bodies / List is empty" instead of its
  eleven rows. Re-taken with `NewRig` + `NewClipSet` set through `ClipEditorWindow.selection` by reflection.
- **Six verifiers** reported per-finding verdicts with `file:line`; the stage cross-checked every DONE against its
  capture. **That cross-check changed one verdict:** RD5 passes in code (all three columns call `MakePaneHeader`) and
  **fails on screen** (three headers, three heights) — recorded as the spec's own worked example of why R22 exists.
- **Five new Ragdoll findings (RD7–RD11)** came from holding the capture against `StyleGuideReferenceImage.png`:
  no search field, no row meta, a flat single-tone list, no selection fill, and a dropdown where the reference has a
  segmented control. None were in the original spec.
- **Result: 8 of the 31 original A106 findings are already on trunk**; residual is 26 (including the six new ones).
  Above the fold threshold, so A106 keeps its own worktree.

*(T0 grounding, gates, revert-to-fail, drift, For integration, S2 audit table, drives, close to follow.)*

### Phase 1 — build (spec-lead `a106`, 2026-09-19, branch `spec/a106`)

- **Commits:** `a3d13260` A106-W1 (14 source files + fixture F1), `f0c23f1` W1 fix (CapturePanel CS1061).
- **Gate 1:** `compile-errors` — `CapturePanel.cs:614 CS1061`, `SetValueWithoutNotify` on the `VisualElement` the
  Format segmented control now is. The RadioButtonGroup swap missed one write site. Fixed in the lead's own turn.
- **Gate 2:** **pass**, EditMode 32/32 over `EditorStyleConformanceTests`, `ClipEditorLayoutTests`,
  `PackagingConformanceTests`, `HealthFindingMessageTests`, `HealthRulesTests`, `VatBakePanelTests`,
  `VatSourceHashResolverTests`, `VatPreviewPlaybackTests` (each name namespace-qualified, one `--edit-mode` each).
- **Ids built:** RT1; VB1–VB6; CP1–CP4, CP7; RD1, RD3, RD4, RD6, RD7, RD8, RD10, RD11; ST1–ST3; HL1–HL3, HL5–HL7.
- **RD5** — cause found, fix unproven by capture. `.toolkit-pane-header` (`ClipEditorWindow.uss:258`) has **no
  height at all**, so each header is as tall as its own content: the Bodies header carried icon+text buttons, the
  Inspector header only a title, and `RagdollViewportElement` is not a `.toolkit-column`, so it also missed that
  class's `padding-top: 8px`. Measured on `11_Ragdoll.png`: "Viewport" ~27px above "Bodies", "Inspector" ~6px above.
  Fix: all three headers now set `style.height = 32f; style.flexShrink = 0f` (layout writes, Conformance_I-safe),
  the Bodies actions became icon-only ghosts, and the viewport root takes `style.paddingTop = 8f`.
  **The stage must confirm this by capture** — it cannot be proven from the builder call, which is the whole point
  of the finding.
- **RD9 was already true on trunk** and was left alone. Pixel-sampling `11_Ragdoll.png` reads the Bodies column at
  `#383838` and the list body at `#282828` — `toolkit-list-surface` is applied and resolving. What made it read flat
  was the missing search field and the absent selection fill above it, i.e. RD7 and RD10, now built. Do not
  "fix" the tone.
- **CP5 not built** (the ad-hoc Capture transport → shared `TransportCoreElement`). It lives in `CapturePanel.cs`,
  which T4 held for five other ids, so it was deferred rather than run as a conflicting second worker. **Residual.**
- **T16 docs not built:** the five tab pages are under `Packages/com.dotsanimationtoolkit/Documentation~/`, which the
  session seam reserves for the stage. **Stage owns them.**

### For integration

1. **`ClipEditorWindow.uss` — `.toolkit-pane-header` needs `min-height: 32px`** (R06). Three specs each set the
   height from C# instead because the sheet is stage-owned; one rule in the sheet would retire all of those writes.
2. **`ViewportFrameElement.SetEmptyState` takes no action button**, so R13's "the action that fills it" cannot be
   met on any viewport empty state (hit on VB6). It wants an optional `actionText` + `Action` overload, forwarded to
   `ToolkitChrome.MakeEmptyState`, which already accepts both.
3. No allowlist change is needed: `Conformance_I`'s only entry is
   `Editor/ClipEditor/Preview/RagdollPreviewSceneryProvider.cs`, untouched, and no new `// colour from data` line was
   added in this spec.
4. **`VatFreshnessBadgeElement.cs:56`** now lifts the first letter of the resolver's reason, because HL6 made those
   reasons lower-case sentence tails. Any new bare display of `VatSourceHashResolver.Resolve`'s `reason` must do the
   same.
5. Open risk for the capture pass: `HealthPanel`'s filter segments keep the old toggles' `<color=#hex>●</color>`
   rich text on a `Button`. Nobody could compile-check that a `Button` renders it; if it prints as markup, strip the
   tags and keep the count as plain text.
