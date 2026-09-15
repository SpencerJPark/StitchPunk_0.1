# A107 — Timeline tabs pass: Clip Editor, Actor Profiles, Cutscenes

> **Status:** 📝 specced 2026-09-15, takes `0.59.0`; not built. **Needs A104, A105 and A106 merged.** Runs **alone**:
> it edits the Clip Editor's panes and transport, the window's partials and the cutscene panel, which earlier specs
> stayed out of.
> **Style guide (binding):** [`Docs/AnimationToolkit/EditorStyleGuide.md`](../../../../Docs/AnimationToolkit/EditorStyleGuide.md).
> Rules R01–R22; owner decisions SG-D1–SG-D8.
> **Executor:** `spec-lead` for §5, granted the window files in A107-D4; stage orchestrator for §6.
> **Before captures:** `Library/UIAudit/before/07_ClipEditor.png`, `10_ActorEditor.png`, `12_CutsceneEditor.png`.

## 0. Session prompt

Written by the stage after the A105/A106 checkpoint: `Assets/_Vault/Spencer/next-session-a107-prompt.md`.

## 1. Decisions

- **A107-D1 — The Clip Editor still leads the layout (A72), now in the style guide's skin.**
  - **Unchanged:** the four panes, the transport as the middle strip and the timeline below.
  - **What changes:** headers, buttons, toggles, empty states and the ruler's resting state. The layout does not move.
- **A107-D2 — Actor Profiles layer rows: hover play, detail for the rest (SG-D7, owner 2026-09-15).**
  - **Row:** each animation row shows its name and one play icon, visible on hover and while that animation plays.
  - **Stop:** lives only in the preview transport.
  - **Speed:** the per-row number field is the animation's speed; it moves to an **Animation** card in the Actor
    Inspector, shown for the selected row.
  - **Layer header:** eye toggle, live dot, name, default-animation dropdown, ghost trash.
- **A107-D3 — Actor Profiles gets the standard asset bar.** The Clip Set and Rig fields stacked above the Profiles
  list move into a 36px asset bar across the tab, like every other tab (R06).
- **A107-D4 — Window files granted:**
  - `ClipEditorWindow.uxml` (the Clip Editor pane headers, transport and timeline-header markup only);
  - `ClipEditorWindow.cs` and `ClipEditorWindow.ComponentStack.cs` (Clip Editor header/button construction only);
  - `ClipEditorTransport.cs`.

## 1.1 Audit — findings to fix (A104 fixes G1–G8 first, including magenta and the clipped clip-list rows)

**Clip Editor** (`07_ClipEditor.png`)
- CE1 — The Clips list shows two rows and a scrollbar in a 70px slot while the hierarchy below gets 400px (R10). The
  clip list gets a default share of at least 8 rows via its split's initial dimension, and the split is remembered.
- CE2 — The "New / Delete", "Edit / Prefab" text buttons in pane headers become ghost icon buttons with tooltips.
  Delete is disabled without a selection (R12, R17, R21).
- CE3 — "Select a clip to pose the rig." sits above the viewport as a loose line (R14). The viewport shows a centred
  empty-state overlay when no clip is selected; otherwise the status goes to the footer.
- CE4 — The viewport tool column holds seven icons of mixed colour and style (blue/teal figures) (R20, R21). Use the
  standard rail: gizmo modes as one segmented group, then camera, then toggles, one icon colour, the on-state filled.
- CE5 — The inspector's "Select a clip to edit its properties." plus an Attachment Points card with a full-width
  "Select Source" button (R12, R13). Use an empty state for no clip; Attachment Points becomes a `MakeCard` with a
  ghost action in its header.
- CE6 — Transport row groups (Length/FPS/frames · buttons · Frame/Time/Speed · Zoom · All/Selected) have uneven gaps.
  "All / Selected" are two buttons (R07, R11). Use one 32px row, 4px gaps, 1px dividers between groups, and All ·
  Selected as a segmented control.
- CE7 — Timeline header: Snap and Auto Key look like plain buttons whether on or off (R17). They become toggles with
  a filled on-state; "Add Event" is a ghost icon + word; "Scale From Start" is a compact dropdown.
