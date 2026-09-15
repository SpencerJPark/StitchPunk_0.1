# Amendment A96 — Materials tab: every actor material against the shader contract

> **Status:** ✅ built 2026-09-14 as `0.46.0` in the A96–A98 parallel worktree batch (merged `62603be5`, integrated `e7ae55f9`); **T13 answered 2026-09-14** (owner: "assign yes"); reworked as A96F (`0.49.0`). The specced `0.43.0` went to A93F; see §7.
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

- [x] **T0 — Baseline (orchestrator).** Gate; totals. Name the example shader file; confirm
  `_MainTexArray` is the array sampler property in `ToolkitSpriteUnlitArray.shadergraph` (D4 keys
  on it). Record `TargetKind` values.
- [x] **T1 — Nothing to pre-write** beyond confirming names; proceed.
- [x] **T2 — Contract validation + fixture [parallel-safe]** — Files: new
  `MaterialContractValidation.cs`, new `Tests/EditMode/MaterialContractValidationTests.cs`
  (`VatMesh_MissingVatFrameA_IsAnError` using a material on a shader that lacks it — `Shader.Find
  ("Unlit/Color")`; `Quad_MissingVatFrameA_IsNotReported`). Revert-to-fail: mark every property
  required for every kind.
- [x] **T3 — Rig material resolver [parallel-safe]** — Files: new `RigMaterialResolver.cs`. Read
  `VatBakeSourceResolver.cs`.
- [x] **T4 — Template utility [parallel-safe]** — Files: new
  `Editor/ClipUtilities/MaterialTemplateUtility.cs`.
- [x] **T5 — Catalog column [parallel-safe]** — Files: new `MaterialCatalogColumn.cs`.
- [x] **T6 — Inspector column [parallel-safe]** — Files: new `MaterialInspectorColumn.cs`.
- [x] **T7 — Panel [parallel-safe]** — Files: new `MaterialsPanel.cs`.
- [x] **T8 — Docs [parallel-safe]** — Files: new `Documentation~/materials-tab.md`,
  `Documentation~/shader-contract.md` (§6 Troubleshooting gains "open the Materials tab").
- **Gate the wave.** `MaterialContractValidationTests`. Commit `A96-T2..T8`.
- [x] **T9 — Orchestrator edits.** `RigTargetBaker.ValidateVatMaterial` calls the new class (keep
  the texture-size check where it is); tab wiring; `index.md`; `CHANGELOG.md` `## [0.43.0]`;
  `package.json`; `Conformance_G`. Gate; the VAT authoring fixtures (grep `ValidateVatMaterial`
  under `Tests/`).
- [x] **T10 — Drive.** Full suites. Select MaleCitizen's rig → its materials list with ✓/✗ per
  property; swap one material's shader to Unlit/Color in a scratch copy → errors appear; Create for
  a Quad target → a `.mat` beside the prefab with instancing on (reload and check). Capture.
- [x] **T11 — Vault + HANDOFF.**
- [x] **T12 — Close.** Roadmap checkbox.
- [x] **T13 — ⏸ owner checkpoint.** Message: "Materials tab with your rig selected: each material,
  which parts use it, which contract properties it has. ⚠ Should Create also assign the new material
  to the part's renderer, or leave that to you?"

---

## 6. Deliberately out of scope

- Editing material properties in the tab.
- Shader Graph generation or shader editing.
- Project-wide material scans (D2).

## 7. Build log

### Phase 0 (stage, 2026-09-14)

- **Version:** the status line said `0.43.0`; A93F–A95F took `0.43.0`–`0.45.0`, so this spec takes `0.46.0`
  (roadmap rule). CHANGELOG's top section is `## [0.45.0]` at `7d036585`.
- **Baseline at `7d036585`:** compile clean; `DotsAnimationToolkit.Tests.EditMode` 851 run, 850 passed, the one failure
  the standing `Conformance_A` (asmdef reference list); `DotsAnimationToolkit.Tests.PlayMode` 285 run, 285 passed.
