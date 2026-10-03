# Amendment A111 — Cutouts tab: tight flat meshes drawn around flipbook art

> **Status:** ✅ built 2026-10-03 as 0.63.0 (T0–T12); ⏸ T13 owner checkpoint open on the captures in `Library/A111Captures/`.
> **Roadmap:** after Phase 6; standalone tab, no runtime change.
> **Why:** a flipbook part renders on a quad, and every transparent pixel inside that quad still runs the fragment
> shader (the shipped `ToolkitSpriteUnlitArray` is opaque + alpha clip, so empty pixels cost a sample and a discard,
> in every pass). Each part is already one instanced draw across the crowd, so a tighter mesh costs no extra draws
> and cuts fragment work in proportion to the area removed. Doing this in Blender means round-tripping every part
> and losing sight of the other frames; this tab does it beside the flipbooks.
> **Executor:** one orchestrator; `worker` subagents in **two waves** (five pure/asset tasks, then four UI tasks),
> each ≤ 2 files, one gate per wave; window wiring and the drive stay with the orchestrator.

---

## 0. Session prompt (paste into a fresh session)

You are running **Amendment A111 — Cutouts tab** on the DOTS Animation Toolkit package (head `0.61.0` or later).
Spec: `Assets/_Vault/Tasks/AnimationPackage/A111_CutoutsTab_Spec.md`. Read it, `Docs/AnimationToolkit/EditorStyleGuide.md`
(R01–R22 bind every screen here), then only what §3 names. Run `ListAgents` first — a peer session may be mid-edit on
the empty-list work in `CHANGELOG.md`'s `[Unreleased]` section. T0 yours; wave 1 (T1–T5), gate; wave 2 (T6–T9), gate;
T10–T12 yours. Stop at T13. Workers never touch Unity MCP; every worker runs
`python Tools/devloop/devloop.py gate com.dotsanimationtoolkit` before its report.

---

## 1. Goal

A **Cutouts** tab, right after Flipbooks in the tab list. Pick a flipbook, flip through its frames over a world-unit
grid, size the art against a reference image, draw a flat polygon around it (a quad to start, more vertices for a
tighter fit), drag the origin, choose which way the face points, and save a `Mesh` where you say. The shape is one
mesh shared by every frame of the flipbook, so it has to cover all of them; the tab shows where any frame pokes out.

```
┌ Cutouts ─────────────────────────────────────────────────────────────────────────────────────────────────┐
│ ✂ Arm_Upper.cutout                                              ● 6 verts · 41% of quad  [⬇ Save Mesh] │
│ ┌ [Flipbooks|Cutouts] ┐ ┌ Canvas ───────────────────────────────────────┐ ┌ Inspector ─────────────────┐ │
│ │ 🔍 search            │ │ ▣ ◇ ⊕ ▢      (rail: fit, shape, origin, ref)  │ │ ▾ Flipbook                 │ │
│ │ ▸ MaleCitizen_Arms   │ │   ┼───┼───┼───┼───┼   1 unit grid             │ │   Pixels / unit   100      │ │
│ │ ▸ MaleCitizen_Head   │ │   │   ╱‾‾‾‾╲ │   │    ← polygon over art       │ │   Frame  ◀ 3 / 12 ▶ Arm_03 │ │
│ │ ▸ MaleCitizen_Torso  │ │   ┼──╱──⊕──╲─┼───┼    ⊕ origin gizmo           │ │   Show all frames  ☑       │ │
│ │   …                  │ │   │  ╲____╱  │   │    ░ reference image        │ │ ▾ Shape                    │ │
│ │                      │ │   ┼───┼───┼───┼───┼                           │ │   Vertices 6  [Fit to art] │ │
│ │                      │ │        ◀  3 / 12 · Arm_03  ▶                  │ │   Padding  2 px            │ │
│ │                      │ │                                               │ │ ▾ Origin   x 0.00 y 0.42   │ │
│ │                      │ │                                               │ │ ▾ Facing   −Z (toward cam) │ │
│ │                      │ │                                               │ │ ▾ Reference  [img] 0.6 α   │ │
│ │ ● 14 flipbooks       │ │ ● frame 7 pokes out by 3 px                   │ │ ▾ Output  Assets/…/Meshes │ │
│ └──────────────────────┘ └───────────────────────────────────────────────┘ └────────────────────────────┘ │
└──────────────────────────────────────────────────────────────────────────────────────────────────────────┘
```

