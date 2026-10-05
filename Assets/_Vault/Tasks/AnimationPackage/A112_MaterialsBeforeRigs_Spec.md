# Amendment A112 — Materials before Rigs: a material's look first, its motion later

> **Status:** 🔨 built 2026-10-04 as **0.66.0**, then reworked the same day (§6): the Materials tab is a per-part input
> check with a red-flag rig preview; the authoring in §1–§3 was built and removed. Editor recompile, fixture run and drive owed.
> **Why:** the owner, 2026-10-04: a rig is a prefab of stacked meshes, "but you need to be able to see what those
> meshes look like to be able to stack them, which means you need to be able to see a material on them." The tab
> strip already reads Cutouts, Materials, Rigs, Clip Sets (1f62fdd7), but the Materials tab still needs a bound
> `RigAsset` before it lists anything, so it cannot be used before a rig exists. A112 makes it work from a
> cutout or mesh alone.

## 0. Grounding (verified 2026-10-04 — do not re-derive)

- `MaterialsPanel` lists only `RigMaterialResolver.Resolve(rig)`, the materials on the rig's `sourcePrefab`.
  Creating a material goes through `MaterialTemplateUtility.TryCreateForTarget` → `ResolveTemplateShaderPath(TargetKind)`.
- Shipped graphs (no keywords in any of them):
  - `ToolkitSpriteUnlit`: `_MainTex _BaseColor _Cutoff _AtlasFrame _BillboardParams`
  - `ToolkitSpriteUnlitArray`: `_MainTexArray _ImageIndex _BaseColor _Cutoff _BillboardParams`
  - `ToolkitVatCrowdUnlit`: `_MainTex _BaseColor _Cutoff _VatBoneTex _VatTexelParams _VatFrameA/B _VatBlend`
- VAT textures are **material-level** (Phase_B_Architecture §1094). Nothing in `Runtime/` or `Editor/` writes
  `_VatBoneTex` onto a real material today: the user does it by hand, and `RigTargetBaker` only warns on a
  mismatch. `VatPreviewMaterial.cs:60-67` is the exact property mapping to copy.
