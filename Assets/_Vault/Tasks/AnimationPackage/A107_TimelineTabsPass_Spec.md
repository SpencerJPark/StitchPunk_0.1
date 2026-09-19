# A107 — Timeline tabs pass: Clip Editor, Actor Profiles, Cutscenes

> **Status:** 📝 specced 2026-09-15, **reconciled against trunk 2026-09-19** (see §7 Phase 0); takes `0.60.0`; not built.
> **"Runs alone" is overturned** (stage, 2026-09-19): its stated reason was that it edits files the other two stay out
> of, which is the definition of parallel-safe. It runs alongside A105 and A106. What genuinely had to wait — the
> final fifteen-tab audit — is a stage step after all three merge.
> **§1.1 below is the residual only** — the struck findings are already on trunk and must not be rebuilt.
> **Style guide (binding):** [`Docs/AnimationToolkit/EditorStyleGuide.md`](../../../../Docs/AnimationToolkit/EditorStyleGuide.md).
> Rules R01–R22; owner decisions SG-D1–SG-D8.
> **Executor:** `spec-lead` for §5, granted the window files in A107-D4; stage orchestrator for §6.
> **Before captures:** `Library/UIAudit/before-phase6-close/07_ClipEditor.png`, `10_ActorEditor.png`,
> `12_CutsceneEditor.png` (taken 2026-09-19 with `NewRig` + `NewClipSet` selected).

## 0. Session prompt

`Assets/_Vault/Spencer/next-session-phase6-close-prompt.md` (2026-09-19).

## 1. Decisions

- **A107-D1 — The Clip Editor still leads the layout (A72), now in the style guide's skin.**
  - **Unchanged:** the four panes, the transport as the middle strip and the timeline below.
  - **What changes:** headers, buttons, toggles, empty states and the ruler's resting state. The layout does not move.
- **A107-D2 — Actor Profiles layer rows: hover play, detail for the rest (SG-D7).** **Fully open** — nothing on trunk
  implements any part of it.
  - **Row:** each animation row shows its name and one play icon, visible on hover and while that animation plays.
  - **Stop:** lives only in the preview transport.
  - **Speed:** the per-row number field moves to an **Animation** card in the Actor Inspector, for the selected row.
  - **Layer header:** eye toggle, live dot, name, default-animation dropdown, ghost trash.
- **A107-D3 — Actor Profiles gets the standard asset bar.** **Fully open** — no `MakeAssetBar` call exists in any
  ActorEditor file. The Clip Set and Rig fields stacked above the Profiles list move into a 36px asset bar across the
  tab, and the four column headers land on one baseline (R06).
- **A107-D4 — Window files granted:**
  - `ClipEditorWindow.uxml` (Clip Editor pane headers, transport and timeline-header markup only);
  - `ClipEditorWindow.cs` and `ClipEditorWindow.ComponentStack.cs` (Clip Editor header/button construction only);
  - `ClipEditorTransport.cs`, `ClipEditorWindow.CameraNavigation.cs`.
- **A107-D5 — NEW (stage, 2026-09-19). The cutscene panel is the one surface no pass has touched, and it is a
  restyle, not a rewrite.** Measured adoption of the shared layer across the three cutscene files: `MakeCard` **0**,
  `MakePropertyRow` **0**, `MakeBadge` **0**, `MakeStatusRow` **0**, `toolkit-list-row` **0**,
  `ViewportFrameElement` **0**; the only hits are `MakeEmptyState` ×1 and `toolkit-list-surface` ×1, both in
  `CutsceneCastPanel.cs`. `CutsceneEditorPanel.cs` is ~5,500 lines. **Workers get grepped line ranges only, and the
  scope is CT1–CT6 — do not attempt a general conversion of that file.**