Layout follows the Ragdoll tab's reference composition (style guide §5 reference image): flush columns, the
catalog in a `CatalogSidebarElement` with a segmented Flipbooks/Cutouts mode (SG-D1, as the Flipbooks tab does with
Flipbooks/Images), a `ViewportFrameElement`-style rail on the canvas (R20), cards in the inspector (R09), one primary
action top right (R12), status in the footer (R14), a designed empty state for no flipbook (R13).

---

## 2. Decisions (recorded 2026-10-03 — do not re-ask).

- **A111-D1 — Name: "Cutouts"** (owner, 2026-10-03). One word like its neighbours; matches `cutout-characters.md`'s vocabulary; says
  what the tool does (cut the mesh out around the art). "Sprite Meshes" and "Part Meshes" were offered and declined.
- **A111-D2 — `ClipEditorTab.Cutouts = 15`**, toggle `tab-cutouts`, pane `cutouts-pane`, placed after Flipbooks in
  the uxml strip. Numbers are ids, not positions (0.48.1 rule).
- **A111-D3 — The canvas is 2D (Painter2D, orthographic, front view), in world units.** Editing a flat polygon is a
  2D job; a Painter2D element is drivable and testable where a `PreviewRenderUtility` stage is not
  (reference_editor_background_verification_limits), and no `Handles.` is allowed in the package Editor anyway.
  Grid: 1-unit major lines, 0.1 minor, labelled at the axes, like the 3D views. Zoom to cursor on wheel, pan on
  middle drag / Alt+left drag — the same bindings as `PreviewCameraNavigation`.
- **A111-D4 — Size is pixels-per-unit**, exactly Unity's sprite meaning (default 100). Changing it rescales the art and
  the shape together (the shape is stored in **pixel space**, D6, so it stays on the art); the reference image keeps
  its world size — that is how the art is matched to it.
- **A111-D5 — Reference image**: any `Texture2D`, drawn under the art with an opacity slider, placed and scaled in
  world units (drag to move, corner drag or fields to size). Its purpose is to size the art: match the art to the
  reference by changing pixels-per-unit. Stored on the cutout asset; never baked into the mesh.
- **A111-D6 — One shape per flipbook, stored in the frame's pixel space.** Every frame of an array is the same size,
  so one polygon fits all of them. "Show all frames" draws the alpha union of every layer as a ghost; a frame whose
  opaque pixels fall outside the polygon is a footer warning naming the frame and the overhang in px.
- **A111-D7 — Shape editing:** starts as the full quad. Drag a vertex; click an edge to insert one; select + Delete
  removes one (never below 3); snap to pixels by default, to grid with Ctrl. Concave shapes allowed; a
  self-intersecting polygon blocks Save with the reason. Triangulated by ear clipping.
- **A111-D8 — Fit to art** (owner: yes, then hand-edit): one button traces the alpha union of all frames, takes its convex hull, expands by
  the padding (default 2 px, for bilinear and mip bleed), and simplifies to the vertex budget (default 8). The owner
  edits from there. Convex only for the automatic fit; hand edits may go concave.
- **A111-D9 — Origin**: a draggable ⊕ gizmo plus x/y fields; snaps to pixel, grid with Ctrl. The mesh's local (0,0,0)
  is the origin, so the part rotates about it — put it on the joint. Presets row: centre, bottom-centre, and each
  corner.
- **A111-D10 — Facing and normals** (owner: flat + rounded). The mesh lies in the XY plane. **Facing** picks −Z
  (toward a default camera, same as Unity's built-in Quad) or +Z. **Normals** picks **Flat** (every normal = facing)
  or **Rounded** with a 0–1 **Roundness**: each outline vertex's normal is facing bent outward along the direction
  from the shape's centroid, by `roundness` (1 = 45°), so lit parts shade like a soft form. Every vertex is on the
  outline, so the middle of a face is the interpolation of its corners; that is the accepted limit (no interior
  vertices in this amendment). Tangents follow the UVs in both modes. Double-sided is a material concern, not offered.
- **A111-D11 — UVs come from position**: each vertex's UV is its pixel coordinate over the frame size, so nothing
  stretches and the same UVs work for every slice of the array.
- **A111-D12 — Output (owner: cutout asset + mesh): a `CutoutAsset` (editor-only ScriptableObject, Editor assembly — the `TexturePackRecipeAsset`
  precedent) plus a generated `Mesh` asset at the chosen path.** The cutout keeps the flipbook, pixels-per-unit, the
  polygon, origin, facing and the reference placement, so reopening it restores the session; Save rewrites the
  same mesh asset in place (GUID kept, so every MeshFilter using it updates). Path chosen with `PathPickerRowElement`,
  remembered per machine like the Clip Sets folder.
- **A111-D13 — Feedback numbers** in the asset bar: vertex count and the polygon's area as a percentage of the full
  quad ("41% of quad") — the transparent-pixel saving made visible.
- **A111-D14 — Out of scope:** assigning the mesh to a rig part's MeshFilter (Materials tab owns part renderers;
  a later "Use on part" is one button if wanted), multiple islands per mesh, bendy/VAT subdivision.

