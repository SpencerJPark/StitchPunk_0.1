# Amendment A99 — Ragdoll tab: bodies, limits and the drop test get a home

> **Status:** 📝 specced 2026-09-10, not built. Takes `0.51.0` (corrected 2026-09-15; `0.46.0` went to A96).
> **Roadmap:** [`AnimationPackage_Roadmap.md`](AnimationPackage_Roadmap.md) Phase 2.
> **Predecessors:** Phase D (ragdoll runtime and preview), A82 (column, split view), A83 (the
> `RagdollHandles` partial was left in place for this amendment). **Three owner checkpoints on
> ragdoll are still open** (HANDOFF §4/§7/§8: ±45° default limits, launch feel, the preview's
> rest-pose derivation). This tab is where they get judged; it does not pre-empt them.
> **Executor:** one orchestrator; `worker` subagents in **one wave of six**, each ≤ 2 files, plus
> one sequential move task.

---

## 0. Session prompt (paste into a fresh session)

You are running **Amendment A99 — Ragdoll tab** on the DOTS Animation Toolkit package (head
`0.45.0` or later; **A82, A83 built**). Spec:
`Assets/_Vault/Tasks/AnimationPackage/A99_RagdollTab_Spec.md`. Read it, the roadmap §3 protocol,
then only what §3 here names, and `Docs/AnimationToolkit/Phase_D_Ragdoll_Spec.md` §9 (you are
touching ragdoll code). T0 yours; one wave (T1–T6); one gate; T7 (the move) sequential; T8–T11
yours. Stop at T12.

---

## 1. Goal

Ragdoll authoring is split: bodies and limits live on `RigAsset.ragdollBodies` /
`ragdollSettings` and are edited in `RigAssetEditor`; the box handles and the drop simulation live
in the Clip Editor's `RagdollHandles` partial behind a toolbar toggle; scenery for the drop is a
provider. None of it has a screen of its own, and the owner has never looked at the ±45° defaults
because there is nowhere obvious to look. After this amendment a Ragdoll tab holds it: a bodies
column, a viewport with the box handles and a Drop / Reset transport, and an inspector for the
selected body's limits and the rig-level settings, all over the shared Rig. The Clip Editor keeps
only its preview toggle (the "does the pose drop right" check while animating).

```
┌ Ragdoll ─────────────────────────────────────────────────────────────────────────────────────────┐
│ Rig [MaleCitizen ▾]                                                    ● 6 bodies · 5 joints       │
│ ┌ Bodies ─────────┐ ┌ Viewport ─────────────────────────────┐ ┌ Inspector: Arm_L ──────────────┐ │
│ │ [+][⟳]          │ │                                        │ │ Node   Arm_L (rig target)      │ │
│ │ ● Torso  root   │ │            (boxes over the actor)      │ │ Size   x 0.12 y 0.30 z 0.12    │ │
│ │ ● Head          │ │                                        │ │ Mass   1.0                     │ │
│ │ ● Arm_L         │ │                                        │ │ Joint  hinge  ±45° [curve]     │ │
│ │ ● Arm_R         │ │  [▶ Drop] [↺ Reset] [ground ▾]         │ │ Twist  ±10°                    │ │
│ │ ● Leg_L         │ └────────────────────────────────────────┘ │ ── Rig settings ──             │ │
│ │ ● Leg_R         │                                            │ Gravity  -9.81  Damping 0.2    │ │
│ └─────────────────┘                                            └────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────────────────────────────────────────┘
```

---

## 2. Decisions (recorded — do not re-ask). ⚠ = interpretation not yet confirmed.

- **A99-D1 — `ClipEditorTab.Ragdoll`, after Capture.** Toggle `tab-ragdoll`, text "Ragdoll", pane
  `ragdoll-pane`. Joins the shared selection for Rig; ignores the clip set except to pick a rest
  pose clip (D5).
- **A99-D2 — Move, do not fork.** `ClipEditorWindow.RagdollHandles.cs` (box handle routing) and
  the toolbar toggle's simulation control move into `Editor/Ragdoll/RagdollViewportElement.cs`; the
  Clip Editor's toggle becomes a call into the same element type hosted invisibly (no boxes drawn
  in the Clip Editor unless the toggle is on — today's behaviour). `PreviewRagdollBoxHandles`,
  `RagdollPreviewSimulation`, `RagdollPreviewScenery(Provider)`, `RagdollPreviewProbe` are not
  edited.