- **A107-D6 — The Cutscenes magenta is closed as a scene defect, not the toolkit's.** `Faceware.mat` named a deleted
  shader guid; the stage repointed it to `Shader Graphs/2DShader` on 2026-09-19 and photographed the fix. Two
  root-level `Quad` objects in `TestArea` still carry a genuinely **null** material and remain magenta — also the
  scene's, also not this spec's. **No lead touches either.**

## 1.1 Audit — residual findings (reconciled 2026-09-19)

Struck = already on trunk, verified by code evidence and cross-checked against the before-captures. Do not rebuild.

**Clip Editor** (`07_ClipEditor.png`)
- ~~CE1 — the Clips list gets a 70px slot.~~ **DONE** (`ClipEditorWindow.uxml:26`:
  `fixed-pane-initial-dimension="150"`, ~8 rows, remembered in `EditorPrefs`).
- **CE2 — PARTIAL.** Tooltips and disable-without-selection are in (`ClipListPane.cs:51,61,197`) but the controls are
  still **icon + text**, not icon-only ghost buttons (R12, R21).
- **CE3 — OPEN.** Still a `previewStatusLabel` line above the viewport
  (`ClipEditorWindow.cs:3863,3880`); no empty-state overlay. Centred overlay when no clip; otherwise the footer (R14).
- **CE4 — OPEN.** The rail still resolves `EditorGUIUtility.IconContent` with no `ApplyIconTone`
  (`ClipEditorWindow.cs:2848,2882`), so the seven icons stay mixed-colour. Standard rail: gizmo modes as one
  segmented group, then camera, then toggles, one tone, on-state filled (R20, R21).
- **CE5 — PARTIAL.** Attachment Points has a heading and per-row actions
  (`ClipEditorWindow.ComponentStack.cs:1040,1062`) but the unresolved case keeps a full-width button and the group is
  not a `MakeCard` with a ghost header action (R12, R13).
- **CE6 — OPEN.** "All" and "Selected" are still two separate `Button`s (`ClipEditorWindow.uxml:120-121`). One 32px
  row, 4px gaps, 1px dividers between groups, All · Selected as a segmented control (R07, R11).
- ~~CE7 — Snap and Auto Key look like plain buttons.~~ **DONE** (`ClipEditorWindow.uxml:129-132`: `ToolbarToggle`
  for both, ghost Add Event, pivot dropdown).
- ~~CE8 — the ruler runs from −30 with blank track headers.~~ **DONE** (`TimelinePane.cs:281-286`: `MakeEmptyState`
  "Pick a clip…to see its tracks"; no `-30` anywhere). **F2 is therefore deleted** — see §4.

**Actor Profiles** (`10_ActorEditor.png`) — *the least-reconciled tab in the batch.*
- **AP1 — OPEN.** Clip Set and Rig fields are still stacked above the Profiles list
  (`ActorEditorProfilesColumn.cs:36-60`), and the four column headers sit at four heights in the capture
  (Profiles ~192, Layers ~104, Preview ~100, Actor Inspector ~98 pt). D3 fixes both (R06).
- **AP2 — OPEN.** Rows still carry play, stop **and** a number field together
  (`ActorEditorLayersColumn.cs:276-291`), and names truncate. D2 (R01).
- **AP3 — OPEN.** `starterButton` is a plain `Button` (`ActorEditorLayersColumn.cs:213-234`); the default-animation
  control is not a dropdown in the layer card header (R12).
- **AP4 — OPEN.** One chip, `summaryButton.text = "… err … warn"` (`ValidationBadgeElement.cs:240-241`). Two badges
  (R16).
- ~~AP5 — the Rig field is truncated and "+ Clip Set" is a full-width grey button.~~ **DONE**
  (`ActorEditorInspectorColumn.cs:234,255`: `MakePropertyRow` + Ghost variant).
- **AP6 — OPEN. (Verdict corrected by the stage.)** A verifier marked this DONE after finding no redundant arrow in
  `ActorEditorInspectorColumn.cs` — but the repetition is in the **preview transport row**, and the capture shows
  "Direction [South East ▾]  SouthEast → SouthEast" plainly. Show the resolved text only when it differs from the
  pick (R03). Grep `ActorEditorPanel.cs` for the arrow, not the inspector column.
