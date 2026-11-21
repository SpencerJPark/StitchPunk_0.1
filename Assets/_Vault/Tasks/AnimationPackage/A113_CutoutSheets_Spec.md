# Amendment A113 — Cutout sheets: many pieces from one atlas, sheet flipbooks, rotation, assemblies

> **Status:** 📝 specced 2026-11-21, not built. Owner answered the shape questions the same day (§1) — buildable.
> Ships as **0.67.0** (one minor bump at the end, not per wave).
> **Why:** the owner wants a fast asset pipeline. Today Cutouts makes one mesh from one whole image or flipbook.
> Two kinds of sheet need more: a **pure atlas** (the nature atlas: dozens of still trees, rocks and bushes on one
> texture) and a **character sheet** (still body parts mixed with small flipbooks, e.g. a blinking head drawn as a
> row of frames). The owner wants Cutouts to find every piece by alpha, cut a mesh for each in one action, keep sheet
> flipbooks working as flipbooks, let them set each piece's origin **and rotation** (atlases pack pieces sideways),
> and group character pieces either as a layered paper-doll set or as one merged mesh that gets rigged and baked to VAT.

**Supersedes A111F F-D7's "whole texture only for atlases".** The owner reversed it on 2026-11-21 with this request.
A whole image still works exactly as before; sheets are an extra mode on top.

---

## 1. Decisions

### Owner calls (2026-11-21, do not re-ask)

- **S-D1 — Sheet flipbooks stay on the sheet.** No slicing into a `Texture2DArray`. They play through the existing
  `SpriteFrameMode.AtlasRect` / `_AtlasFrame` path, so a whole sheet is one texture and one material.
- **S-D2 — Flipbooks are auto-suggested, owner confirms.** Alpha finds the islands; islands of matching size on a
  regular row or grid pitch are suggested as one flipbook (one outline colour per group). The owner accepts, splits
  or merges groups by clicking on the canvas.
- **S-D3 — Groups support both outputs.** A **Layered set** is separate meshes, each with its own origin, rotation and
  draw order, output as a Source Prefab + `RigAsset`. These are the paper-doll parts that
  `Documentation~/cutout-characters.md` already describes, animated with transform and sprite tracks. A **Merged**
  assembly welds the still pieces into one skinned mesh that the owner rigs and bakes to VAT.
- **S-D4 — Merged rigging is rigid, one bone per piece.** Each piece gets a bone at its origin, weighted 100%, nested
  as the owner parents them (forearm under upper arm). There is no weight painting. The owner animates the bones in
  Unity's Animation window and bakes through the VAT Bake tab (A78's Source Prefab path). Smooth weights are out of
  scope.
- **S-D5 — Rotation is a free angle that snaps to 90°.** Drag a ring handle around the origin. It snaps to 90° steps;
  hold Ctrl for a free angle. A typed value and ⟲90 / ⟳90 buttons sit in the inspector. The rotation is baked into
  the mesh's vertices, so "up" is correct in game and the transform stays at identity.
- **S-D6 — Assembly happens in Cutouts.** A third canvas mode, **Assemble**, sits beside Object and Edit on the same
  fixed grid. Drag pieces in from the catalog, then move, rotate and reorder them, and drag one origin onto another
  piece to parent it. Scene view is not used.
- **S-D7 — The owner supplies the test art** (§6). Until it arrives, the drive uses a placeholder the orchestrator
  generates, and the captures are labelled as placeholder art.

### My calls (delegated architecture, 2026-11-21 — recorded, not owner-asked)

- **S-D8 — No runtime or shader change.** `AtlasFrameUV` is `uv * rect.xy + rect.zw`
  (`Shaders/Includes/ToolkitFlipbook.hlsl:40`), and a part's rest rect is `ClipSampler.IdentityAtlasRect`
  (`RigTargetBaker.cs:99`). So a sheet-flipbook mesh carries **sheet-space UVs of its first cell**. Identity then
  shows frame 0, and frame *k*'s key is `(1, 1, (cellₖ.min − cell₀.min) / sheetSize)`. All cells in a group must
  share one size, and the island finder enforces it by growing every cell to the group's largest bounds. Still
  pieces carry sheet-space UVs of their own rect. Both kinds sit on the sheet's one material.
- **S-D9 — The data shapes are in §2.** `CutoutAsset` gains a source rect, rotation and sheet cells. The new
  `SheetFlipbookAsset` lives in Authoring because the Clip Editor binds it to an AtlasRect track. The new
  `CutoutAssemblyAsset` is editor-only, like `CutoutAsset`.
