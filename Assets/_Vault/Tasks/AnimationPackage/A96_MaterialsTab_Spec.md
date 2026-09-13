# Amendment A96 — Materials tab: every actor material against the shader contract

> **Status:** 📝 specced 2026-09-10, not built. Takes `0.43.0`.
> **Roadmap:** [`AnimationPackage_Roadmap.md`](AnimationPackage_Roadmap.md) Phase 2.
> **Predecessors:** A78 (per-part `ValidateVatMaterial`), A82 (column, split view), the shader
> contract (`Documentation~/shader-contract.md`). Optional: A95 (sheet-bound tracks need an array sampler — cross-check).
> **Executor:** one orchestrator; `worker` subagents in **one wave of seven**, each ≤ 2 files.

---

## 0. Session prompt (paste into a fresh session)

You are running **Amendment A96 — Materials tab** on the DOTS Animation Toolkit package (head
`0.42.0` or later; **A82 built**). Spec:
`Assets/_Vault/Tasks/AnimationPackage/A96_MaterialsTab_Spec.md`. Read it, the roadmap §3 protocol,
then only what §3 here names. T0 and T1 yours; one wave (T2–T8); one gate; T9–T12 yours. Stop at
T13.

---

## 1. Goal

The shader contract lists six per-instance properties and which system writes each. A material
missing one silently renders wrong: a VAT part with no `_VatFrameA` is a motionless clump, a
flipbook quad with no `_ImageIndex` shows frame 0 forever, a billboard with no `_BillboardParams`
never turns. The only check today is `ValidateVatMaterial` at bake, VAT-only, in a baker. After this
amendment a Materials tab lists every material any rig's source prefab uses, per target kind, and
says which contract properties it has and lacks, whether DOTS instancing is on, and — for a part whose
sprite track is bound to a sheet — whether the material samples a `Texture2DArray`. A Create button makes a correct material from the
package's shipped example shader for a chosen rig target.

```
┌ Materials ──────────────────────────────────────────────────────────────────────────────────────┐
│ Rig [MaleCitizen ▾]                                                     [Create for target ▾]     │
│ ┌ Materials (5) ─────────────────┐ ┌ M_Citizen_Body ───────────────────────────────────────────┐ │
│ │ ● M_Citizen_Body    Quad ×3    │ │ Shader  Toolkit/CompositeExample                             │ │
│ │ ● M_Citizen_Hair    Vat        │ │ Used by  Torso, Arm_L, Arm_R                                 │ │
│ │ ● M_Citizen_Face    Flipbook   │ │ ✓ _ImageIndex  ✓ _AtlasFrame  ✓ _BillboardParams              │ │
│ │ ● M_Old             (unused)   │ │ – _VatFrameA (not needed for Quad)                           │ │
│ └────────────────────────────────┘ │ ✓ DOTS instancing                                             │ │
│                                    │ ● Sheet: CitizenFace binds Face, material has no _MainTexArray│ │
│                                    └──────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────────────────────────────────┘
```

---

## 2. Decisions (recorded — do not re-ask). ⚠ = interpretation not yet confirmed.

- **A96-D1 — `ClipEditorTab.Materials`, after Sprite Sheets.** Toggle `tab-materials`, text
  "Materials", pane `materials-pane`. **Joins the shared selection for Rig only** (reads
  `ActiveAssetSelection.Rig`; never writes the clip set).
- **A96-D2 — Scope is the rig's `sourcePrefab`**: every `Renderer.sharedMaterials` under it,
  mapped to rig targets by `sourceNodePath`, so each material knows which target kinds it serves.
  Materials in the project that no rig uses are not listed (no project-wide material scan).
- **A96-D3 — The contract is data, not prose.** `MaterialContractValidation` (Authoring/Validation)
  holds the property table: name, required-for kinds (`Quad`, `FlipbookQuad`, `VatMesh`, billboard
  root), and the check `material.HasProperty(name)`. Required-and-missing → Error; present-and-not-
  needed → Note; `enableInstancing == false` → Error for every kind (Entities Graphics needs it).
  The existing `ValidateVatMaterial` in `RigTargetBaker` calls this class for its texture-size
  check plus the property check — one source of truth, two callers.
- **A96-D4 — Sheet cross-check (A95 present; A95 D0/D7 rewrote this 2026-09-12):** if a
  `SpriteTrack.sheet` bound on any clip in the shared clip set targets this material's part and the
  material has no `_MainTexArray` texture property, Warning naming the sheet, the part and the
  material. There is no columns/rows layout to compare — A95 builds `Texture2DArray`s, not grids.
- **A96-D5 — Create makes a material from `Shaders/ToolkitCompositeExample.shader`** (T0 confirms
  the path and that it is the shipped example), enables instancing, sets keywords for the chosen
  target kind, saves beside the rig's prefab as `M_<Rig>_<Target>.mat`, and **does not** assign it
  to the renderer — that is a prefab edit the author confirms in the Inspector. ⚠ Auto-assign
  could be offered; the checkpoint asks.
- **A96-D6 — Read-only otherwise.** No property editing here; double-click a row opens the
  material in the Inspector.

