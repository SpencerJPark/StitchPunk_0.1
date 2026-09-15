# Amendment A96F — Materials tab: Create also assigns the material to the part

> **Status:** ✅ built 2026-09-15 as `0.49.0` (spec/a96f, merged fast-forward; integration `9271b512`). T7 closed 2026-09-15 under the owner's standing rule (unseen checkpoints pass unless game breaking); the multi-slot ⚠ stays recorded below for whenever the owner looks.
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

- [x] **T0 — Grounding (lead).** Verify §3's names and ranges; confirm `ResolveByPath` works on `LoadPrefabContents` roots
  (paths are relative to the prefab root in both); log drift in §7.
- [x] **T1 — Stub (lead).** The §4 signature returning false, committed before the wave.
- [x] **T2 — Assign utility + fixture [parallel-safe]** — Files: `Editor/ClipUtilities/MaterialTemplateUtility.cs`, new
  `Tests/EditMode/MaterialTemplateAssignTests.cs`. `AssignToSingleSlotRenderer_ReplacesMaterialInPrefabAsset`: a GUID-named folder
  under Assets (read its path back), a two-node prefab saved with `SaveAsPrefabAsset`, a `CreateInstance` rig whose target points
  at the child node; after the call, reload the prefab and assert the child's `sharedMaterial` is the new material; delete the
  folder in TearDown. Revert-to-fail: skip `SaveAsPrefabAsset`.
- [x] **T3 — Panel [parallel-safe]** — Files: `Editor/Materials/MaterialsPanel.cs`. Button text, M-D4/M-D5 result line,
  `Refresh()`, `LastAssignedDescription`.
- [x] **T4 — Docs [parallel-safe]** — Files: `Documentation~/materials-tab.md`. Create now assigns; what it replaces; how to undo
  (assign the old material back).
- **Gate the wave.** `MaterialTemplateAssignTests`, `MaterialContractValidationTests`, `PackagingConformanceTests`.
- [x] **T5 — Drive (stage).** Full suites. Scratch copies of `NewRig.asset` and `MaleCitizen.prefab` in `Assets/A96FScratch/`
  (rig copy's `sourcePrefab` → prefab copy). Detached `MaterialsPanel`, `SetRig`, `CreateForTarget(BaseHead)`: the prefab copy's
  `BaseHead` renderer holds `M_A96FScratchRig_BaseHead` after a reload; the original prefab and `BaseHead.mat` untouched; scratch
  deleted; registry sha256s unchanged.
- [x] **T6 — Vault + HANDOFF + close (stage).** CHANGELOG, `package.json` and the conformance pin `0.49.0`.
- [x] **T7 — ⏸ owner checkpoint.** "Materials: pick a Quad part and press Create and assign. The part's renderer now uses the new
  material and the line says what it replaced. ⚠ For a renderer with several slots it replaces the selected material's slot, else
  slot 0: right?"

---

## 6. Out of scope

- Assigning to scene instances or prefab variants other than `rig.sourcePrefab` itself.
- Undo for the prefab write (it is an asset write, like every rig structure edit).

## 7. Build log

### Phase 0

Phase 0 (stage, 2026-09-15, head `2d53ae6f`): doctor clean (git 2.43.0, hooks installed, broker alive, no stage blockers); compile clean; EditMode baseline 857 (856 passed, standing Conformance_A only); PlayMode baseline 285 (285 passed); CHANGELOG top section `## [0.48.1]`; registry sha256 AnimEventKey `3bdb420d…14701`, TargetTag `dbec3d5f…eb4f`. Lead opus, workers sonnet; merges authorized once ready with gates green (owner, 2026-09-14/15). Owner is away: checkpoints close by the standing rule (assume pass unless game breaking); this batch is followed by A101 on trunk.

### T0 grounding (lead, 2026-09-15, worktree `spec/a96f`)

Every §3 name resolves; the ranges drifted by a few lines only: `TryCreateForTarget` is
`MaterialTemplateUtility.cs` 63–113, `CreateForTarget` is `MaterialsPanel.cs` 145–166, the
`LoadPrefabContents` write is `RigStructureEditor.cs` 79–105, `ResolveByPath` is
`PrefabAuthoringBridge.cs` 197–228, and the Create section of `materials-tab.md` is 72–90.

`ResolveByPath` confirmed usable on a `LoadPrefabContents` root: it takes a plain `Transform`
root and walks `sourceNodePath` segment by segment against `GetChild` names, with no
`PrefabStage` or scene dependency, and `RigHierarchyPane.cs:351` already calls it with
`target.sourceNodePath` against an ordinary prefab root — so the path is root-relative in both,
and M-D3 needs no path rewriting. `RigStructureEditor` is also the proof that the
`LoadPrefabContents` / `SaveAsPrefabAsset` / `UnloadPrefabContents` triple (M-D2) is the
package's established non-undoable asset write, `UnloadPrefabContents` in a `finally`.

**Drift 1 — the fixture's scratch folder is in the package, not under `Assets`.** T2 says "a
GUID-named folder under Assets (read its path back)". The package's own convention
(`DiskRoundTripTests.cs` 44–73) instead creates the GUID-named folder under
`Packages/com.dotsanimationtoolkit/Tests/EditMode`, which is already a valid asset folder, so
nothing has to walk and create intermediate parents. Followed that: same guarantee, and the
fixture never names an `Assets/<Folder>` path, so Conformance_D cannot bite.

### Build (lead + one wave of three, 2026-09-15)

`65f3c96e` T1 committed the stub alone so the wave shared a settled signature. `8ffe4a3f` landed
T2–T4 together: `TryAssignToTargetRenderer` in `MaterialTemplateUtility`, the panel's button and
result line, `MaterialTemplateAssignTests`, and the `materials-tab.md` Create section.

The slot rule (M-D3) reads `renderer.sharedMaterials` once: an empty array becomes length 1, a
single-slot renderer takes index 0, and only a multi-slot renderer consults
`preferredSlotMaterial`, taking its first occurrence and otherwise index 0. `assignedDescription`
names the slot only in the multi-slot case, and a slot that held nothing reads "(replaced
nothing)" rather than a dangling name. Every failure path returns before the write with a
message that already begins "not assigned:", so the panel can concatenate it after the created
file name without re-wording it, and the created material is never lost (M-D4).