- **S-D10 — Batch output goes to `<Folder>/<SheetName>/`.** It holds one `CutoutAsset` + `Mesh` per still, one
  `CutoutAsset` + `Mesh` + `SheetFlipbookAsset` per group, and is re-saved in place (GUIDs kept), as A111 does
  today. The batch origin preset defaults to **bottom centre** (trees and rocks stand on their base). An optional
  **Prefab per still** toggle (off by default) writes a MeshFilter/MeshRenderer prefab per still, for Feature
  Placement.
- **S-D11 — The material is picked, not authored.** An assembly has one `Material` field, which the prefab writers
  assign to every renderer. A112's "never re-add material authoring" holds.
- **S-D12 — Sheet flipbooks inside a Merged assembly** cannot live in the VAT mesh, because there is one atlas rect
  per instance. They are written as separate `FlipbookPlane` nodes under their bone. If T0's probe P1 finds that a
  part cannot follow a `SocketAttachMode.Bone` socket at runtime, the Merged writer **refuses** flipbook pieces and
  names them ("Merged assemblies take still pieces; put the blinking head in a Layered set"). That case is a §7
  escalation, not a quiet runtime change.
- **S-D13 — Draw order becomes depth.** In a Layered set each piece is offset 0.001 world units toward the camera
  per draw-order step. In a Merged mesh, triangles are emitted back to front in draw order and keep the same z
  offset.

---

## 2. Contracts (pinned — workers build to these names exactly)

```csharp
// CutoutAsset.cs — additions (Editor/Cutouts)
public RectInt sourceRectPixels;                 // zero size = whole texture (today's behaviour)
public float rotationDegrees;                    // baked into vertices about originPixels
public List<RectInt> sheetFrameCells = new List<RectInt>(); // empty = still; else cells in play order, all one size
public bool IsSheetFlipbook => sheetFrameCells != null && sheetFrameCells.Count > 1;
public RectInt ResolveSourceRectPixels();        // sourceRectPixels, or the whole texture when its size is zero
// outlinePixels and originPixels stay in the piece's local pixel space: (0,0) = the rect's bottom-left.

// CutoutEnums.cs — additions
public enum CutoutAssemblyOutputKind { LayeredSet, Merged }

// CutoutIslandFinder.cs — new, Editor/Cutouts, pure static
public struct CutoutIsland { public RectInt boundsPixels; public int opaquePixelCount; }
public struct CutoutSheetFlipbookSuggestion { public List<int> islandIndices; public List<RectInt> cells; }
public static class CutoutIslandFinder
{
    public const int DefaultJoinDistancePixels = 3;
    public const int DefaultSizeTolerancePixels = 4;
    public static List<CutoutIsland> FindIslands(bool[] mask, Vector2Int size, int joinDistancePixels, int minimumOpaquePixels);
    public static List<CutoutSheetFlipbookSuggestion> SuggestSheetFlipbooks(IReadOnlyList<CutoutIsland> islands, int sizeTolerancePixels);
    public static bool[] CropMask(bool[] mask, Vector2Int size, RectInt rect);
}
// Islands are read top-to-bottom, left-to-right in sheet space. Cells are in that same order, all resized to the
// group's largest bounds and centred on each island.

// SheetFlipbookAsset.cs — new, Authoring/Assets, namespace DotsAnimationToolkit.Authoring
[Serializable] public sealed class SheetFlipbookFrame { public string name; public RectInt cellPixels; }
[CreateAssetMenu(fileName = "NewSheetFlipbook", menuName = "DOTS Animation Toolkit/Sheet Flipbook", order = 42)]
public sealed class SheetFlipbookAsset : ScriptableObject
{
    public Texture2D sheet;
    public List<SheetFlipbookFrame> frames = new List<SheetFlipbookFrame>();
    public float4 AtlasRectForFrame(int frameIndex); // (1,1, (cellₖ.min − cell₀.min) / sheetSize); identity for 0 or out of range
}

// CutoutAssemblyAsset.cs — new, Editor/Cutouts
[Serializable] public sealed class CutoutAssemblyPiece
{
    public string name; public CutoutAsset cutout;
    public Vector2 positionWorld; public float extraRotationDegrees; public int drawOrder;
    public int parentPieceIndex = -1;           // -1 = child of the root
}
[CreateAssetMenu(fileName = "NewCutoutAssembly", menuName = "DOTS Animation Toolkit/Cutout Assembly", order = 43)]
public sealed class CutoutAssemblyAsset : ScriptableObject
{
    public List<CutoutAssemblyPiece> pieces = new List<CutoutAssemblyPiece>();
    public CutoutAssemblyOutputKind outputKind = CutoutAssemblyOutputKind.LayeredSet;
    public Material material; public string outputFolder = string.Empty;
    public GameObject outputPrefab; public RigAsset outputRig; public Mesh outputMergedMesh;
}

// CutoutMeshBuilder.cs — new overload (the existing ones stay and forward to it with whole-rect, 0°)
public static bool TryBuild(IReadOnlyList<Vector2> outlinePixels, IReadOnlyList<Vector2Int> innerEdges,
    RectInt sourceRectPixels, Vector2Int sheetSizePixels, Vector2 originPixels, float rotationDegrees,
    float pixelsPerUnit, CutoutFacing facing, CutoutNormalMode normalMode, float roundness, Mesh target);
// UVs = (sourceRect.min + outlinePoint) / sheetSize. Positions = rotate(outlinePoint − origin, rotationDegrees) / ppu.
```