- **Registry sha256:** `DotsAnimationToolkitAnimEventKeyRegistry.asset`
  `3bdb420d55b808ecfd9251ab144ac89645c4d6f903b4a8a3498a42aa76d14701`; `DotsAnimationToolkitTargetTagRegistry.asset`
  `dbec3d5f6d31db02891682e7f88e6011f7317658f1d29753a0185ff2ebd1eb4f`. Drives must leave both unchanged.
- **Stage untracked files:** none. Nothing on the stage names a type this spec removes or renames.
- **Runs as** a spec-lead (opus) with sonnet workers in a Worktree Toolkit batch beside the other two of A96–A98; the
  stage owns window wiring, `index.md`, CHANGELOG, `package.json`, the conformance pin, drives and the close.

### T0 grounding (spec-lead, worktree `spec/a96`, 2026-09-14)

- **Example shader (D5) — drift 1.** There is no `ToolkitCompositeExample.shader`: `Shaders/` holds only three Shader
  Graphs (`shader-contract.md` still cites the old file by line). Create picks the graph by kind: Quad →
  `ToolkitSpriteUnlit.shadergraph` (atlas: `_MainTex`, `_AtlasFrame`, `_BillboardParams`); FlipbookPlane →
  `ToolkitSpriteUnlitArray.shadergraph` (`_MainTexArray`, `_ImageIndex`, `_BillboardParams`; sheets are arrays);
  VatMesh → `ToolkitVatCrowdUnlit.shadergraph` (`_VatBoneTex`, `_VatTexelParams`, `_VatFrameA/B`, `_VatBlend`).
  None of the three declares a keyword, so D5's "sets keywords" has nothing to set — drift 2.
- **`_MainTexArray` (D4)** confirmed as the array sampler reference name in `ToolkitSpriteUnlitArray.shadergraph`.
- **`TargetKind`** (`Runtime/Components/AnimationToolkitEnums.cs`, namespace `DotsAnimationToolkit`): `Quad = 0`,
  `VatMesh = 1`, `FlipbookPlane = 2`. The spec's `FlipbookQuad` is `FlipbookPlane` — drift 3.
- **Flipbook requirement — drift 4.** The two sprite graphs each carry only one of `_ImageIndex` / `_AtlasFrame`, and
  the baker adds both components to every FlipbookPlane (the frame mode is per track). Requiring both would flag
  both shipped graphs, so the table gives the pair an alternative group: either one satisfies FlipbookPlane.
- **Billboard root — drift 5.** Nothing in the package writes `BillboardParamsProperty` (billboard roots turn
  transforms on the CPU; the contract says "host/game"), so `_BillboardParams` is required for no kind and the
  `isBillboardRoot` parameter of 4.1 was dropped. Quad requires nothing (baker: transform-only).
- **Severity — drift 6.** `ValidationSeverity` has only Warning and Error; D3's "Note" is carried by
  `ContractPropertyState.PresentButNotNeeded` in the status list, not by a message. No `ValidationCode` was added
  (messages use `ValidationCode.None`), so the Health tab, validation docs and code enum are untouched.
- **`ValidateVatMaterial` — drift 7.** It has no texture-size check: it checks the `_VatBoneTex`/`_VatPosTex` slot
  exists and binds the baked texture. No fixture names `ValidateVatMaterial`; its coverage is PlayMode
  `ActorBakingAcceptanceTests`, which pins exact toolkit-warning counts. The contract call therefore runs only after
  the slot check passes, folded into one warning, and the fixture's VAT-capable material had to become
  contract-correct: `VatMaterialProbe.shader` gains `_VatFrameA/_VatFrameB/_VatBlend`, `ActorBakeFixture.
  CreateVatCapableMaterial` turns instancing on. Plain-material and no-material tests return before the call.
- **D6 double-click — drift 8.** `ToolkitCatalogColumn` has no double-click hook; the inspector column's
  "Select in Inspector" button selects and pings the material instead.