- A bake ends at `VatBakePanel.cs:461` (`VatTextureSetBuilder.WriteSet`). Its parts come from
  `VatBakeSourceResolver` (`VatBakeSource.PrefabRenderer` is a `SkinnedMeshRenderer` on the rig's source prefab).
  `VatTextureSetAsset.TryGetPart(targetId, out VatPartTextures)` gives you `boneTexture`, `textureWidth`,
  `rowsPerFrame` and `boneCount`.
- `CutoutAsset.flipbook` is a `FlipbookAsset`, `Texture2DArray` or `Texture2D`. `ResolveArray()` returns null for
  an image.
- `MaterialTemplateUtility.TryAssignToTargetRenderer` is the existing write path for a prefab material slot
  (`LoadPrefabContents` → save the prefab, never `AssetDatabase.SaveAssets()`).
- `CatalogSidebarElement.AddMode` gives the Cutouts-style switch. `ToolkitChrome.MakeEmptyState` is the "empty, here's
  Create" card.

## 1. Decisions (orchestrator, 2026-10-04 — ⚠ marks the ones the owner did not say outright)

- **M-D1 — A material has two parts: its look and its motion.** The look (texture, tint, cutoff) is made in the
  Materials tab, before any rig exists. The motion is either **Flipbook** or **VAT**.
- **M-D2 — Flipbook comes from where the art came from** (owner's idea): if the cutout's source resolves to a
  `Texture2DArray`, the material gets Flipbook. A `Texture2D` image source, or a bare mesh, gets none. The user can
  still switch the Flipbook toggle afterwards.
- **M-D3 — VAT is turned on by the bake, never ticked.** A VAT material with no bone texture collapses the mesh, so
  the Materials tab only *shows* VAT state ("Not baked: VAT Bake turns this on" / "Baked by <set>"). After
  `WriteSet`, the VAT Bake tab updates each baked part's material. The owner confirmed VAT is per object, so one
  material per baked part is the normal case. ⚠ If one material is shared by two baked parts, that part is
  **refused and reported** ("give each part its own material"), not copied silently.
- **M-D4 — The material's motion is read from its shader, not stored anywhere.** `_MainTexArray` + `_ImageIndex`
  means Flipbook; `_VatBoneTex` means VAT. No new fields and no keywords, so the same check works on the owner's
  custom shaders.
- **M-D5 — Shipped shaders are chosen from a table, not merged into one graph** ⚠. None → `ToolkitSpriteUnlit`,
  Flipbook → `ToolkitSpriteUnlitArray`, VAT → `ToolkitVatCrowdUnlit`. Turning a feature on or off on a toolkit
  material swaps the shader on the same asset, so the GUID and the renderers that use it survive. `_MainTex`,
  `_BaseColor` and `_Cutoff` carry over between the static and VAT graphs. Flipbook ↔ static changes the texture
  type, so the texture is cleared and the footer says so. Flipbook + VAT has no shipped shader and is refused with
  "use your own shader". A single keyword-based graph is deferred (§4).
- **M-D6 — Custom shaders are first-class.** The Shader card shows **Toolkit** or **Custom**. On a custom shader,
  the feature toggles are read-only. They show which features its properties provide and list the missing contract
  properties (`MaterialContractValidation.EvaluateProperties`), pointing at `Shaders/Nodes/*.hlsl` for a Custom
  Function node. The VAT bake writes textures onto a custom material when it has `_VatBoneTex`. It never swaps a
  custom shader.
- **M-D7 — The sidebar is Materials | Cutouts | Meshes**, as on the Cutouts tab (owner).
  - **Materials** lists every material on a toolkit graph, plus the bound rig's materials.
  - **Cutouts** lists `CutoutAsset`s.
  - **Meshes** lists every `Mesh` asset under `Assets/`.
  - Selecting a cutout or mesh shows its material in the inspector. With none, it shows a `MakeEmptyState` card:
    **Create material**.
- **M-D8 — How a cutout or mesh knows its material** ⚠. A cutout stores it: new field `CutoutAsset.material`. A
  bare mesh has no field, so its material is found as `M_<MeshName>.mat` in the mesh's folder (the name Create
  writes). The default folder for a new material is the mesh's or cutout output's folder.
- **M-D9 — Putting the material on the rig** ⚠. When a rig is bound, the inspector offers **Use in rig: N parts**.
  It assigns the material to every renderer slot in `rig.sourcePrefab` whose `MeshFilter.sharedMesh` is this
  mesh, using the `TryAssignToTargetRenderer` write path. The rig and clip-set fields stay as an optional
  **Check against** row, so the existing contract check against a clip set still runs.
- **M-D10 — Bone-flavour VAT only.** A vertex-flavour bake leaves materials untouched and says why in the report;
  the shipped graph has no `_VatPosTex`.

## 2. Pinned contract (wave 2 codes against exactly this)

```csharp
namespace DotsAnimationToolkit.Editor
{
    [System.Flags] public enum MaterialFeature { None = 0, Flipbook = 1, Vat = 2 }

    public static class MaterialFeatureResolver                       // Editor/Materials/MaterialFeatureResolver.cs
    {
        public static MaterialFeature ReadFeatures(Material material);            // M-D4
        public static bool IsToolkitShader(Shader shader);                        // one of the three shipped graphs
        public static string ResolveToolkitShaderPath(MaterialFeature features);  // null for Flipbook|Vat
        public static MaterialFeature FeaturesForCutout(CutoutAsset cutout);      // M-D2
        public static string ComputeDefaultMaterialPath(Mesh mesh);               // "<mesh folder>/M_<MeshName>.mat"
        public static Material FindMaterialForMesh(Mesh mesh);                    // M-D8 lookup, null when none
    }

    public static class MaterialAuthoringUtility                      // Editor/Materials/MaterialAuthoringUtility.cs
    {
        public static bool TryCreateMaterial(string assetPath, MaterialFeature features, Texture sourceTexture,
            out Material createdMaterial, out string failureMessage);
        public static bool TrySetToolkitFeatures(Material material, MaterialFeature features,
            out string resultMessage);                                            // M-D5 swap; refuses custom shaders
    }

    public static class MaterialMeshAssignment                        // Editor/Materials/MaterialMeshAssignment.cs
    {
        public static int CountRendererSlotsUsingMesh(GameObject prefab, Mesh mesh);
        public static bool TryAssignToRenderersUsingMesh(GameObject prefab, Mesh mesh, Material material,
            out int assignedSlotCount, out string failureMessage);
    }

    public static class VatMaterialUpgrader                           // Editor/VatBaking/VatMaterialUpgrader.cs
    {
        // Returns one human line per part ("BaseHead: switched to VAT, textures set" / "...: refused, shared with X").
        public static void UpgradeMaterialsForSet(VatTextureSetAsset textureSet,
            IReadOnlyList<VatBakeSource> bakedSources, List<string> reportLines);
    }
}
// CutoutAsset gains: [Tooltip("The material this cutout's mesh is shown with (A112).")] public Material material;
```

## 3. Tasks

Every worker: edits only the files it is named, reads only the line ranges it is given, runs
`python Tools/devloop/devloop.py gate com.dotsanimationtoolkit` before reporting, stops editing at turn 30, and
writes a report of 30 lines or fewer. No Unity MCP.

- **Wave 1 [parallel, disjoint files]**
  - [x] T1 — `MaterialFeatureResolver.cs` (new, includes the `MaterialFeature` enum) +
    `Tests/EditMode/MaterialFeatureResolverTests.cs`:
    - `ReadFeatures_EachShippedGraph_ReportsItsFeature`
    - `ResolveToolkitShaderPath_FlipbookAndVat_IsNull`
  - [x] T2 — `MaterialAuthoringUtility.cs` (new). Reads `MaterialTemplateUtility.cs:60-116` for the creation
    pattern. Codes against T1's signatures as pinned. Fixture `TrySetToolkitFeatures_StaticToVat_KeepsBaseColor`.
  - [x] T3 — `MaterialMeshAssignment.cs` (new). Reads `MaterialTemplateUtility.cs:118-end` for the prefab write path.
  - [x] T4 — `VatMaterialUpgrader.cs` (new). Copies the property mapping from `VatPreviewMaterial.cs:60-67`;
    M-D3/M-D6/M-D10. Fixtures:
    - `Upgrade_ToolkitStaticMaterial_BecomesVatWithBoneTexture`
    - `Upgrade_MaterialSharedByTwoParts_IsRefused`
  - [x] T5 — `CutoutAsset.cs`: the `material` field only.
- Gate, then the four fixtures. Revert-to-fail each one in the Editor; delete any test that cannot fail. Commit.
- **Wave 2 [parallel, codes against §2]**
  - [x] T6 — `MaterialsPanel.cs`: the `CatalogSidebarElement` with three modes, rig and clip-set moved into a
    **Check against** row, and selection routing (a cutout or mesh resolves its material per M-D8).
  - [x] T7 — `MaterialCatalogColumn.cs` (Materials scan per M-D7) + new `MeshCatalogColumn.cs`
    (`ToolkitCatalogColumn<Mesh>`, empty state with a Create action). The Cutouts mode reuses `CutoutCatalogColumn`
    as-is.
  - [x] T8 — `MaterialInspectorColumn.cs`:
    - **Motion** card: a Flipbook toggle and a VAT status line.
    - **Shader** card: Toolkit/Custom plus missing-property hints.
    - The empty-state **Create material** card.
    - The **Use in rig: N parts** action.
  - [x] T9 — `VatBakePanel.cs`: after `WriteSet` (line 461), call `VatMaterialUpgrader` and add its lines to the
    success report.
- Gate, lint, Editor recompile, then the EditMode run for touched fixtures. Commit.
- [ ] T10 — Orchestrator:
  - CHANGELOG 0.66.0 and HANDOFF.
  - `shader-contract.md`: a "Your own shader" section (the properties per feature, `Shaders/Nodes/` as Custom
    Function nodes).
  - The drive, on scratch copies: a cutout from a flipbook → Create material → it is Flipbook. An image cutout →
    a static material. Use in rig → the prefab's slot changes. A VAT bake → the part's material swaps to the VAT
    graph with `_VatBoneTex` set. A shared material → refused.
  - Captures, and the R01–R22 audit.
- [ ] T11 — ⏸ Owner checkpoint: the ⚠ decisions (M-D3 refuse-when-shared, M-D5 table over keywords, M-D8 name
  lookup, M-D9 Use in rig).

## 4. Deferred (recorded, not built)

- One keyword-based graph per lighting model (`_TOOLKIT_FLIPBOOK` / `_TOOLKIT_VAT`) replacing the table. This
  also unlocks Flipbook + VAT, and needs `shader-edit` surgery on a graph, not a small worker task.
- Shipping the `Shaders/Nodes` functions as Sub Graphs for custom shaders. The same surgery applies.
- Adding parts to a rig from the Rigs tab. Rigs are still built as prefabs by hand; M-D9 only assigns materials.

## 5. Log

- 2026-10-04: specced from the owner's voice note. Owner on VAT: "a vat would be per object" (read as: one
  material per baked part is fine, since the bone texture is a second sample).