Writers (wave 2) each expose one entry point:
`CutoutSheetBatchWriter.TryWriteBatch(Texture2D sheet, IReadOnlyList<CutoutIsland> stills, IReadOnlyList<CutoutSheetFlipbookSuggestion> groups, CutoutSheetBatchSettings settings, out string report)`,
`CutoutAssemblyLayeredWriter.TryWrite(CutoutAssemblyAsset assembly, out string report)`,
`CutoutAssemblyMergedWriter.TryWrite(CutoutAssemblyAsset assembly, out string report)`.
`CutoutSheetBatchSettings` is a struct in the batch writer's file: `outputFolder`, `pixelsPerUnit`,
`originPreset` (normalised Vector2, default `(0.5, 0)`), `fitVertexBudget`, `fitPaddingPixels`, `writePrefabPerStill`.

---

## 3. Read list (line ranges for briefs; grep the member, read ±40 lines)

- `Editor/Cutouts/CutoutAsset.cs` (136): whole file. `ResolveSourceTexture`, `FrameSize`, `FrameCount`.
- `Editor/Cutouts/CutoutAlphaTracer.cs` (411): `ReadSourceMasks` :38, `FitOutline` :113.
- `Editor/Cutouts/CutoutMeshBuilder.cs` (110): whole file.
- `Editor/Cutouts/CutoutMeshWriter.cs` (178): `DefaultMeshPathFor` :27, `TrySave` :44. This is the in-place re-save pattern.
- `Editor/Cutouts/CutoutCanvasElement.cs` (1096): grid constants :11. T0 P3 locates the grid painter.
- `Editor/Cutouts/CutoutShapeManipulator.cs` (604): the Object-mode gesture branch.
- `Editor/Cutouts/CutoutInspectorColumn.cs` (361), `CutoutCatalogColumn.cs` (86), `CutoutsPanel.cs` (965):
  `LoadFlipbookSource` :205, `OnImagesActivated` :185, `OnFitToArtRequested` :706, `OnOriginPresetRequested` :762.
- `Editor/ClipEditor/Panes/FlipbookFramePickerBuilder.cs`: `AtlasTrackHint` :18, `BuildFlipbookField` :21.
- `Editor/ClipEditor/Panes/ClipInspectorPane.cs`: AtlasRect branches :683 and :1436.
- `Authoring/Assets/FlipbookAsset.cs` is the shape `SheetFlipbookAsset` mirrors. `Authoring/Assets/RigAsset.cs` has
  `sourcePrefab` :36, `RigTargetDefinition` :201 (`sourceNodePath`, `kind`), and `EnsureStableIds` :68.
- `Runtime/Components/AnimationToolkitEnums.cs`: `TargetKind` :53. `Runtime/Blobs/SocketRegistryBlob.cs`: `SocketAttachMode` :9.

---

## 4. Tasks

Waves follow `AnimationPackage_Roadmap.md` §3. Each task is one Sonnet `worker` with at most two files. Fixtures are
listed with their task, and each must be proven to fail by reverting its fix, or it is deleted.