- **4.3 signature — drift 9.** `CreateForTarget(rig, target) → Material` became
  `TryCreateForTarget(rig, target, out Material, out string failureMessage)` so the panel can show why (no saved
  prefab, missing graph). It never overwrites (`GenerateUniqueAssetPath`) and saves with `SaveAssetIfDirty`, never
  `SaveAssets`.
- **D4 home.** `RigMaterialResolver.CollectSheetBindingWarnings` (Editor) rather than the Authoring class, to reuse
  `TrackTargetMatchResolver`.

### Wave build and gates (spec-lead, 2026-09-14)

- **Commits on `spec/a96`:** `9e9b74b7` T1 stubs + metas; `cd81d886` T2–T8 wave (seven sonnet workers, one file each,
  docs worker two); `bf90243c` lead's T9 part (baker contract call, probe shader, fixture instancing); `e37157bc`
  section 7 grounding. Lead fixes before the wave commit: a second `<summary>` the panel worker added on `Bind`
  (Conformance_F), and a null-target guard in the panel's target dropdown.
- **EditMode gate at `e37157bc`:** `MaterialContractValidationTests` + `PackagingConformanceTests` — 14 run, 13 passed,
  the one failure the standing `Conformance_A` (Editor asmdef lists `Unity.RenderPipelines.Universal.Runtime`).
  Real: passed equals named minus the standing failure. The first attempt was refused "commit before gating" (dirty
  tree), which is why the baker and section 7 were committed before any gate ran.
- **PlayMode gate:** the broker refuses play-mode fixtures ("not supported by the broker yet"), so
  `DotsAnimationToolkit.Tests.PlayMode.ActorBakingAcceptanceTests` at `e37157bc` was sent to the stage as
  "gate needed". **Stage verdict: PASS** - the stage checked out `e37157bc`, compile gate 0 errors, then ran the fixture: 28 run, 28 passed, 0 failed (28 `[Test]`/`[UnityTest]` in the file, so the count is real).
- **Revert-to-fail:** a mutation commit made `EvaluateProperties` treat every property as required for every kind
  (`bool required = true;`). The gate ran 14: 12 passed, 2 failed — `Quad_MissingVatFrameA_IsNotReported` (five
  errors, `_ImageIndex or _AtlasFrame` … `_BillboardParams`, on a Quad) plus the standing `Conformance_A`.
  `git reset --hard HEAD~1` then put `MaterialContractValidation.cs` back to sha256
  `E7D6BB39484CA83E02977437D065D3A9285B033A7AE51BC43253537E25DD245C`, the same as before. Two earlier mutation
  attempts were reset without a verdict: no broker heartbeat (exit 3), then "Unity is compiling".
- **Unverified here:** nothing was rendered or driven (no MCP in a worktree). Unchecked so far: panel layout, the ✓/✗
  glyphs in the Editor font, `DropdownField` behaviour with an empty choice list, `Create` on a real prefab
  (`GenerateUniqueAssetPath`, `SaveAssetIfDirty`), and whether `AssetDatabase.LoadAssetAtPath<Shader>` on a
  `.shadergraph` returns the graph shader. That is all T10.

### For integration

**CHANGELOG** (`## [0.46.0]`):

```
## [0.46.0] - 2026-09-14
### Added
- Clip Editor Materials tab (after Sprite Sheets). For the shared rig it lists every material on the source prefab's
  renderers, which parts use each, and which shader-contract properties each has and lacks per target kind, plus GPU
  instancing and a sprite-sheet check (a sheet-bound part whose material has no `_MainTexArray`). Read-only; Select in
  Inspector pings the material.
- Create for target: a material from the package's shader graph for the target's kind (Quad → ToolkitSpriteUnlit,
  Flipbook Plane → ToolkitSpriteUnlitArray, VAT Mesh → ToolkitVatCrowdUnlit), instancing on, saved beside the rig's
  prefab as `M_<Rig>_<Target>.mat` without overwriting. It is not assigned to the renderer.
- `MaterialContractValidation` (Authoring): the contract as data. A Flipbook Plane needs `_ImageIndex` or
  `_AtlasFrame`; a VAT Mesh needs `_VatFrameA`, `_VatFrameB`, `_VatBlend`; no kind requires `_BillboardParams`.
- `RigMaterialResolver`, `MaterialTemplateUtility`, documentation page `materials-tab.md`.
### Changed
- Entity bake: a VAT Mesh part whose material has the VAT texture slot also logs one warning listing missing contract
  properties or instancing off.
```

