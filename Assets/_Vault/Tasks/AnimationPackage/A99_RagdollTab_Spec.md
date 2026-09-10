# Amendment A99 — Ragdoll tab: bodies, limits and the drop test get a home

> **Status:** 📝 specced 2026-09-10, not built. Takes `0.46.0`.
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

_(empty)_