- [ ] **T0 — Orchestrator grounding.** Run the baseline gate `python Tools/devloop/devloop.py gate com.dotsanimationtoolkit`
  and record the EditMode/PlayMode totals. Run four probes and record each answer in §7:
  - **P1** (Sonnet verifier): can a non-VAT part follow a `SocketAttachMode.Bone` socket at runtime? Trace the
    consumers of `SocketClipTrackBlob`. The answer decides S-D12.
  - **P2** (Haiku grep): how does the Rigs tab create a `RigTargetDefinition` from a prefab node? Find the method
    that writes `sourceNodePath` and calls `EnsureStableIds`.
  - **P3** (Haiku grep): the method(s) in `CutoutCanvasElement.cs` that paint the grid and the art, with line ranges.
  - **P4** (Sonnet verifier): what the VAT Bake tab needs from a Source Prefab: `SkinnedMeshRenderer`, `Animator`,
    and how `ClipAsset` `sourceClip` reaches it. The answer is the Merged writer's acceptance test.

- **Wave 1 [parallel-safe]: data and pure logic**
  - [ ] T1 — `CutoutAsset.cs` + `CutoutEnums.cs`: §2 additions. `FrameSize` and the existing callers keep working
    with a zero rect.
  - [ ] T2 — `CutoutIslandFinder.cs` (new) + `Tests/EditMode/CutoutIslandFinderTests.cs`. Join is a square dilation
    by `joinDistancePixels` before labelling; islands under `minimumOpaquePixels` are dropped. Fixtures:
    `TwoBlobsWithinJoinDistance_AreOneIsland`, `FourEqualBlobsInARow_SuggestOneFlipbookOfFourCellsInOrder`.
  - [ ] T3 — `CutoutMeshBuilder.cs`: the §2 overload. Fixture in the existing `CutoutMeshBuilderTests.cs`:
    `SheetRectNinetyDegrees_UVsStayInsideRectAndPositionsTurn`.
  - [ ] T4 — `Authoring/Assets/SheetFlipbookAsset.cs` (new) + `CutoutAssemblyAsset.cs` (new). No fixture; it is
    plain data plus one formula, and T9's drive checks the formula.
  - [ ] T5 — `CutoutCanvasElement.cs` + `CutoutCanvasGrid.cs` (new): move the grid painter that P3 located into a
    static `CutoutCanvasGrid` (the `Conformance_G` suffix list allows plain nouns on the allowlist; add it). Behaviour
    must be identical, and the before/after captures must match.
- Gate, then fixtures, then commit `A113-T1..T5:`.

- **Wave 2 [parallel-safe]: writers and the Clip Editor hook**
  - [ ] T6 — `CutoutSheetBatchWriter.cs` (new). For each still island: crop its mask, `FitOutline`, apply the origin
    preset, and write a `CutoutAsset` + mesh. For each group: union the cells' cropped masks, fit once, write a
    `CutoutAsset` (`sheetFrameCells` set) + mesh + `SheetFlipbookAsset` (frames named `<Group>_00…`). Optionally
    write a prefab per still. Re-save in place by name.
  - [ ] T7 — `CutoutAssemblyLayeredWriter.cs` (new). Write a prefab with a root plus one node per piece, nested by
    `parentPieceIndex`. Each node's local position comes from the parent-relative world positions, its local
    rotation is `extraRotationDegrees`, and its z offset follows S-D13. Give each node a MeshFilter and a
    MeshRenderer with the `material`. Write a `RigAsset` with `sourcePrefab` set and one target per node (`Quad` for
    a still, `FlipbookPlane` for a sheet flipbook) through P2's method. Re-save in place.
  - [ ] T8 — `CutoutAssemblyMergedWriter.cs` (new). Build one mesh from the still pieces, transformed into root
    space, with draw-order triangle order, `boneWeights` 1.0 to the piece's bone, and `bindposes` from the bone
    hierarchy. Build a prefab with the bone Transforms (the same nesting as T7), a `SkinnedMeshRenderer` and
    whatever else P4 says the VAT bake needs. Sheet-flipbook pieces follow S-D12. Write a `RigAsset` whose skinned
    target is `VatMesh`. Fixture: `MergedMesh_EveryVertexWeightsFullyToItsPiecesBone`.
  - [ ] T9 — `FlipbookFramePickerBuilder.cs` + `ClipInspectorPane.cs` (the AtlasRect branches only). An AtlasRect
    track accepts a `SheetFlipbookAsset` in the existing Flipbook field. The frame popup lists its frame names and
    writes `key.atlasRect = AtlasRectForFrame(index)`. `AtlasTrackHint` is replaced by the popup when one is bound.