- 2026-10-04: built. T1 + T5 by the orchestrator, T2–T4 and T6–T9 by seven parallel workers against §2 and a pinned
  wave-2 surface (`MaterialInspectorSubject`; `MaterialInspectorColumn.Bind(subject, rig, clipSet)` with
  MaterialCreated / MaterialChanged / StatusReported; `MeshCatalogColumn`). Offline gate and lint pass. The Unity MCP
  bridge was down for the session, so the Editor recompile, the four fixtures' revert-to-fail, the drive and the
  captures are still owed (T10). Calls made while building, beyond the ⚠ list: the per-target **Create and assign**
  (0.49.0) is removed, replaced by Create material on a cutout/mesh + Use in rig; Materials-mode **+** asks for a
  save path and makes a static material; Meshes-mode **+** switches to Cutouts; a Flipbook material baked as VAT is
  refused like any Flipbook + VAT.

## 6. Rework R — the Materials tab is a check, not an editor (owner, 2026-10-04)

Owner, after seeing the build: "there's nowhere to set the values of the material or assign a texture ... maybe this
tab isn't actually that useful if I am going to be just redoing the Unity materials tab. I want it to go back to
being based on rigs and clip set, and it being a check to make sure all the right parts have the right inputs based
on what their animation says. Then it's up to the user to make sure those inputs are wired shader side; we provide
pre-done shaders, but they can build their own modelled after ours. I still want the 3D view, but I want it to
highlight a mesh red if it is missing inputs that its animations want."