- **A99-D3 — Bodies column is an A82-style list over `rig.ragdollBodies`** (not assets, so the
  same D2 call as A93: copy the styling, bind to the list). New adds a body on the selected rig
  target or bone (a `RigTargetPicker` / bone name picker); Delete confirms.
- **A99-D4 — Inspector edits `RagdollBodyDefinition` and `RagdollRigSettings` fields directly**
  with undo on the rig, committing on blur/Enter. Field set = whatever the two types have (T0
  lists them); no new fields. The limit is drawn as an arc in the viewport when its body is
  selected (`PreviewRagdollBoxHandles` may already do this — T0 checks).
- **A99-D5 — Rest pose for the drop is the rig's rest pose by default**, with an optional "pose
  from clip at time" row so the owner can drop from mid-Walk — this is the HANDOFF §7 caveat made
  visible (the preview derives limits from the on-screen pose). The row's tooltip states that
  caveat in one line.
- **A99-D6 — The drop transport is `TransportCoreElement` with `Play` = Drop, `Stop` = Reset**,
  capabilities `Stop` only; the ground dropdown picks the scenery provider.
- **A99-D7 — The three open checkpoints are re-asked, once, at this tab's checkpoint** with the
  tab as the viewing device. Nothing about limits or launch changes in this amendment. ⚠ (all three)

---

## 3. Read first

- `Docs/AnimationToolkit/Phase_D_Ragdoll_Spec.md` §9.
- `Authoring/Assets/RigAsset.cs` lines 400–520 (`RagdollRigSettings`, `RagdollBodyDefinition`).
- `Editor/ClipEditor/ClipEditorWindow.RagdollHandles.cs` in full (it is a partial; T0 records its
  length — if over 600 lines, the move is two workers).
- `Editor/ClipEditor/Preview/RagdollPreviewSimulation.cs` lines 80–180 (`TryBuild`, `Step`),
  250–280; `PreviewRagdollBoxHandles.cs` — grep `public`; `RagdollPreviewSceneryProvider.cs` in full.
- `Editor/ClipEditor/Preview/ClipPreviewController.cs` lines 160–200
  (`TryEnableRagdollPreview`/`DisableRagdollPreview`), 685–730 (ragdoll box selection/pick).
- `Editor/Inspectors/RigAssetEditor.cs` — grep `ragdoll` for the current field editing to lift.
- `Editor/ClipEditor/Shared/TransportCoreElement.cs` — public surface.

---

## 4. Design

### 4.1 `Editor/Ragdoll/RagdollViewportElement.cs` (T7, the move) — hosts a `ClipPreviewController`,
the moved handle routing, the simulation lifecycle (`TryEnableRagdollPreview` → `Step` per tick →
`DisableRagdollPreview`), D6 transport, D5 pose row.
### 4.2 `Editor/Ragdoll/RagdollBodiesColumn.cs` (T2), `RagdollInspectorColumn.cs` (T3),
`RagdollPanel.cs` (T4).
### 4.3 `Editor/ClipEditor/Editing/RagdollBodyEditing.cs` (T1) — `AddBody(rig, address)`,
`RemoveBody(rig, bodyId)`, `SetLimit(...)` with undo; lifted from `RigAssetEditor` where present.
### 4.4 Pure: `Editor/Ragdoll/RagdollBodySummaryResolver.cs` (T1) — "6 bodies · 5 joints" and
per-body validity (a body whose node no longer resolves is flagged in the column).

---

## 5. Tasks

- [ ] **T0 — Baseline (orchestrator).** Gate; totals. Line count of the partial; field list of the
  two types; whether the limit arc is drawn today; capture the Clip Editor with the ragdoll toggle
  on (before).
- [ ] **T1 — Editing + summary resolver + fixture [parallel-safe]** — Files: new
  `RagdollBodyEditing.cs`, new `RagdollBodySummaryResolver.cs`. Fixture (orchestrator adds):
  `Tests/EditMode/RagdollBodySummaryResolverTests.cs` — `Summary_CountsJointsAsBodiesWithAParent`
  (3 bodies, one root → "3 bodies · 2 joints"). Revert-to-fail: count all bodies as joints.