---

## 3. Read first (named ranges only)

- `Editor/Flipbooks/FlipbooksPanel.cs` 1–140 (composition, sidebar modes, empty state, asset bar).
- `Editor/Flipbooks/FlipbookCatalogColumn.cs` in full (221) — reused as-is for the Flipbooks mode.
- `Editor/Flipbooks/FlipbookLayerThumbnailCache.cs` in full — GPU copy per layer; **not CPU-readable**, so the alpha
  tracer reads through a RenderTexture + `ReadPixels` (T0 confirms on a compressed array).
- `Editor/ClipEditor/Shared/ToolkitCatalogColumn.cs`, `CatalogSidebarElement.cs`, `ViewportFrameElement.cs`,
  `PathPickerRowElement.cs`, `ToolkitChrome.cs`, `ToolkitEmptyListSurface.cs` — public surface only (grep `public`).
- `Editor/ClipEditor/Preview/PreviewCameraNavigation.cs` 1–60 — the zoom/pan bindings to mirror.
- `Editor/TexturePacker/TexturePackRecipeAsset.cs` 40–60 — editor-only asset pattern.
- `Editor/ClipEditor/ClipEditorWindow.cs` 1425–1480 (BindTabs), 1690–1705 (Show*Tab); `ClipEditorWindow.uxml` line 7.
- `Tests/EditMode/ClipEditorLayoutTests.cs` ~90, `EditorStyleConformanceTests.cs` ~30 — lists a new tab joins.

---

## 4. Design (new files under `Editor/Cutouts/`)

| File | Kind | Surface |
|---|---|---|
| `CutoutAsset.cs` | SO | `flipbook` (Object: FlipbookAsset or Texture2DArray), `pixelsPerUnit`, `List<Vector2> outlinePixels`, `Vector2 originPixels`, `CutoutFacing facing`, `CutoutNormalMode normalMode`, `float roundness`, `Texture2D referenceImage`, `Rect referenceRectWorld`, `float referenceOpacity`, `Mesh outputMesh`, `string outputPath` |
| `CutoutMeshBuilder.cs` | pure | `static bool TryBuild(IReadOnlyList<Vector2> outlinePixels, Vector2Int frameSize, Vector2 originPixels, float pixelsPerUnit, CutoutFacing facing, CutoutNormalMode normalMode, float roundness, Mesh target, out string failureReason)` |
| `PolygonTriangulator.cs` | pure | `static bool TryEarClip(IReadOnlyList<Vector2> polygon, List<int> triangles)`, `static bool IsSelfIntersecting(...)`, `static float SignedArea(...)` |
| `CutoutAlphaTracer.cs` | pure + GPU read | `static bool[] ReadAlphaUnion(Texture2DArray array, float alphaThreshold)`, `static List<Vector2> FitOutline(bool[] mask, Vector2Int size, int vertexBudget, float paddingPixels)`, `static List<FrameOverhang> FindOverhangs(...)` |
| `CutoutCanvasElement.cs` | UI | Painter2D grid, frame, union ghost, reference, polygon, origin; zoom/pan; `event Action ShapeChanged` |
| `CutoutShapeManipulator.cs` | UI | vertex drag/insert/delete, origin drag, reference drag/resize, snapping |
| `CutoutInspectorColumn.cs` | UI | the six cards of §1 |
| `CutoutCatalogColumn.cs` | UI | `ToolkitCatalogColumn<CutoutAsset>` |
| `CutoutsPanel.cs` | UI | composition, asset bar, Save Mesh, footer status, working copy + undo |

---

## 5. Tasks