- Gate, then fixtures, then commit `A113-T6..T9:`.

- **Wave 3 [parallel-safe, the §2 contract pinned, UI to `EditorStyleGuide.md` R01–R22]**
  - [ ] T10 — `CutoutSheetOverlay.cs` (new). A VisualElement layered over the canvas that draws island outlines,
    with one colour per suggested group, grey for stills and an accent for the selection. Click selects, Shift-click
    adds, and a drag draws a rubber band. It raises `SelectionChanged(IReadOnlyList<int>)`. Symbols, not words (the
    visual-language rule).
  - [ ] T11 — `CutoutAssemblyCanvasElement.cs` (new). This canvas reuses `CutoutCanvasGrid`. It draws each piece
    textured from its sheet rect and rotated, with origin dots and parent→child bone lines. Dragging a piece moves
    it. Dragging its ring rotates it, snapping to 90° unless Ctrl is held. Dragging an origin dot onto another piece
    parents it, and dropping on empty grid unparents it. `[` and `]` change draw order, Delete removes a piece, and
    there is wheel zoom and middle/Alt pan as in A111F.
  - [ ] T12 — `CutoutShapeManipulator.cs` + `CutoutCanvasElement.cs`. The Object-mode rotation ring around the
    origin follows S-D5, and the art draws rotated so what you see is what the mesh bakes.
  - [ ] T13 — `CutoutInspectorColumn.cs`. Add a Rotation row (value, ⟲90, ⟳90). Add a **Sheet** card, shown when
    the source is an image: Find pieces, then threshold, join distance and minimum size; a selected count; Group as
    flipbook / Split / Merge islands; the batch origin preset; Prefab per still; and the big primary action
    **Cut N pieces**.
  - [ ] T14 — `CutoutAssemblyInspector.cs` (new). A piece list (name, draw order, parent popup) beside a detail panel
    for the selected piece. It has a Layered | Merged segmented control, Material, Folder, and the big primary
    action **Build prefab**. A Merged build with flipbook pieces shows S-D12's message before it writes anything.
- Gate, then commit `A113-T10..T14:`.

- **Wave 4 (sequential, one worker): wiring**
  - [ ] T15 — `CutoutsPanel.cs` + `CutoutCatalogColumn.cs`. The image source gets the sheet overlay, the Sheet card
    and the batch call. The header mode control becomes Object | Edit | **Assemble**: Assemble swaps in the assembly
    canvas and inspector, and Tab still toggles Object/Edit. The catalog gets an **Assemblies** section with New,
    rename and delete-with-confirmation; dragging a cutout row onto the assembly canvas adds a piece. Status-line
    messages cover every writer's report.
- Gate, then commit `A113-T15:`.

- [ ] **T16 — Orchestrator: verify and close.** Run the full EditMode then PlayMode suites; the totals must not
  drop. Then drive the feature on the §6 art:
  1. Nature atlas: Find pieces, Cut N, reload from disk, and confirm every mesh's UVs sit inside its rect.
  2. Character sheet: accept a suggested flipbook, rotate one sideways piece 90°, assemble 4 or more pieces with
     parenting.
  3. Layered build: the rig opens in the Rigs tab with the right target kinds; in the Clip Editor an AtlasRect key
     picked by name changes frame in the preview.
  4. Merged build: the VAT Bake tab accepts the prefab and bakes one clip the orchestrator authors on two bones.

  Capture every affected surface (Cutouts sheet and assemble modes, Object-mode rotation ring, Clip Editor atlas
  key, Rigs, VAT Bake). Repaint about 20 times before `GrabPixels`, per the recipe in
  `Memories/Code/AnimationToolkit.md`. Judge the captures against the owner's art and audit R01–R22. Then do
  `CHANGELOG.md` 0.67.0, `Documentation~/cutouts.md` (Sheets + Assemble sections) plus a cross-link from
  `cutout-characters.md`, the `HANDOFF.md` §4 paragraph, and a `Memories/Code/AnimationToolkit.md` entry if a trap
  was found, and tick the roadmap.
- [ ] **T17 — ⏸ Owner checkpoint: the gestures by hand on real art.** Covers island click/rubber band, group
  split/merge, the rotation ring with and without Ctrl, parenting by drag, and draw-order keys. Closes as accepted if
  unseen (standing rule), unless something is game-breaking.