- CE8 — With no clip, the ruler runs from −30 and the key area and track headers are blank (R13). The ruler starts at
  0, and the track header column shows "Select a clip to see its tracks".

**Actor Profiles** (`10_ActorEditor.png`)
- AP1 — The four column headers sit at four heights; Clip Set/Rig fields are stacked above "Profiles" (R06; D3).
- AP2 — Every animation row has play, stop and a number field; names truncate "MeleeCon…", "Resurrecti…" (R01; D2).
  Names get the room the controls took, with a tooltip.
- AP3 — The "(none)" default-animation buttons and trash icons per layer become a dropdown and a ghost trash in the
  layer card header (R12).
- AP4 — "0 err 4 warn" is an orange chip over the preview; make it two badges (R16).
- AP5 — Actor Inspector: "NewRig (Rig A" is truncated in a half-width field; "+ Clip Set" is a full-width grey button
  (R01, R12). Use property rows with full-width fields and a secondary "Add clip set".
- AP6 — "South East ▾   SouthEast → SouthEast" repeats the direction (R03). Keep the dropdown; the resolved arrow text
  only appears when it differs from the pick.
- AP7 — The Layer Events strip's label column doesn't align with the layer names above (R05).

**Cutscenes** (`12_CutsceneEditor.png`)
- CT1 — "No cutscene loaded. Pick one above, or create a new one." is drawn straight onto the darkened scene render,
  at low contrast (R13, R18). Use a centred empty-state card on a solid surface, with New as its action.
- CT2 — Cast: "+ Actor", "+ Prop", "Sync" are filled buttons with a wrapped hint below (R12, R13). Use ghost icon
  buttons in the Cast pane header and an empty state in the list.
- CT3 — The disabled rail icons are nearly invisible on the scene (R17, R20). Use the standard rail on its
  translucent card, with 40% disabled icons.
- CT4 — "● Key" (red dot) sits beside "Auto Key" and "Skip Holds", which don't show whether they're on (R17). Record
  keeps its red dot; the toggles get a filled on-state.
- CT5 — The inspector's "Assign a Cutscene asset above." sits top-left (R13): use an empty state.

## 2. Files (all under `Packages/com.dotsanimationtoolkit/Editor/ClipEditor/`)

- **Clip Editor:** `Panes/ClipListPane.cs`, `Panes/RigHierarchyPane.cs`, `Panes/ClipInspectorPane.cs`,
  `Panes/TimelinePane.cs`, `Panes/TimelinePane.View.cs`, `TimeRulerElement.cs`, `ClipEditorTransport.cs`,
  `ClipEditorWindow.uxml`, `ClipEditorWindow.cs`, `ClipEditorWindow.CameraNavigation.cs` (rail) — D4.
- **Actor Profiles:** `ActorEditor/ActorEditorPanel.cs`, `ActorEditor/ActorEditorLayersColumn.cs`,
  `ActorEditor/ActorEditorProfilesColumn.cs`, `ActorEditor/ActorEditorInspectorColumn.cs` (on the `Conformance_I`
  allowlist — converting lets it leave), `ActorEditor/LayerEventStripElement.cs` (allowlisted).
- **Cutscenes:** `Cutscene/CutsceneEditorPanel.cs` (5,510 lines: workers get grepped line ranges only),
  `Cutscene/CutsceneCastPanel.cs`, `Cutscene/CutsceneViewportElement.cs`.

## 3. Read

- T0 greps §1.1's strings.
- Always read: the style guide §2–§4, A104's For-integration block, `Shared/ToolkitChrome.cs`,
  `Shared/ViewportFrameElement.cs`.
- `ClipEditorLayoutTests` (element names) before any UXML change.

## 4. Fixtures

- **F1 `ActorEditorLayerRowTests.SelectingAnAnimationRow_ShowsItsSpeed_InTheAnimationCard`** (D2).
  - **Setup:** a detached `ActorEditorPanel` with a `CreateInstance` profile.
  - **Asserts:** after selecting an animation row, the inspector's Animation card field reads that animation's speed.
  - **Revert-to-fail:** leave the card unbound.