Settled the same day (owner's answers):
- **R-D1** Every A112 authoring piece is removed: the Cutouts/Meshes sidebar, Create material, the Flipbook toggle,
  Save as prefab, Use in rig, `CutoutAsset.material`, the catalog thumbnail option, `MaterialFeatureResolver`,
  `MaterialAuthoringUtility`, `MaterialMeshAssignment`, `MaterialPrefabSaving`, `MaterialPreviewElement`.
  M-D1, M-D2, M-D5, M-D7, M-D8, M-D9 are void.
- **R-D2** VAT Bake still writes `_VatBoneTex` + `_VatTexelParams` onto each baked part's material when the material
  declares them, but never swaps a shader. A material without them is reported (here and in the bake report).
  Refuse-when-shared (M-D3) and bone-only (M-D10) stay.
- **R-D3** The left column lists **rig parts** (one per target) with ✓/✗; the right column shows the selected part's
  needs, which its material lacks, and which clips ask for each.
- **R-D4** A part missing inputs is drawn with a **red tint over its texture** in the 3D view of the rig's prefab; the
  selected part is boxed.
- **R-D5** (orchestrator) Needs come from the clip set: a slice sprite track needs `_ImageIndex` + `_MainTexArray`; an
  atlas sprite track `_AtlasFrame`; a VAT track (by target) or the clip's `vatSource` (every VatMesh target)
  `_VatFrameA`, `_VatFrameB`, `_VatBlend`, `_VatBoneTex`, `_VatTexelParams`. Transform tracks need nothing. No clip set
  = nothing is checked, and the tab says to pick one. The preview reuses the scale-aware camera
  (`MaterialPreviewCameraRig`) with billboarding off through a property block.
- **Built (R), cd05c362:** `PartInputCheck` (+ fixture), `MaterialPartListColumn`, `MaterialCheckPreviewElement` (red
  overlay per source texture with alpha clip; array materials get a plain wash since URP Unlit cannot sample an
  array), `MaterialInspectorColumn` and `MaterialsPanel` rewritten, `VatMaterialUpgrader` writes textures only
  (fixtures updated). Deleted: MaterialFeatureResolver, MaterialAuthoringUtility, MaterialMeshAssignment,
  MaterialPrefabSaving, MaterialPreviewElement, MeshCatalogColumn, MaterialCatalogColumn and their tests;
  ToolkitCatalogColumn, ToolkitComponents.uss and CutoutAsset reverted to 10d1a9c6. `MaterialPreviewCameraRig` kept.
  Gate + lint only; the Unity MCP bridge was down all session.