- **AP7 — OPEN.** `LayerEventStripElement.cs:22-23` fixes its label column at 72px with no shared constant tying it
  to the layer name column (`ActorEditorLayersColumn.cs:208-211`) (R05).
- **AP8 — NEW (stage, from the capture).** The Preview's "No actor to preview / Pick a profile in the…layers play
  here." text is drawn **over a live actor render**, so the sentence crosses the figure and is unreadable. Either
  suppress the render behind the overlay or put the empty state on a solid surface (R13, R18).
- **A107-D2 — OPEN** (no hover play, stop still inline, speed still per-row).
- **A107-D3 — OPEN** (no `MakeAssetBar` in any ActorEditor file).

**Cutscenes** (`12_CutsceneEditor.png`)
- **CT1 — OPEN.** "No cutscene loaded. Pick one above, or create a new one." is a plain centred `toolkit-hint` Label
  over the scene render (`CutsceneEditorPanel.cs:1090-1101`). Centred empty-state card on a solid surface, New as its
  action (R13, R18).
- **CT2 — PARTIAL.** "+ Actor" and "+ Prop" are ghost now and the list has an empty state
  (`CutsceneCastPanel.cs:73-92,122`), but **Sync** (`:87`) is still a filled Secondary. Make it a ghost icon button in
  the Cast pane header (R12).
- **CT3 — OPEN, and it needs a product call, not just a pass.** The rail is opaque **by an explicit design comment**
  (`CutsceneEditorPanel.cs:1033-1037`, `ClipEditorWindow.uss:1505-1529`), and no 0.4-disabled rule targets it. The
  style guide's R20 wants one rail style on a translucent card. **Record the owner's answer in §7 before changing it.**
- **CT4 — PARTIAL.** Auto Key has a filled recording state (`CutsceneEditorPanel.cs:576-591`, `AutoKey.cs:37,43`);
  **Skip Holds** (`:587`) is a bare `ToolbarToggle` with no on-state (R17).
- **CT5 — OPEN.** Still `new Label("Assign a Cutscene asset above.")` (`CutsceneEditorPanel.cs:4456`). Empty state (R13).
- **CT6 — NEW (A108 carry-over).** The four icon+word buttons A108 left for this pass now inherit the Secondary
  default from `ToolkitIcons.MakeIconTextButton`. Give each its intended variant:
  `CutsceneCastPanel.cs:87` (Sync — folds into CT2), `CutsceneEditorPanel.cs:406` (New Cutscene),
  `CutsceneEditorPanel.cs:550` (transport/continue), `CutsceneEditorPanel.cs:2411` (addPartTrackButton).
- **CT7 — NEW (stage, from the capture).** "All" and "Playhead" at the right of the cutscene transport are two bare
  text buttons, the same defect as CE6. Make them a segmented control (R11).

## 2. Files (all under `Packages/com.dotsanimationtoolkit/Editor/ClipEditor/`)

- **Clip Editor:** `Panes/ClipListPane.cs`, `Panes/RigHierarchyPane.cs`, `ClipEditorWindow.uxml`,
  `ClipEditorWindow.cs`, `ClipEditorWindow.ComponentStack.cs`, `ClipEditorWindow.CameraNavigation.cs` — D4.
  `Panes/TimelinePane.cs`, `Panes/TimelinePane.View.cs` and `TimeRulerElement.cs` are **done** (CE7, CE8) — do not
  open them.
- **Actor Profiles:** `ActorEditor/ActorEditorPanel.cs`, `ActorEditor/ActorEditorLayersColumn.cs`,
  `ActorEditor/ActorEditorProfilesColumn.cs`, `ActorEditor/ValidationBadgeElement.cs`,
  `ActorEditor/LayerEventStripElement.cs` (allowlisted). `ActorEditorInspectorColumn.cs` is needed **only** for D2's
  Animation card (AP5 is done).