`CreateForTarget` captures `SelectedMaterial` into `previouslySelectedMaterial` **before** the
create, because `Refresh()` at the end of the method rebinds the selection — passing the live
property would have handed the assignment whatever the rebuilt list happened to select. The
method still returns true when only the assignment was skipped: its own `failureMessage` stays
empty, since a skipped assignment is not a create failure.

**Gate (`8ffe4a3f`):** `MaterialTemplateAssignTests` + `MaterialContractValidationTests` +
`PackagingConformanceTests` = 15 tests, 14 passed, the one standing
`Conformance_A_AsmdefReferenceLists_MatchSection13Exactly` failure. Compile and Burst clean.

**Revert-to-fail:** probe commit `909305a8` replaced the single
`PrefabUtility.SaveAsPrefabAsset(contents, prefabAssetPath)` line with a comment and gated;
`AssignToSingleSlotRenderer_ReplacesMaterialInPrefabAsset` failed with "Expected: <NewPart …> But
was: <OldPart …>", proving the fixture reads the prefab back off disk rather than the in-memory
contents. `git reset --hard HEAD~1` restored `MaterialTemplateUtility.cs` to sha256
`7306d0ebb7ed96dbd6b178a7bfb979a73dbeb5605894fe9583a9b92f30ee0c74`, unchanged from before the
probe.