---

## 3. Read first

- `Documentation~/shader-contract.md` §1 (the table), §2.2 (flipbook parameters), §5.
- `Authoring/Baking/RigTargetBaker.cs` — grep `ValidateVatMaterial` and read the method.
- `Authoring/Assets/RigAsset.cs` lines 200–245 (`RigTargetDefinition.sourceNodePath`, `kind`),
  and grep `enum TargetKind` in `Runtime/Components/AnimationToolkitEnums.cs`.
- `Shaders/` listing — the example shader's name.
- `Editor/VatBaking/VatBakeSourceResolver.cs` in full — resolving prefab nodes from
  `sourceNodePath` (reuse, do not duplicate).
- `Editor/ClipEditor/Authoring/RigsPanel.cs` lines 1–80 — a tab that reads the shared Rig.

---

## 4. Design

### 4.1 `Authoring/Validation/MaterialContractValidation.cs` (T2)

```csharp
public static class MaterialContractValidation
{
    public static IReadOnlyList<ContractProperty> Properties { get; }   // the table
    public static void Validate(Material material, TargetKind kind, bool isBillboardRoot,
        string partName, List<ValidationMessage> output);
}
public readonly struct ContractProperty { public readonly string name; public readonly TargetKindMask requiredFor; }
```

### 4.2 `Editor/Materials/RigMaterialResolver.cs` (T3) — `Resolve(RigAsset rig) →
List<RigMaterialUsage { Material material; List<RigTargetDefinition> targets; }>` via the prefab's
renderers and `VatBakeSourceResolver`'s node lookup.

### 4.3 `Editor/Materials/MaterialTemplateUtility.cs` → must live in `Editor/ClipUtilities/` (T4)
— `CreateForTarget(RigAsset rig, RigTargetDefinition target) → Material` per D5.

### 4.4 Columns + panel (T5, T6, T7) — `MaterialCatalogColumn.cs` (A82 column over
`Material`), `MaterialInspectorColumn.cs` (D3 rows, D4 line), `MaterialsPanel.cs`.

---

## 5. Tasks

- [ ] **T0 — Baseline (orchestrator).** Gate; totals. Name the example shader file; confirm
  `_MainTexArray` is the array sampler property in `ToolkitSpriteUnlitArray.shadergraph` (D4 keys
  on it). Record `TargetKind` values.
- [ ] **T1 — Nothing to pre-write** beyond confirming names; proceed.
- [ ] **T2 — Contract validation + fixture [parallel-safe]** — Files: new
  `MaterialContractValidation.cs`, new `Tests/EditMode/MaterialContractValidationTests.cs`
  (`VatMesh_MissingVatFrameA_IsAnError` using a material on a shader that lacks it — `Shader.Find
  ("Unlit/Color")`; `Quad_MissingVatFrameA_IsNotReported`). Revert-to-fail: mark every property
  required for every kind.
- [ ] **T3 — Rig material resolver [parallel-safe]** — Files: new `RigMaterialResolver.cs`. Read
  `VatBakeSourceResolver.cs`.
- [ ] **T4 — Template utility [parallel-safe]** — Files: new
  `Editor/ClipUtilities/MaterialTemplateUtility.cs`.
- [ ] **T5 — Catalog column [parallel-safe]** — Files: new `MaterialCatalogColumn.cs`.
- [ ] **T6 — Inspector column [parallel-safe]** — Files: new `MaterialInspectorColumn.cs`.
- [ ] **T7 — Panel [parallel-safe]** — Files: new `MaterialsPanel.cs`.
- [ ] **T8 — Docs [parallel-safe]** — Files: new `Documentation~/materials-tab.md`,
  `Documentation~/shader-contract.md` (§6 Troubleshooting gains "open the Materials tab").
- **Gate the wave.** `MaterialContractValidationTests`. Commit `A96-T2..T8`.
- [ ] **T9 — Orchestrator edits.** `RigTargetBaker.ValidateVatMaterial` calls the new class (keep
  the texture-size check where it is); tab wiring; `index.md`; `CHANGELOG.md` `## [0.43.0]`;
  `package.json`; `Conformance_G`. Gate; the VAT authoring fixtures (grep `ValidateVatMaterial`
  under `Tests/`).
- [ ] **T10 — Drive.** Full suites. Select MaleCitizen's rig → its materials list with ✓/✗ per
  property; swap one material's shader to Unlit/Color in a scratch copy → errors appear; Create for
  a Quad target → a `.mat` beside the prefab with instancing on (reload and check). Capture.
- [ ] **T11 — Vault + HANDOFF.**
- [ ] **T12 — Close.** Roadmap checkbox.
- [ ] **T13 — ⏸ owner checkpoint.** Message: "Materials tab with your rig selected: each material,
  which parts use it, which contract properties it has. ⚠ Should Create also assign the new material
  to the part's renderer, or leave that to you?"

---

## 6. Deliberately out of scope

- Editing material properties in the tab.
- Shader Graph generation or shader editing.
- Project-wide material scans (D2).

## 7. Build log

_(empty)_