- [x] **T0 — Baseline (orchestrator).** `ListAgents`. Gate the package. Before-captures of Flipbooks + the tab list.
  Confirm a RenderTexture `ReadPixels` returns alpha for a compressed `Texture2DArray` layer (one `execute_code`
  probe — feedback_probe_platform_before_designing). Pick the next free minor for the changelog.
- **Wave 1 [parallel-safe, disjoint files]**
  - [x] **T1 — Triangulator + fixture.** Files: `PolygonTriangulator.cs`, `Tests/EditMode/PolygonTriangulatorTests.cs`.
    `EarClip_ConcaveL_ProducesFourTriangles` and `SelfIntersectingBowtie_IsRejected`. Revert-to-fail: skip the
    reflex-vertex check → the L test fails.
  - [x] **T2 — Mesh builder + fixture.** Files: `CutoutMeshBuilder.cs`, `Tests/EditMode/CutoutMeshBuilderTests.cs`.
    `Quad_OriginAtBottomCentre_SitsOnZero` (vertex y min = 0), `FacingNegativeZ_NormalsPointNegativeZ`, and
    `Rounded_CornerNormalsLeanAwayFromCentroid`. Revert-to-fail: drop the bend → the rounded test fails.
  - [x] **T3 — Alpha tracer + fixture.** Files: `CutoutAlphaTracer.cs`, `Tests/EditMode/CutoutAlphaTracerTests.cs`
    (pure `FitOutline` on a hand-built mask: a diamond fits in ≤ budget vertices and contains every set pixel).
  - [x] **T4 — Asset + enum.** Files: `CutoutAsset.cs`, `CutoutEnums.cs` (`CutoutFacing`, `CutoutNormalMode`).
  - [x] **T5 — Tab glyph.** Files: `ToolkitGlyphs.Surfaces.cs` (one drawn "cutout" glyph matching the fifteen —
    one stroke weight, R21).