- **Cutscenes:** `Cutscene/CutsceneEditorPanel.cs` (~5,500 lines: grepped line ranges only, scope CT1/CT3/CT4/CT5/CT6/CT7),
  `Cutscene/CutsceneCastPanel.cs`, `Cutscene/CutsceneViewportElement.cs`.
- **Nobody touches** `Editor/ClipEditor/Shared/`, `ClipEditorWindow.uss`, `EditorStyleConformanceTests.cs`,
  `package.json`, `CHANGELOG.md`, `HANDOFF.md` or `Documentation~/` — those are the stage's, at integration.

## 3. Read

- T0 confirms the §1.1 anchors still resolve (all verified on trunk `8a19368d`, 2026-09-19).
- Always read: the style guide §2–§4, A104's For-integration block, `Shared/ToolkitChrome.cs`,
  `Shared/ViewportFrameElement.cs`.
- `ClipEditorLayoutTests` (element names) before any UXML change.

## 4. Fixtures

- **F1 `ActorEditorLayerRowTests.SelectingAnAnimationRow_ShowsItsSpeed_InTheAnimationCard`** (D2).
  - **Setup:** a detached `ActorEditorPanel` with a `CreateInstance` profile.
  - **Asserts:** after selecting an animation row, the inspector's Animation card field reads that animation's speed.
  - **Revert-to-fail:** leave the card unbound.
- ~~**F2 `TimeRulerRestTests.WithNoClip_FirstLabelIsZero`**~~ — **deleted at reconciliation.** CE8 already shipped, the
  fixture never existed, and the remaining behaviour is drawing code, which the roadmap rule says to prove by capture
  rather than pin with a test.
- Every gate runs `ClipEditorLayoutTests` (update names only if D4 markup renames any — prefer not to),
  `EditorStyleConformanceTests`, `PackagingConformanceTests`, `ActorPreviewComposerTests`.

## 5. Tasks — lead

- [ ] **T0 — Ground.** Claim `a107`; confirm the §1.1 anchors; log drift in §7.
- [ ] **W1 — `[parallel-safe]`, ≤2 files each:**
  - T1 — CE2: `ClipListPane.cs`, `RigHierarchyPane.cs`.
  - T2 — CE5: `ClipEditorWindow.ComponentStack.cs`.
  - T3 — CE6: `ClipEditorWindow.uxml`, `ClipEditorTransport.cs`.
  - T4 — AP1 + D3 (asset bar, one header baseline): `ActorEditorPanel.cs`, `ActorEditorProfilesColumn.cs`.
  - T5 — AP2, AP3 + D2's row: `ActorEditorLayersColumn.cs`.
  - T6 — AP4: `ValidationBadgeElement.cs`.
  - T7 — AP7: `LayerEventStripElement.cs`.
  - T8 — CT2, CT6's Sync: `CutsceneCastPanel.cs`.
  - T9 — CT1: `CutsceneViewportElement.cs`.
- **Gate W1:** the conformance set + `ActorPreviewComposerTests`.
- [ ] **W2:**
  - T10 — CE3, CE4: `ClipEditorWindow.cs`, `ClipEditorWindow.CameraNavigation.cs`.
  - T11 — AP6, AP8: `ActorEditorPanel.cs` (after T4).
  - T12 — D2's Animation card: `ActorEditorInspectorColumn.cs` (after T5).
  - T13 — CT4, CT5, CT6's three panel sites, CT7: `CutsceneEditorPanel.cs` (grepped ranges only).
  - T14 — F1 fixture.
- **Gate W2:** F1 + the conformance set + `ActorPreviewComposerTests`. The revert-to-fail commit fails exactly F1.
- [ ] **T15 — Close text:** CHANGELOG `## [0.60.0] — Timeline tabs pass`; allowlist shrink; traps; HANDOFF paragraph.
  Then `status a107 ready`.

