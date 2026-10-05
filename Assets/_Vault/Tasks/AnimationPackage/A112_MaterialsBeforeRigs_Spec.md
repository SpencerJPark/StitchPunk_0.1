# Amendment A112 — Materials before Rigs: a material's look first, its motion later

> **Status:** 📝 specced 2026-10-04, not built. Ships as **0.66.0**.
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
  - [ ] T1 — `MaterialFeatureResolver.cs` (new, includes the `MaterialFeature` enum) +
    `Tests/EditMode/MaterialFeatureResolverTests.cs`:
    - `ReadFeatures_EachShippedGraph_ReportsItsFeature`
    - `ResolveToolkitShaderPath_FlipbookAndVat_IsNull`
  - [ ] T2 — `MaterialAuthoringUtility.cs` (new). Reads `MaterialTemplateUtility.cs:60-116` for the creation
    pattern. Codes against T1's signatures as pinned. Fixture `TrySetToolkitFeatures_StaticToVat_KeepsBaseColor`.
  - [ ] T3 — `MaterialMeshAssignment.cs` (new). Reads `MaterialTemplateUtility.cs:118-end` for the prefab write path.
  - [ ] T4 — `VatMaterialUpgrader.cs` (new). Copies the property mapping from `VatPreviewMaterial.cs:60-67`;
    M-D3/M-D6/M-D10. Fixtures:
    - `Upgrade_ToolkitStaticMaterial_BecomesVatWithBoneTexture`
    - `Upgrade_MaterialSharedByTwoParts_IsRefused`
  - [ ] T5 — `CutoutAsset.cs`: the `material` field only.
- Gate, then the four fixtures. Revert-to-fail each one in the Editor; delete any test that cannot fail. Commit.
- **Wave 2 [parallel, codes against §2]**
  - [ ] T6 — `MaterialsPanel.cs`: the `CatalogSidebarElement` with three modes, rig and clip-set moved into a
    **Check against** row, and selection routing (a cutout or mesh resolves its material per M-D8).
  - [ ] T7 — `MaterialCatalogColumn.cs` (Materials scan per M-D7) + new `MeshCatalogColumn.cs`
    (`ToolkitCatalogColumn<Mesh>`, empty state with a Create action). The Cutouts mode reuses `CutoutCatalogColumn`
    as-is.
  - [ ] T8 — `MaterialInspectorColumn.cs`:
    - **Motion** card: a Flipbook toggle and a VAT status line.
    - **Shader** card: Toolkit/Custom plus missing-property hints.
    - The empty-state **Create material** card.
    - The **Use in rig: N parts** action.
  - [ ] T9 — `VatBakePanel.cs`: after `WriteSet` (line 461), call `VatMaterialUpgrader` and add its lines to the
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