**Conformance_G allowlist:** none needed (`MaterialContractValidation`, `RigMaterialResolver`,
`MaterialTemplateUtility` in `Editor/ClipUtilities/`).

**Wiring (stage-owned files):**
- `ClipEditorTab.Materials` after `SpriteSheets`; UXML toggle `tab-materials` (text "Materials"), pane
  `materials-pane`.
- Build at window creation: `materialsPanel = new MaterialsPanel(); materialsPane.Add(materialsPanel);
  materialsPanel.Bind(sharedSelection);`. The panel subscribes to `RigChanged` and `ClipSetChanged` and never
  writes the selection.
- On showing the tab call `materialsPanel.Refresh()`: prefab and material edits are not observed.
- In teardown: `materialsPanel?.Dispose()` (unsubscribes both events).
- `index.md`: link `materials-tab.md`.
- Element names for layout tests and drives: `materials-rig-name`, `materials-create-target`,
  `materials-create-button`, `materials-result`, catalog `material-catalog-column` (`materials-list`,
  `materials-search`), inspector `material-inspector-title` / `-select` / `-hint` / `-shader` / `-used-by` /
  `-property-<_Name>` / `-instancing` / `-sheet-warning`.
- Split key `Materials.Catalog`.

**Detached drive recipe (T10):**
1. Copy MaleCitizen's rig and prefab into the scratch folder, pointing the copy's `sourcePrefab` at the prefab copy.
2. `MaterialsPanel panel = new MaterialsPanel(); panel.SetRig(rigCopy);`, then read `panel.Usages` (each with
   `Material`, `Targets`, `UnmappedNodePaths`) and `panel.SelectedMaterial`.
3. Swap one material copy's shader to `Unlit/Color`, call `panel.Refresh()`, and check that
   `MaterialContractValidation.Validate` reports errors.
4. Call `panel.CreateForTarget(quadTarget, out string failure)` and read `panel.LastCreatedMaterial`, then its path
   beside the prefab copy and `enableInstancing` after a reimport.
5. `panel.Dispose()`. Delete the scratch folder, and check both registry sha256s are unchanged.

**Vault-note traps:**
- There is no example `.shader`. Create picks one of three shader graphs by kind, and `shader-contract.md` still cites
  `ToolkitCompositeExample.shader` by line number.
- Each sprite graph has only one frame property. The flipbook requirement is therefore an alternative group, never
  both.
- `ActorBakingAcceptanceTests` pins exact toolkit-warning counts. Any new baker warning needs the fixture material
  made contract-correct: the probe shader has frame properties, and the capable material has instancing on.
- The contract messages use `ValidationCode.None`, with no new V code, so the Health tab does not list them.
- Create uses `SaveAssetIfDirty`, never `SaveAssets`.
- The worktree broker refuses play-mode fixtures, and a dirty tree refuses any gate.

**HANDOFF draft:** A96 (0.46.0) adds the Clip Editor Materials tab. It follows the shared rig and lists every
material on the rig's source-prefab renderers, mapped to targets by source node path. For each material it shows the
shader-contract property table per target kind, GPU instancing, and a sheet check against the shared clip set. It is
read-only, and Create makes a material from the kind's shipped shader graph beside the prefab without assigning it.
The contract lives in `MaterialContractValidation` (Authoring), which the entity baker also calls for VAT Mesh parts
once the VAT slot exists. Owner checkpoint T13 is open: should Create also assign the new material to the part's
renderer?

### Close (stage, 2026-09-14)