- **Gate wave 1.** Run T1–T3 fixtures. Commit `A111 wave 1`.
- **Wave 2 [parallel-safe; each gets wave 1's public signatures pasted into its brief]**
  - [x] **T6 — Canvas.** Files: `CutoutCanvasElement.cs`.
  - [x] **T7 — Manipulator.** Files: `CutoutShapeManipulator.cs`.
  - [x] **T8 — Inspector + catalog.** Files: `CutoutInspectorColumn.cs`, `CutoutCatalogColumn.cs`.
  - [x] **T9 — Panel.** Files: `CutoutsPanel.cs` (hosts T6–T8 by the §4 surfaces).
- **Gate wave 2.** Commit `A111 wave 2`.
- [x] **T10 — Window wiring (orchestrator).** Enum, uxml toggle after Flipbooks, `BindTab` tooltip, Show/hide,
  layout + conformance lists, `package.json`, `CHANGELOG.md`. Gate + `lint`.
- [x] **T11 — Docs [worker].** Files: `Documentation~/cutouts.md` (new), `Documentation~/index.md` (one row + one
  further-reading line). `cutout-characters.md` gets one sentence pointing here.
- [x] **T12 — Drive + captures (orchestrator, Editor focused).** MaleCitizen's arm flipbook: frames cycle with ◀ ▶;
  Fit to art lands ≤ 8 verts covering every frame; drag the origin to the shoulder; Save → mesh at the chosen path;
  drop it on a MeshFilter in a scratch scene → art unstretched, pivot at the shoulder, normals −Z; reopen the cutout
  → session restored; edit + Save → same GUID. Before/after captures audited against R01–R22 in §6 (R22).
- [ ] **T13 — ⏸ Owner checkpoint.** The owner's own visual pass on the captures.

---

## 6. Log

- 2026-10-03: drafted from the owner's description; D1 Cutouts, D8 fit then hand-edit, D10 flat + rounded, D12 asset +
  mesh answered in-session the same day.
- 2026-10-03 T0: `ListAgents` — no peer editing the package (the `[Unreleased]` empty-list work is already committed in
  e35b85ac). Gate PASS at 0.61.0. Probe: `Graphics.Blit(array, rt, layer, 0)` + `ReadPixels` on the non-readable
  `RGBA_DXT5_SRGB` `EarArray` (64×64×16) returned 958 opaque / 3020 transparent px, different per layer — the tracer
  reads through it. The project has **no `FlipbookAsset` instances**, only bare arrays (Ear/Eye/Head/Mouth/Nose…), so the
  T12 drive uses a bare array, not "MaleCitizen's arm flipbook". Version: `[Unreleased]` ships as **0.62.0** and A111
  takes **0.63.0** (T10). Before-captures deferred to T12: the Editor was unfocused, and an unfocused GrabPixels is stale.
- 2026-10-03 wave 1 (T1–T5, five parallel workers): gate PASS, lint PASS, Editor recompile clean, 12/12 (the three new
  fixtures + `ToolkitButtonStyleTests`, which resolves every glyph id). Revert-to-fail run in the Editor: dropping the
  triangulator's containment check fails the L test (9 indices, not 12); zeroing the bend fails the rounded test;
  pixel centres instead of padded corners fail the diamond test. T1 deviation: the brief's 2×2 L passes without the
  containment check (the notch only touches the first ear), so the L is 3×3 (area 5) where the notch sits inside it.
- 2026-10-03 wave 2 (T6–T9, four parallel workers against one pinned contract) + T10/T11: gate PASS (Editor 280
  files, every new file compiled), lint PASS. The first Editor run failed `Conformance_G`: `PolygonTriangulator`,
  `CutoutAlphaTracer` and `CutoutMeshWriter` carry no role suffix — they joined the plain-noun allowlist
  (`PngSequenceWriter` / `VatTextureBaker` precedent) rather than renaming the spec's own §4 names. Commits f454f8de
  (wave 1), 7a14e7f4 (wave 2), 4ddcaae4 (T10–T11). Version: committed `[Unreleased]` → 0.62.0, A111 → 0.63.0.
- 2026-10-03 T12 drive (Editor focused, captures in `Library/A111Captures/`): `HeadArray` (64 × 256², DXT5) loads in
  399 ms including the 64-layer alpha read. Frames cycle; **Fit to art → 8 verts, 56% of quad**, footer "Every frame
  fits". Origin (128, 10) → Save → mesh: 8 verts / 6 tris, UV-vs-position error 0 px (unstretched), every normal and
  every triangle's winding −Z, bounds min y −0.08 = (2.1 − 10)/100 (pivot at the origin). Pick another array, pick
  HeadArray again → its cutout reopens with outline + origin restored, not unsaved. Delete a vertex + Rounded +
  Save → **same mesh GUID**, 7 verts, first normal leans out (−0.08, −0.37, −0.92); one cutout asset (updated in
  place). Undo of a pixels/unit edit restores 100. Scratch assets deleted; owner's tab restored.
  Fixes from the captures: footer moved under the canvas column with the column inset (R14/R05); canvas column
  flush (was in a grey gutter); "−Z (toward camera)" clipped "+Z" → "−Z"/"+Z" + tooltip (R01); inspector showed a
  column of zeros with nothing loaded → only the hint (R13); axis labels ride the axes per D3 and clear the rail,
  no "0" on the origin gizmo; overhang status numbered from 1 like the frame stepper (R03); saved names
  `X_CutoutMesh` + `X_Cutout` instead of `X_Cutout` + `X_Cutout_Data`.
  **R01–R22 audit (after_cutouts_final.png):** R01 ✓ (row ellipsis has its tooltip) · R02 ✓ · R03 ✓ · R04/R05 ✓ ·
  R06 ✓ (Cutouts/Canvas/Inspector headers on one baseline) · R07 ✓ · R08 ✓ · R09 ✓ · R10 ✓ · R11 ✓ · R12 ✓ (Save
  Mesh only) · R13 ✓ · R14 ✓ · R15 ✓ (no literals added; USS uses tokens) · R16 ✓ (blue only on the selected row and
  selected vertex) · R17–R19 ✓ · R20 ✓ (ViewportFrameElement rail) · R21 ⚠ see below · R22 ✓.
  **Not proven, for T13:** (1) pointer gestures — vertex drag/insert, origin drag, reference drag/resize, wheel zoom,
  middle/Alt pan — were exercised through the panel's methods, not real pointer events; the owner's hands are the
  test. (2) No scratch-scene MeshFilter: the owner's open scene (`PaintedGround/New Scene`) may be unsaved, so the
  mesh was verified numerically instead. (3) The tab list renders text-only at this window width, so the drawn
  Cutouts glyph was never seen; R21 also wants a look at the rail mixing the drawn glyph with built-in icons and a
  magnifier for "Frame". (4) No true tab-list before-capture: the window had already recompiled with the tab.
  (5) The project has no reference image to size against; the Reference card is untested beyond compiling.