- **F2 `TimeRulerRestTests.WithNoClip_FirstLabelIsZero`** (CE8), only if the ruler's range is computed outside
  drawing code.
  - **Revert-to-fail:** restore the −30 pre-roll.
  - If the range is only drawing, delete this fixture (roadmap rule) and prove it by capture.
- Every gate runs `ClipEditorLayoutTests` (update names only if D4 markup renames any — prefer not to),
  `EditorStyleConformanceTests`, `PackagingConformanceTests`, `ActorPreviewComposerTests`.

## 5. Tasks — lead

- [ ] **T0 — Ground.** Claim `a107`; grep §1.1; read A104's For-integration; log drift.
- [ ] **W1 — `[parallel-safe]`, ≤2 files each:**
  - T1 — CE1, CE2 (clips): `ClipListPane.cs`, `ClipEditorWindow.uxml`.
  - T2 — CE2 (hierarchy): `RigHierarchyPane.cs`.
  - T3 — CE5: `ClipInspectorPane.cs`.
  - T4 — CE6: `ClipEditorTransport.cs`.
  - T5 — CE7, CE8 (headers): `TimelinePane.cs`.
  - T6 — CE8 (ruler) + F2: `TimeRulerElement.cs`.
  - T7 — AP1/D3: `ActorEditorPanel.cs`, `ActorEditorProfilesColumn.cs`.
  - T8 — AP2, AP3/D2: `ActorEditorLayersColumn.cs`.
  - T9 — AP5, D2 Animation card: `ActorEditorInspectorColumn.cs`.
  - T10 — CT2: `CutsceneCastPanel.cs`.
  - T11 — CT1, CT3: `CutsceneViewportElement.cs`.
- **Gate W1:** F2 (if kept) + the conformance set.
- [ ] **W2:**
  - T12 — CE3, CE4: `ClipEditorWindow.cs`, `ClipEditorWindow.CameraNavigation.cs`.
  - T13 — AP4, AP6, AP7: `LayerEventStripElement.cs` + the preview header in `ActorEditorPanel.cs`, after T7.
  - T14 — CT4, CT5: `CutsceneEditorPanel.cs` (grepped ranges).
  - T15 — F1 fixture.
- **Gate W2:** F1 + F2 + the conformance set + `ActorPreviewComposerTests`. The revert-to-fail commit fails exactly
  F1 (and F2).
- [ ] **T16 — Close text:**
  - CHANGELOG `## [0.59.0] — Timeline tabs pass`;
  - allowlist shrink;
  - traps;
  - HANDOFF paragraph.

  Then `status a107 ready`.

## 6. Tasks — stage orchestrator

- [ ] **S1 — Merge** `a107`; compile; suites.
- [ ] **S2 — After captures of all 15 tabs** → `Library/UIAudit/after-a107/`: the final full audit.
  - Mark every CE/AP/CT id in §7.
  - Run one last R01–R22 pass over **every** tab, including A105/A106's, and list any regression. **Judge hard.**
- [ ] **S3 — Drives:**
  - **Clip Editor:** the clip list shows ≥8 rows; Snap on/off is visible; the empty timeline starts at 0.
  - **Actor Profiles:** hover play works, and the Animation card edits speed through undo on
    `ActorEditorScratch.profile`, never `MaleCitizen.profile`.
  - **Cutscenes:** the empty-state New creates a scratch cutscene under `Assets/A107Scratch/` (deleted after).
- [ ] **S4 — Close:**
  - CHANGELOG `0.59.0`, pin, HANDOFF, roadmap tick;
  - the style guide gains a "Shipped" line naming A104–A107;
  - `Code_Audit_2026-09.md` gets a note that the UI pass is done.
- [ ] **S5 — ⏸ owner checkpoint** (real stop while present):
  - **Show:** the full before/after set of 15 tabs.
  - **Q1.** Is this the product you'd put in a store listing? If not, which three screens are furthest from it?

## 7. Build log

*(T0 grounding, gates, revert-to-fail, drift, For integration, S2 full audit table, drives, close.)*