**CT3 is not in a wave.** It needs the owner's answer on the rail's deliberate opacity; the stage carries the
question and builds it on trunk in Phase 3 if the answer says to change it.

## 6. Tasks — stage orchestrator

- [ ] **S1 — Merge** `a107` (third, after `a105` and `a106`); compile; suites.
- [ ] **S2 — After captures of all 15 tabs** → `Library/UIAudit/after-phase6-close/`, with a rig, clip set and
  profile selected. Mark every CE/AP/CT id in §7.
- [ ] **S3 — Drives:** Clip Editor — Snap on/off is visible, the clip list shows ≥8 rows; Actor Profiles — hover play
  works and the Animation card edits speed through undo on `ActorEditorScratch.profile`, never `MaleCitizen.profile`;
  Cutscenes — the empty-state New creates a scratch cutscene under `Assets/A107Scratch/` (deleted after).
- [ ] **S4 — Close:** CHANGELOG `0.60.0`, pin, HANDOFF, roadmap tick; the style guide gains a "Shipped" line naming
  A104–A108 and this batch; `Code_Audit_2026-09.md` gets a note that the UI pass is done.
- [ ] **S5 — the final fifteen-tab R01–R22 audit** (this is the step that genuinely had to wait for all three merges):
  a table per tab in §7, every rule either met or named as an owner call.
- [ ] **S6 — one verification write-up for the whole batch** (owner, 2026-09-19: no per-spec checkpoints):
  - **Q1.** Is this the product you'd put in a store listing? If not, which three screens are furthest from it?

## 7. Build log

### Phase 0 — reconciliation (stage, 2026-09-19, trunk `8a19368d`)

- **Baseline:** compile clean; EditMode **962/962**; PlayMode **304/304**.
- **Before-captures:** `Library/UIAudit/before-phase6-close/`, fifteen tabs, 15 distinct md5s, re-taken with
  `NewRig` + `NewClipSet` selected after the first pass photographed empty states.
- **Verifier budget note:** the Clip Editor's CE1–CE8 brief capped a verifier at 25 turns with **no report**. Per
  CLAUDE.md a capped agent is never resumed; it was re-split into CE1–CE4 and CE5–CE8, each with a per-finding tool
  budget and a "partial report is required" instruction, and both returned inside budget. Ten files in one brief is
  over the line for a verifier.
- **The stage's capture cross-check changed one verdict:** **AP6** was reported DONE on the evidence of
  `ActorEditorInspectorColumn.cs`, but the capture shows "SouthEast → SouthEast" in the **preview transport row**.
  The verifier searched the wrong file. Recorded in §1.1 as OPEN.
- **Three new findings** came from the captures: AP8 (empty-state text over a live render), CT7 (bare All/Playhead
  buttons) and CT6's scope (A108's four carried-over buttons, located to exact lines).
- **D5's adoption measurement** is what sizes this spec: the cutscene panel and viewport have **zero** shared-layer
  adoption, which is why CT work is scoped to named ids rather than a conversion.
- **D6:** `Faceware.mat` repointed from the deleted guid `e5e6305b…` to `Shader Graphs/2DShader`
  (`dd0290a2…`, the same `fileID`, and the shader 27 sibling materials already use). Verified in the Editor
  (`Faceware shader = Shader Graphs/2DShader`) and photographed: `12_CutsceneEditor.png` (before, magenta faces) vs
  `12_CutsceneEditor_facewareFixed.png` (after, faces correct). Two root-level `Quad` renderers with null materials
  are the only magenta left in the scene.
- **Result: 3 of the 20 original A107 findings are already on trunk**; residual is 20 (including the three new ones
  and CT6). The largest of the three specs. Keeps its own worktree, and **runs in parallel**, overturning the
  original "runs alone".

*(T0 grounding, gates, revert-to-fail, drift, For integration, S2 audit table, drives, close to follow.)*
