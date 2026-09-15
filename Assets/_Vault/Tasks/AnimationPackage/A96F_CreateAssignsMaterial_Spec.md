# Amendment A96F — Materials tab: Create also assigns the material to the part

> **Status:** 📝 specced 2026-09-14 from the owner's A96 T13 answer, not built. Takes `0.49.0`.
> **Roadmap:** [`AnimationPackage_Roadmap.md`](AnimationPackage_Roadmap.md), Phase 2 follow-up to A96.
> **Predecessors:** A96 (`0.46.0`).
> **Executor:** one lead; `worker` subagents in **one wave of three**, each ≤ 2 files; the stage does the drive and the close
> (no window wiring changes).

---

## 0. Session prompt

You are running **A96F — Create also assigns** on the DOTS Animation Toolkit (head `0.48.0` or later). Spec:
`Assets/_Vault/Tasks/AnimationPackage/A96F_CreateAssignsMaterial_Spec.md`. Read it, the roadmap §3 protocol, then only what §3
names. §2 is settled. T0 and T1 are the lead's; one wave (T2–T4); one gate; T5–T7 belong to the stage orchestrator.

---

## 1. Goal

The owner's A96 T13 answer (2026-09-14): "assign yes". Today Create writes `M_<Rig>_<Target>.mat` beside the rig's prefab and
tells the author to assign it in the Inspector. After this amendment Create also puts the new material on the renderer at the
target's `sourceNodePath` in `rig.sourcePrefab`, and the result line says what it replaced.

---

## 2. Decisions (owner answer + orchestrator calls, 2026-09-14 — do not re-ask). ⚠ = interpretation for the checkpoint.

- **M-D1 — Create always assigns.** No toggle and no dialog: the owner asked for it, and the replaced material stays on disk, so
  the edit is reversible by assigning the old one back. The button reads "Create and assign".
- **M-D2 — The write is a prefab asset edit** through `PrefabUtility.LoadPrefabContents` / `SaveAsPrefabAsset` /
  `UnloadPrefabContents`, the pattern `RigStructureEditor` uses (an immediate, non-undoable asset write). Never through a scene
  instance, never `AssetDatabase.SaveAssets()`.
- **M-D3 — Which renderer and slot.** The node is `PrefabAuthoringBridge.ResolveByPath(contentsRoot, target.sourceNodePath)`;
  its `Renderer` (on the node itself, not children). One material slot: replace it. Several slots: ⚠ replace the slot holding
  the material the tab has selected when that material is on this renderer, else slot 0; the result line names the slot.
- **M-D4 — Failures never lose the material.** Missing node, no renderer, or a prefab that is not a saved asset: the material is
  still created, assignment is skipped, and the result line says why ("Created …; not assigned: node 'X' has no Renderer").
- **M-D5 — Result line:** "Created M_NewRig_BaseHead.mat and assigned it to BaseHead (replaced BaseHead.mat)."
- **M-D6 — After the write** the tab refreshes (`Refresh()`), so the list shows the new material serving the part and the old
  one no longer listing it.

---

## 3. Read first

- `Editor/ClipUtilities/MaterialTemplateUtility.cs` lines 60–115 (`TryCreateForTarget`).
- `Editor/Materials/MaterialsPanel.cs` lines 140–170 (`CreateForTarget`, the result label) and grep `materials-create-button`.
- `Editor/ClipEditor/Authoring/RigStructureEditor.cs` lines 70–100 (the `LoadPrefabContents` write pattern).
- `Editor/ClipEditor/Authoring/PrefabAuthoringBridge.cs` lines 190–235 (`ResolveByPath`).
- `Documentation~/materials-tab.md` (the Create section).

---

## 4. Design

`MaterialTemplateUtility` (Editor/ClipUtilities) gains:

```csharp
public static bool TryAssignToTargetRenderer(RigAsset rig, RigTargetDefinition target, Material material,
    Material preferredSlotMaterial, out string assignedDescription, out string failureMessage);
```

`assignedDescription` is the M-D5 tail ("BaseHead (replaced BaseHead.mat)", or "BaseHead slot 2 (replaced Trim.mat)").
`MaterialsPanel.CreateForTarget` calls it after a successful create, passing `SelectedMaterial`, and writes M-D4/M-D5.
`LastAssignedDescription` is exposed for drives.

---

## 5. Tasks

- [ ] **T0 — Grounding (lead).** Verify §3's names and ranges; confirm `ResolveByPath` works on `LoadPrefabContents` roots
  (paths are relative to the prefab root in both); log drift in §7.
- [ ] **T1 — Stub (lead).** The §4 signature returning false, committed before the wave.
- [ ] **T2 — Assign utility + fixture [parallel-safe]** — Files: `Editor/ClipUtilities/MaterialTemplateUtility.cs`, new
  `Tests/EditMode/MaterialTemplateAssignTests.cs`. `AssignToSingleSlotRenderer_ReplacesMaterialInPrefabAsset`: a GUID-named folder
  under Assets (read its path back), a two-node prefab saved with `SaveAsPrefabAsset`, a `CreateInstance` rig whose target points
  at the child node; after the call, reload the prefab and assert the child's `sharedMaterial` is the new material; delete the
  folder in TearDown. Revert-to-fail: skip `SaveAsPrefabAsset`.
- [ ] **T3 — Panel [parallel-safe]** — Files: `Editor/Materials/MaterialsPanel.cs`. Button text, M-D4/M-D5 result line,
  `Refresh()`, `LastAssignedDescription`.
- [ ] **T4 — Docs [parallel-safe]** — Files: `Documentation~/materials-tab.md`. Create now assigns; what it replaces; how to undo
  (assign the old material back).
- **Gate the wave.** `MaterialTemplateAssignTests`, `MaterialContractValidationTests`, `PackagingConformanceTests`.
- [ ] **T5 — Drive (stage).** Full suites. Scratch copies of `NewRig.asset` and `MaleCitizen.prefab` in `Assets/A96FScratch/`
  (rig copy's `sourcePrefab` → prefab copy). Detached `MaterialsPanel`, `SetRig`, `CreateForTarget(BaseHead)`: the prefab copy's
  `BaseHead` renderer holds `M_A96FScratchRig_BaseHead` after a reload; the original prefab and `BaseHead.mat` untouched; scratch
  deleted; registry sha256s unchanged.
- [ ] **T6 — Vault + HANDOFF + close (stage).** CHANGELOG, `package.json` and the conformance pin `0.49.0`.
- [ ] **T7 — ⏸ owner checkpoint.** "Materials: pick a Quad part and press Create and assign. The part's renderer now uses the new
  material and the line says what it replaced. ⚠ For a renderer with several slots it replaces the selected material's slot, else
  slot 0: right?"

---

## 6. Out of scope

- Assigning to scene instances or prefab variants other than `rig.sourcePrefab` itself.
- Undo for the prefab write (it is an asset write, like every rig structure edit).

## 7. Build log

_(empty)_