Drift: one (the fixture's scratch folder, logged in the T0 grounding block above).

### For integration

**CHANGELOG — `## [0.49.0]`:**

> ### Changed
> - Materials tab: the header button is now **Create and assign**. After writing
>   `M_<Rig>_<Target>.mat` beside the rig's prefab it also puts the new material on the renderer
>   at the target's Source Node Path inside the rig's Source Prefab, through
>   `PrefabUtility.LoadPrefabContents` / `SaveAsPrefabAsset` — an immediate, non-undoable asset
>   write, the same one every rig structure edit makes. A single-slot renderer has its slot
>   replaced; a multi-slot renderer has the slot holding the catalog's selected material replaced
>   when that material is on this renderer, otherwise slot 0, and the result line names the slot:
>   "Created M_NewRig_BaseHead.mat and assigned it to BaseHead (replaced BaseHead.mat)."
> - A missing node, a node with no Renderer, or a Source Prefab that is not a saved asset skips
>   the assignment without losing the material — the line reads "Created M_NewRig_BaseHead.mat;
>   not assigned: node 'BaseHead' has no Renderer." The replaced material stays on disk, so the
>   edit is undone by assigning it back.
>
> ### Added
> - `MaterialTemplateUtility.TryAssignToTargetRenderer` and `MaterialsPanel.LastAssignedDescription`.

**`Conformance_G` allowlist:** nothing new. The method joined the existing
`MaterialTemplateUtility`, which already sits in `Editor/ClipUtilities` where the `Utility`
suffix is permitted. No new static class, no new file outside `Tests/EditMode`.

**Vault-note traps** (for `Assets/_Vault/Memories/Code/AnimationToolkit.md`):

- A panel method that ends in `Refresh()` must capture any selection it wants to pass downstream
  *before* the create, not read the property at the point of use — `Refresh()` rebinds
  `SelectedMaterial` from the rebuilt usage list and silently substitutes a different material.
- An EditMode fixture that must prove a prefab **file** changed has to reload through
  `AssetDatabase.LoadAssetAtPath` after the call; asserting against the `LoadPrefabContents` root
  or the pre-save instance passes even when `SaveAsPrefabAsset` is never reached — that is exactly
  the mutation this spec's revert-to-fail used.
- `PrefabAuthoringBridge.ResolveByPath` is root-relative and scene-free, so the same
  `sourceNodePath` resolves against a `LoadPrefabContents` root, a loaded prefab asset root and a
  scene instance with no rewriting.

**HANDOFF draft:** A96F (`0.49.0`) answers the owner's A96 T13 call: the Materials tab's Create
button became **Create and assign** and now finishes the job it used to hand back to the
Inspector. `MaterialTemplateUtility.TryAssignToTargetRenderer` opens the rig's Source Prefab with
`LoadPrefabContents`, resolves the target's Source Node Path, takes the Renderer on that node
only, replaces one material slot and saves the prefab asset — the package's established
non-undoable rig-structure write, never `AssetDatabase.SaveAssets()`. One slot is replaced
outright; several slots mean the slot holding the catalog's selected material when it is on this
renderer, else slot 0, with the slot named in the result line. Nothing is lost when the
assignment cannot happen: the material is still created and the line says why. Built by one lead
and a wave of three workers, gated at 15 tests with only the standing `Conformance_A` failure,
and the fixture proven by skipping `SaveAsPrefabAsset` and watching it fail. Unverified here: the
T5 drive (nothing real was written) and the multi-slot branch, which no fixture covers — it is
the ⚠ interpretation the T7 checkpoint asks about.

### Close (stage, 2026-09-15)

T5 drive (detached `MaterialsPanel`, scratch `Assets/A96FScratch/` with copies of `NewRig.asset` and `MaleCitizen.prefab`, the rig copy's `sourcePrefab` pointed at the prefab copy): `CreateForTarget(BaseHead)` returned true, `LastAssignedDescription` = "BaseHead (replaced BaseHead.mat)", the material landed at `Assets/A96FScratch/M_A96FScratchRig_BaseHead.mat`, the reloaded prefab copy's `Visual/MaleUnitVisual/Pelvis/Torso/Neck/BaseHead` renderer holds it and the prefab YAML carries its GUID; the real `MaleCitizen.prefab` still has `BaseHead` on that node. Scratch deleted; both registry sha256s unchanged; full suites EditMode 863 (862 passed, standing Conformance_A only) and PlayMode 285. T6: CHANGELOG `## [0.49.0]`, `package.json` and the conformance pin moved with the batch to `0.51.0`, HANDOFF §4, AnimationToolkit.md. T7 (⚠ multi-slot renderer: the selected material's slot, else slot 0) is closed as accepted per the standing rule; not seen by the owner.