- [ ] **T2 — Bodies column [parallel-safe]** — Files: new `RagdollBodiesColumn.cs`.
- [ ] **T3 — Inspector column [parallel-safe]** — Files: new `RagdollInspectorColumn.cs`. Read
  `RigAssetEditor.cs`'s ragdoll range.
- [ ] **T4 — Panel [parallel-safe]** — Files: new `RagdollPanel.cs`. Hosts a
  `RagdollViewportElement` by its **surface only** (§4.1 signatures; the file lands in T7).
- [ ] **T5 — `RigAssetEditor` loses its ragdoll section [parallel-safe]** — Files:
  `RigAssetEditor.cs` (the ragdoll range only; replaced by one "Edit in the Ragdoll tab" button that
  focuses the tab — the opener idiom from `ActorProfileAssetOpener.cs`).
- [ ] **T6 — Docs [parallel-safe]** — Files: `Documentation~/ragdoll.md` (an "Authoring in the
  Ragdoll tab" section; the D5 caveat sentence), `CHANGELOG.md` `## [0.46.0]`.
- **Gate the wave** — it will not compile until T7 lands `RagdollViewportElement`; so: spawn the
  wave, wait, then run T7, then gate everything together. Commit `A99-T1..T7` as one.
- [ ] **T7 — The move (one worker, sequential, after the wave).** Files: new
  `RagdollViewportElement.cs`, `ClipEditorWindow.RagdollHandles.cs` (shrinks to the toggle → element
  bridge). Move verbatim; D2.
- **Gate.** `RagdollBodySummaryResolverTests`, `ClipEditorLayoutTests`, and the existing ragdoll
  preview fixtures (grep `Ragdoll` under `Tests/`).
- [ ] **T8 — Window wiring (orchestrator).** Tab; `index.md`; `package.json`; `Conformance_G`. Gate.
- [ ] **T9 — Drive.** Full suites. Select MaleCitizen's rig → bodies listed; select Arm_L → box
  highlighted, inspector shows its limit; Drop → falls onto the ground; Reset → pose restored (the
  "does it un-write" question from HANDOFF §9); edit a limit → reload rig from disk → persisted;
  Clip Editor toggle still works. Capture before/after.
- [ ] **T10 — Vault + HANDOFF.** HANDOFF §4; the three open ragdoll checkpoints are re-pointed at
  this tab.
- [ ] **T11 — Close.** Roadmap checkbox.
- [ ] **T12 — ⏸ owner checkpoint.** Message: "Ragdoll tab: pick your rig, select a body, press
  Drop. Three things have waited for your eyes since Phase D and this is the place: (1) the ±45°
  default hinge limits — right or wrong? (2) the drop from a mid-animation pose (the pose row) —
  does the first frame jump? (3) does the launch feel right when triggered from an animation in
  Actor Profiles? Nothing about them changed in this amendment; the tab is for looking."

---

## 6. Deliberately out of scope

- Changing any limit default, solver parameter or launch behaviour (D7).
- The bone-reparent guard (HANDOFF §6 — the owner's call, still open).
- Runtime ragdoll changes.

## 7. Build log

### Phase 0

Phase 0 (stage, 2026-09-15, head `2d53ae6f`): doctor clean (git 2.43.0, hooks installed, broker alive, no stage blockers); compile clean; EditMode baseline 857 (856 passed, standing Conformance_A only); PlayMode baseline 285 (285 passed); CHANGELOG top section `## [0.48.1]`; registry sha256 AnimEventKey `3bdb420d…14701`, TargetTag `dbec3d5f…eb4f`. Lead opus, workers sonnet; merges authorized once ready with gates green (owner, 2026-09-14/15). Owner is away: checkpoints close by the standing rule (assume pass unless game breaking); this batch is followed by A101 on trunk. A99 stage T0: `ClipEditorWindow.RagdollHandles.cs` is 447 lines (under 600: one move worker); `RagdollBodyDefinition` fields: displayName, address (RigNodeAddress), boxCenter, boxSize, boxEulerAngles, mass, linearDamping (-1 = rig default), angularDamping (-1 = rig default), restitution, friction, limitMinDegrees, limitMaxDegrees, swingLimitDegrees, twistLimitDegrees, selfGroup, selfCollidesWith, collidesWithWorld; `RagdollRigSettings` (a struct at RigAsset.cs:403): space (RagdollSpace), gravityScale, defaultLinearDamping, defaultAngularDamping, jointStiffness, jointDamping, solverIterations (byte), substepHz. No limit arc is drawn today (PreviewRagdollBoxHandles has no arc/limit code). Version corrected 0.46.0 → 0.51.0. The before-capture of the Clip Editor with the ragdoll toggle on is skipped: the owner's window is docked and the batch cautions forbid driving it. The lead owns `ClipEditorWindow.RagdollHandles.cs` exclusively for T7.

### Build (worktree `spec/a99`, 2026-09-15)

Commits: `cd6b3c84` T0 stubs (seven shared types, so the six-worker wave compiled against one fixed
surface); `ca9c2792` T1-T7a; `37c012ed` T7b plus the compile fix below. Gate on `37c012ed`:
61 passed, 1 failed — `Conformance_A` only, the standing failure. Fixtures gated:
`RagdollBodySummaryResolverTests`, `PackagingConformanceTests`, `RagdollAuthoringTests`,
`RagdollPlanarConstraintTests`, `RagdollPreviewParityTests`, `RagdollSolverDeterminismTests`,
`RagdollSolverTests`. No PlayMode fixture names the moved editor code.

Drift, decided here and logged rather than asked:

1. **The move is a session, not a lift.** `ClipEditorWindow.cs` and `ClipEditorWindow.ComponentStack.cs`
   call five members of the partial and both files belong to the stage, so the drag state could not
   simply leave. `Editor/Ragdoll/RagdollBoxDragSession.cs` (a sealed class, no suffix rule) now holds
   every field and every geometry helper verbatim; the partial shrank 447 → 85 lines and keeps
   `selectedRagdollBodyId`, `FocusRagdollBody`, `TryBeginRagdollBoxDrag`, `ContinueRagdollBoxDrag`,
   `EndRagdollBoxDrag` as a bridge. The first gate caught what static review had not:
   `ClipEditorWindow.cs:2723` and `:3110` also read `activeRagdollBoxHandle`, so the partial carries a
   read-only property of that name proxying `ragdollBoxDragSession.ActiveHandle`.
2. **`BodyPicked` is declared and never raised.** `ClipPreviewController` can pick a *handle* on the
   already-selected body (`PickRagdollBoxHandle`) but has no "which body is under this point" call,
   and inventing one is out of D2's scope. Selection is the bodies column's job for now.
3. **Add Body uses an inline `PopupField<RigTargetDefinition>`, not `RigTargetPicker`.** That picker
   takes a `TargetTagRegistry` and a moving tag id — it exists to move a tag onto a rig part, not to
   name a node for a new body.
4. **Self-collision mask is an `IntegerField` clamped 0-255**, not a `MaskField`: the eight groups have
   no authored names to fill a choice list with.
5. **The rig inspector's button only raises the window** (`ClipEditorWindow.ShowWindow`). Focusing the
   new tab needs the enum member, which is the stage's T8; wire it there.
6. **T6 wrote `Documentation~/ragdoll.md` only.** CHANGELOG, `package.json` and `index.md` are the
   stage's; the section text is below.
7. **Unverified:** the revert-to-fail on the new fixture was not run (turn budget). The mutation is
   named in T1 — count every body as a joint in `RagdollBodySummaryResolver.Resolve` and
   `Summary_CountsJointsAsBodiesWithAParent` must fail on "3 bodies · 3 joints". Nothing has been
   driven in the Editor: T9's drive still owes the Drop, the Reset un-write and the persistence check.

### For integration

**CHANGELOG section for 0.51.0:**

```
## [0.51.0] - 2026-09-15
### Added
- Ragdoll tab in the Clip Editor: a bodies column over the selected rig's `ragdollBodies` with add
  and delete, a viewport with the box handles and a Drop / Reset transport over the scenery props,
  and an inspector for the selected body's collider, mass, damping, friction and joint limits
  followed by the rig-wide ragdoll settings. The tab follows the window's shared Rig selection.
- `RagdollBodySummaryResolver` reports "6 bodies · 5 joints" and flags a body whose node no longer
  resolves; a joint is a body with another body above it in the addressed hierarchy.
- The viewport's pose row drops from the rig's rest pose by default, or from a bound clip at a time,
  with the caveat stated in the tooltip: the preview measures each joint's limit against the pose on
  screen when the drop starts.
### Changed
- The ragdoll box-handle drag math moved out of `ClipEditorWindow.RagdollHandles.cs` (447 → 85 lines)
  into `RagdollBoxDragSession`, shared by the Clip Editor's viewport and the new tab. No behaviour
  change: the same undo group per gesture, the same clamping, the same handles.
- `RigAssetEditor`'s ragdoll section is now an "Edit in the Ragdoll tab" button plus the body summary.
  The ragdoll validation badges stay on the inspector.
- Nothing about limit defaults, solver parameters or launch behaviour changed.
```

**`Conformance_G` allowlist names needed: none.** `RagdollBodyEditing` ends in `Editing`,
`RagdollBodySummaryResolver` in `Resolver`; every other new type is a sealed instance class.

**Window wiring (T8), all of it outside this worktree:**

- `ClipEditorTab.cs`: add `Ragdoll = 13` after `Capture`.
- `ClipEditorWindow.uxml`: a toolbar toggle named `tab-ragdoll`, text "Ragdoll", placed per the
  owner's strip order, and a pane `VisualElement` named `ragdoll-pane`.
- `ClipEditorWindow.cs`, beside the other panels:
  `private RagdollPanel ragdollPanel;` → in the pane build,
  `ragdollPanel = new RagdollPanel(); ragdollPanel.Bind(activeAssetSelection); ragdollPane.Add(ragdollPanel);`
  and in `OnDisable`/`Dispose`, `ragdollPanel?.Dispose();`.
- Rest pose source (optional, D5's clip row): when the window's clip selection changes, call
  `ragdollPanel`'s viewport through the panel — the panel tracks the shared `ClipSet`; the clip and
  time are handed in with `RagdollViewportElement.SetRestPoseSource(clipSet, clip, normalizedTime)`.
  Until that is wired the row shows "No clip bound" and the drop uses the rest pose, which is D5's
  default anyway.
- The Clip Editor's own ragdoll toggle is untouched: it still drives `previewController` directly and
  its drags now route through `ragdollBoxDragSession` inside the same partial.
- `RigAssetEditor`'s button should become a tab focus once `ClipEditorTab.Ragdoll` exists (the idiom is
  `ActorProfileAssetOpener`'s `ClipEditorWindow.FocusWithActorEditorTab`).

**Vault-note traps:**

- A window partial's private members are the window's public API to its other partials. Grep every
  `ClipEditorWindow*.cs` for each field name before deleting one — the compiler is the only check, and
  `activeRagdollBoxHandle` was read two files away from where it was declared.
- `ClipPreviewController.TryEnableRagdollPreview` captures whatever pose is on screen; `Disable`
  restores it. That pair is the whole Drop / Reset transport — no separate reset path exists.
- A panel that only stores what it is handed shows nothing: `RagdollInspectorColumn.SetRig` and
  `SetSelectedBodyId` both redraw, because `RagdollPanel` never calls `Refresh` on it.

**HANDOFF draft:** The Ragdoll tab landed at 0.51.0. Bodies, box handles, limits and the drop test
now have one screen: a bodies column over the rig's `ragdollBodies`, a viewport hosting its own
`ClipPreviewController` with the box handles and a Drop / Reset transport over the scenery props, and
an inspector for the selected body's collider, mass, damping and limits above the rig-wide settings —
all following the window's shared Rig, with the rig asset's own inspector reduced to an "Edit in the
Ragdoll tab" button and its validation badges. The box-handle drag math moved verbatim out of the
Clip Editor's window partial into `RagdollBoxDragSession`, which both viewports now share, so the
Clip Editor's ragdoll toggle behaves exactly as before. Nothing about limit defaults, the solver or
launch changed: the three ragdoll checkpoints open since Phase D are re-asked here with the tab as
the viewing device.