- **Merge:** fast-forward to `62603be5`, pushed; worktree and branch removed cleanly. The lead's PlayMode gate
  (`ActorBakingAcceptanceTests`, which the broker refuses) ran on the stage by hand at `e37157bc`: `stage-commit`, compile
  clean, 28 of 28, `restore-trunk`. The lead had stopped waiting; the verdict message resumed it and it marked ready.
- **Integration `e7ae55f9`:** `ClipEditorTab` Materials 10, Retarget 11, Capture 12; toggles and cover panes after Health;
  `tabToggles` sized 13; `MaterialsPanel` built with the window (`Bind(selection)`, `Refresh()` on show), `RetargetPanel`
  and `CapturePanel` built lazily on first show with `Bind(selection)`; all three disposed in teardown; layout test lists,
  `index.md`, CHANGELOG 0.46.0–0.48.0, `package.json` and the conformance pin at 0.48.0. Not driven in the real window (the
  docked Clip Editor is the owner's).
- **Gates:** integration fixtures 24 of 25 (`MaterialContractValidationTests`, `RetargetBindingResolverTests`,
  `PngSequenceWriterTests`, `GifEncodingTests`, `ClipEditorLayoutTests`, `PackagingConformanceTests`; Conformance_A the only
  failure); full EditMode 857 run, 856 passed (Conformance_A only; 851 + the batch's six new tests); PlayMode 285 of 285.
- **T10 drive (scratch only, `Assets/A96Scratch/`):** `NewRig.asset` and its source prefab `Assets/Prefabs/Units/MaleCitizen.prefab`
  copied with `AssetDatabase.CopyAsset`, the rig copy's `sourcePrefab` pointed at the prefab copy (34 renderers).
  - Detached `MaterialsPanel` + `SetRig(rigCopy)`: **30 material usages**, 16 mapped to rig targets (e.g. `BaseHead → BaseHead:Quad`,
    `Eyes → Eyes:Quad`) and 14 on nodes no target maps (`Belt`, `Bulge`, `Ear`, `FaceDetails`, `Faceware`, `Hair`, the three UI
    materials, …). First material selected (`BaseHead`); the inspector read "Shader  Shader Graphs/2DShader", "Used by  BaseHead",
    "✓ GPU instancing". The project's unit parts still use the legacy `Shader Graphs/2DShader` graphs, and `Faceware.mat` is on
    `Hidden/InternalErrorShader` (a broken material in the project, not a toolkit bug).
  - A scratch copy of `Torso.mat` switched to `Unlit/Color`: `Validate` as **Quad** returns nothing (a Quad requires no property, and the
    copy kept instancing on); as **FlipbookPlane** one Error "declares no _ImageIndex or _AtlasFrame"; as **VatMesh** three Errors
    (`_VatFrameA`, `_VatFrameB`, `_VatBlend`). With instancing turned off in memory, Quad gives one Error "has GPU instancing off;
    Entities Graphics needs it on". Drift: the spec's "swap to Unlit/Color → errors appear" holds for Flipbook and VAT kinds only.
  - `CreateForTarget(BaseHead, Quad)` wrote `Assets/A96Scratch/M_A96ScratchRig_BaseHead.mat` beside the prefab copy. After
    `Resources.UnloadAsset` and a forced reimport: shader `DOTS Animation Toolkit/ToolkitSpriteUnlit` (YAML `m_Shader` guid resolves to
    `Shaders/ToolkitSpriteUnlit.shadergraph`), `enableInstancing` true (`m_EnableInstancingVariants: 1`), `_AtlasFrame` present,
    no keywords. Not assigned to any renderer.
  - Nothing under `Assets/Materials` or `Assets/Prefabs` changed; scratch deleted; registry sha256s unchanged.
- **Not seen by eye:** the drawn tab (a detached panel has no window to capture), the ✓/✗ glyph rows and the Create dropdown.

### Owner checkpoint answer (2026-09-14)

- **Answered "assign yes":** Create should also assign the new material to the part's renderer. Specced as `A96F_CreateAssignsMaterial_Spec.md` (`0.49.0`),
  not built; the rest of this section's drift stands.