---

## 5. Run model: observer orchestrator, cheap hands

- **Opus orchestrator = the observer.** It plans waves, writes briefs, reads diffs and captures, and judges. It
  edits only docs, the changelog and §7. It alone touches `mcp__UnityMCP__*`. It never re-derives a worker's code
  in its own context. When a diff needs checking it spawns a `verifier`.
- **Sonnet `worker`** = one task, at most two files. The brief pastes the task text, the task's §2 block and §3 line
  ranges, and the hard rules (no `var`, no single-letter names, explicit types, one `<summary>` per file of 3 lines
  or fewer, no spec citations in shipped code). It ends with: "run `python Tools/devloop/devloop.py gate
  com.dotsanimationtoolkit` before writing your report; at turn 30 stop editing and write your report; report ≤ 30
  lines; never call any `mcp__UnityMCP__*` tool."
- **Sonnet `verifier`** = P1, P4, and one contract check after each of waves 2 and 3: do the names match §2
  exactly?
- **Haiku** = locate-only greps (P2, P3, "where is X called"). It never edits and never reviews.
- A capped agent is never resumed: read its diff, then spawn a fresh worker for the rest. Watch the ledger line
  after each agent. HIGH or OVER means the next brief in that family is split further. A 429 kills all workers;
  check the disk before respawning.
- **Wave widths:** wave 1 = 5, wave 2 = 4, wave 3 = 5 parallel workers, wave 4 = 1. One gate per wave, not per
  worker.

---

## 6. Test art (owner-supplied, S-D7)

- `Assets/Art/CutoutSheets/NatureAtlas.png`: still pieces only, transparent background.
- `Assets/Art/CutoutSheets/CharacterSheet.png`: still body parts plus at least one row of equal-size flipbook
  frames, and ideally one piece packed sideways.

Import both as Sprite (Single) or Default with **Read/Write on**, because `ReadSourceMasks` reads the pixels.
If either file is missing at T16, generate a placeholder in the scratchpad (coloured blobs plus a 4-frame row), drive
on that, and flag it in the checkpoint message.

---

## 7. Log

- 2026-11-21: specced. Grounding: runtime `FlipbookAtlas` / `AtlasRect` mode exists with no editor frame picker
  (`AtlasTrackHint = "flipbooks bind Slice tracks"`). `AtlasFrameUV = uv*xy + zw` and the identity rest rect make
  S-D8 work with no runtime change. VAT bake poses a rig's Source Prefab (A78), so Merged must emit a skinned
  prefab. `SocketAttachMode.Bone` exists, and whether a part can ride it is P1.
- 2026-11-21 (spec-lead, worktree `spec/a113`): T0 offline baseline gate PASS (needs a `Library` junction to the
  stage's Library plus asmdef mtimes matched to the stage, else `GATE UNAVAILABLE`/`GATE STALE`). Probes:
  - **P1 = NO → §7 escalation.** `SocketResolveSystem`'s `ResolveSocketsJob` carries `[WithNone(typeof(RigPartBinding))]`
    and reads bake-time `SocketClipTrackBlob` samples, so a rig part cannot follow a Bone socket. Per S-D12 the Merged
    writer and the assembly inspector **refuse** sheet-flipbook pieces by name. Letting a blinking head ride a VAT
    bone would need a runtime change (owner call).
  - **P2:** `ClipComponentModel.PromoteToRigTarget` needs a ClipAsset; the writers set `sourceNodePath` via
    `PrefabAuthoringBridge.GetHierarchyPath(node, root)` (root excluded, `/`-joined) and call `EnsureStableIds`.
  - **P3:** grid painter was `CutoutCanvasElement.OnGenerateGrid` :668–730 → now `CutoutCanvasGrid.Paint`.
  - **P4:** the VAT bake needs a `SkinnedMeshRenderer` with bones (matched by a `VatMesh` target path or the only
    one), no Animator; clips are sampled with `AnimationMode.SampleAnimationClip` on the prefab root.
  - Rotation convention: CCW-positive degrees about the origin (+Y → −X at 90°). Assembly `positionWorld` and
    `extraRotationDegrees` are absolute; writers parent with `worldPositionStays`. Moving a parent on the assembly
    canvas does not drag its children (T17 may want that).
  - R12: Sheet card's Find pieces is secondary; Cut N is the one primary and hides Save mesh while pieces are found.
