# Direction & Facing — Design Spec (toolkit adoption)

> **Status:** 🔨 phases 1–4 built 2026-08-29 (data model, `UnitFacingSystem`, directional clip selection, Direction Set Editor window) — compiles clean, new EditMode tests + touched-fixture suites pass. **Phase 5 (owner art proof) is outstanding** — needs a real Six-direction walk/idle set and a part with authored alt-view slices, through the tool, then a compile+rebake+play pass before this retires to `Verification/`.
> **Raw source:** [`../Claude/Systems_Gap_Audit_2026-08.md`](../Claude/Systems_Gap_Audit_2026-08.md) area 3 — the re-audit of the two pre-toolkit Direction specs.
> **Supersedes (both deleted this session, git keeps them):** `Direction_System.md` — its A/B/C fork is answered by toolkit machinery that now exists; `DirectionalTexturePacking_System.md` — its *direction* half is dead (slices won, §2), its *channel-packing + recolor* half survives re-purposed to a different axis (§6) and gets its own spec later.
> **Sequencing:** the data-model half of this plan gates the migration's phase 2 (rig/clip authoring) — Spencer should author clips and part atlases already knowing the Six-direction conventions below.

---

**Skills Needed:**
- `dots-system-scaffold` — `UnitFacingSystem` (§5)
- `dots-authoring-baker` — `PartFacing` baking on quad parts, `UnitSO`/`PartDefinitionSO` field additions (§4)
- `dots-test` — facing-space mapping + directional clip fallback (EditMode; the toolkit already pins `FacingResolver` itself in its own suite — do not re-test it)

---

## 1. What the re-audit found

The toolkit already owns most of what both old specs planned, split exactly along its stated line — *"package-owned data, not selection logic"*:

- **Vocabulary:** `Direction` (8-way, **south faces the camera**) + `AnimationDirections` (per-**actor** count: One/Two/Four/Six/Eight). Nothing in the toolkit stores or authors these — storage is host-side by design.
- **Quantizer:** `FacingResolver.FromMovement(in float2 movementXY, available, currentFacing)` — sign-based (never nearest-angle), holds facing when nearly stationary. Replaces `DirectionUtils.Get4/6/8Direction` outright.
- **Clip economics:** every set ≥ Two is mirror-closed; `FacingResolver.ResolveClipFacing` folds any facing to an **east-side authored clip + mirrorX flag**. Authoring cost per state is 1/1/2/**4**/5 clips for the five counts.
- **Mirror:** `PartFacing.mirrorX` — a post-composition pose reflection per part.
- **Alt views:** `PartFacing.viewOffset` — slice steps inside the part's variant block, composing as `restSliceIndex + viewOffset + clip key` (design system, facing, animation — three terms, three owners). This *is* the "directionCount seam" the old spec wanted to reserve; it's built.
- **Inheritance for free:** facing is a component applied after composition, so only locomotion needs direction clip variants — blinks/expressions/action overlays never multiply per direction.

What does **not** exist anywhere: a system that decides facing, stores it, picks directional clips, and writes `PartFacing`. The legacy `UnitFaceDirectionSystem` was deleted as a corpse in the migration. That host half is this plan.

## 2. Decisions stamped 2026-08-29 (owner Q&A — do not reopen)

- [x] **Direction art = texture-array slices** via `PartFacing.viewOffset`, toolkit-native. RGBA channel packing is **not** the direction mechanism.
- [x] **Channel packing is re-purposed to intra-slice *state variants*** — e.g. hair's *turns* are different slices, but hair-resized-for-a-hat is a second channel pair in the same slice: RG = (shape-mask × hair color, alpha), BA = the alternate variant's (mask, alpha). Follow-up spec (§6b), not built here.
- [x] **Roster default `AnimationDirections.Six`** — diagonals + head-on + head-away, no true profile. 4 authored clips per locomotion state (SE, NE, S, N); W/E fold to the three-quarters, SW/NW are mirrors.
- [x] **Mirror only ever via `PartFacing.mirrorX`.** The old spec's `scale.x` flip is now a silent-failure trap — `TransformApplySystem` stomps part transforms every frame. Also: **never** play a `MirrorClipUtility`-authored mirrored clip with runtime `mirrorX` set — double reflection fails silently ("wrong-footed", not broken). Owner-confirmed intent: most side art is mirrored (SW is SE reversed) — that is exactly the mirror-closure model, so only east-side clips are ever authored.
- [x] **`DirectionUtils` + `DirectionUtilsTests` are deleted** — fully superseded by `FacingResolver`. The only game-side math that remains is the world→facing-space mapping (§5).
- [x] **Per-animation coverage is derived from authored slots, not declared** — a logical animation resolves to different clips by the actor's facing, and each set carries only the directions it actually has (all / left-right / one). No fallback-member decision exists; the resolver snaps into the set's own effective count (§4).
- [x] **Facing space: world-fixed `velocity.xz`** — revisit only if the Cinemachine rig ever yaws.
- [x] **Aim override: yes** — while attacking with a live `CombatTarget`, facing quantizes the to-target direction instead of velocity (phase 3; the same seam serves talking-partner facing later).
- [x] **A visual Direction Set Editor is part of this plan** (§6a) and lands before the mass clip-authoring pass — assigning per-direction clips blind in the inspector is the error-prone path this tool removes.

## 3. Entry points

No request component. Facing is **derived state**, same as design apply: movement (and optionally aim, §5) → quantize → store → consumers read.

## 4. Data model

- **`UnitFacing : IComponentData { Direction current; }`** — new, on unit roots. Written only by `UnitFacingSystem`; read by assignment + the `PartFacing` push.
- **`UnitSO.animationDirections : AnimationDirections = Six`** → baked into `UnitDataBlob`. This is the *turn granularity* — how finely the actor's facing quantizes — and per the toolkit's own doc it is a property of the content (a citizen and a boss can share a rig and differ). Individual animations may cover fewer directions than the actor turns through; the set folds the difference (next bullet).
- **`DirectionSetSO`** (new asset — the "logical animation"): five east-side `ClipAsset` slots, `{ southEast; northEast; south; north; east; }`, mirrors always free. Its **effective `AnimationDirections` is derived at bake from which slots are filled**, mapping onto the toolkit's mirror-closed sets: `southEast` only → Two (left/right via mirror — the common case for existing art); `+northEast` → Four; `+south +north` → Six; all five → Eight; **`south` only → One** (plays head-on, never turns, never mirrors — sit-at-desk-style animations). Any other fill pattern bake-warns and rounds down to the largest valid set. Every clip-mapping field that should turn re-types from `ClipAsset` to `DirectionSetSO`: `UnitSO.idleAnimation` / `movingAnimation`, `StanceAnimationMapping`'s pair, `ActionAnimationMapping.animation`. Being an asset (not an inline struct) is what makes sets shareable across units on the same rig and gives the editor tool (§6a) something to open. Baked per set: 5 `ulong`s + one effective-count byte into the owning blob. Behavior/narrative `PlayAnimation` `ClipAsset` fields stay single-clip in v1 (a behavior that needs facing routes through an action mapping); upgrading them to `DirectionSetSO` later is a field re-type, not a redesign.
- **`PartDefinitionSO` per-direction view offsets:** `int viewOffsetSouthEast / NorthEast / South / North` (default 0) → baked into `PartLibraryBlob` per part def. 0 everywhere = the part never changes art with facing (nose); non-zero = alt-view slices exist inside the variant block (ear from behind). **This is the part-authoring convention the audit warned about — settled now, before more part SOs or atlases are authored:** a part's variant block reserves its turn views as consecutive slices, offsets recorded here.
- **`PartFacing` baked (0, false) on every quad part** by `CharacterRigAuthoring`/`BodyPartAuthoring` — it's opt-in in the toolkit (a part without it never mirrors and never offsets), so the game bakes it wherever turning should apply, i.e. all body-part quads.

## 5. Systems

- **`UnitFacingSystem`** (new — `AnimationSystemGroup/AnimationAssignmentSystemGroup`, `[UpdateBefore(typeof(UnitAnimationAssignmentSystem))]`; facing must resolve before clip selection):
  1. Map world movement to the toolkit's facing space (+x east, +y away-from-camera): world-fixed `velocity.xz` (stamped §2).
  2. **Aim override** (stamped §2): while `unitAction.current` is an attack and a live `CombatTarget` exists, quantize the to-target direction instead of velocity.
  3. `FacingResolver.FromMovement(screenXY, blob.animationDirections, facing.current)` → write `UnitFacing` on change.
  4. On facing change: `ResolveClipFacing` → for each `BodyPart` entry with `PartFacing`, write `{ viewOffset: blob lookup by clipFacing, mirrorX }` via `ComponentLookup` (with `HasComponent` check, per the toolkit's own warning — never as a job query parameter).
- **`UnitAnimationAssignmentSystem`** (edit): `GetBaseAnimation`/`GetAnimationForAction` resolve through the set blob in two steps: `FacingResolver.Snap(facing.current, set.effectiveCount)` folds the actor's facing into what this set actually has, then `ToAuthoredSide` picks the east-side slot + mirror. A Two-coverage walk on a Six-turning actor therefore just mirrors left/right — the "art not drawn yet" degeneracy needs no special case. No re-issue logic needed — `PlaybackQuery.IsPlaying` compares `ClipId`, so a facing change makes the current clip "not playing" and the existing play-on-change path swaps it. **One subtlety:** the set-level mirror and the part-level mirror must agree — both derive from the same `UnitFacing`, so they cannot drift, but the clip's `mirrorX` is served through `PartFacing` (there is no per-play mirror flag on a command), which is why facing writes `PartFacing` even for actors whose parts have no alt views.
- **`PlayerAttackSystem` / behavior action clips:** directional for free — both resolve through `actionAnimations`.
- **New tests (EditMode):** pin the world→facing-space mapping and the fill-pattern → effective-count derivation. Do **not** re-test `FacingResolver` — the toolkit's `FacingResolverTests` already pin snap/mirror/quantize.

## 6a. Direction Set Editor — the authoring tool (owner-requested, part of this plan)

Assigning five clip slots blind in a default inspector is exactly how a set ends up wrong-footed; the owner wants to *see* the character per direction while wiring sets. Scope for v1 of the tool:

- **Opens a `DirectionSetSO`** and shows one preview pane per direction of the derived coverage (Two shows SE+SW, Six shows all six), with the west-side panes rendered as live mirrors of their east clip — so "SW is SE reversed" is visible before anything runs in Play mode. The toolkit Clip Editor's preview-stage infrastructure (`PreviewSceneStage`, preview scenery providers) is the rendering substrate to lean on — do not build a second preview pipeline.
- **Slot assignment by drag or picker** per direction; the coverage readout updates live (fill `northEast` → "Four"), and invalid fill patterns show the same warning the bake will raise.
- **Playback scrub** shared across panes (one time slider, all directions in sync) — this is the check that catches mismatched clip lengths/foot phase between direction variants, the classic directional-art bug.
- **Not in the tool:** clip *content* editing (that is the Clip Editor's job — the tool links through to it), `PartFacing` view offsets (part-level, authored on `PartDefinitionSO`), and rig editing.
- [x] **Tool form: standalone `EditorWindow`**, opened from within the toolkit's Clip Editor (not a `DirectionSetSO` custom inspector) — owner wants direction-based clip authoring reachable from the same place as clip content authoring. Stamped 2026-08-29 (build-time).
  **→ Superseded 2026-08-29 by [`DirectionSetsPanel_System.md`](DirectionSetsPanel_System.md):** the tool becomes an in-Clip-Editor toggle pane ("2D Direction Sets"), the asset moves into the toolkit as `DirectionSetAsset`, and the six-pane grid is replaced by one viewer + a direction slider + a clip queue. Phase 5 of this plan runs through that panel.

Sequencing: the tool needs the `DirectionSetSO` asset type (phase 1) and nothing from the runtime phases; it must exist **before the mass authoring pass** (phase 5), so it lands as phase 4, and runtime phases 2–3 proceed in parallel with hand-authored test sets.

## 6b. Follow-up (not built here): channel-pair state variants

What survives of `DirectionalTexturePacking_System.md`, re-scoped per the owner's call: **channels select same-slice state variants** (hatted hair, squashed/resized poses), not directions. Two variants per slice — RG pair and BA pair, each `(grayscale shape mask, alpha)`, tinted by the part's color at shade time — selected by a game-owned MaterialProperty the toolkit never touches. The packer tool (`PainterlyMaskPacker` pattern) and the mask-times-color recolor thinking carry over from the deleted spec (git history has the full draft, including the `PaletteColorSO` ramp option). Draft that spec when the first real case (hat) is on the art bench — it needs zero decisions from this plan beyond "direction is on the slice axis, so channels are yours".

## 7. Proposed file manifest

**New:** `Components/Units/FacingComponents.cs` (`UnitFacing`) · `Systems/AnimationSystemGroup/AnimationAssignmentSystemGroup/UnitFacingSystem.cs` · `Data/SOs/DirectionSetSO.cs` (+ its blob struct beside `UnitBlob`) · `Editor/DirectionSetEditor/` (§6a) · `Tests/FacingSpaceTests.cs` (+ fill-pattern → effective-count fixture)
**Edited:** `Data/SOs/UnitSO.cs` (+`animationDirections`; clip fields re-typed to `DirectionSetSO`) · `Data/Structs/UnitBlob.cs` · `PostBakingSystemGroup/UnitLibraryBakingSystem.cs` (bake sets + fill-pattern warning) · `Data/SOs/PartDefinitionSO.cs` (+4 view offsets) · `PartLibraryBakingSystem.cs` · `Authoring/BodyPartAuthoring.cs` (or `CharacterRigAuthoring`) (+`PartFacing` bake) · `UnitAnimationAssignmentSystem.cs`
**Deleted:** `Utils/DirectionUtils.cs` · `Tests/DirectionUtilsTests.cs`
**Assets:** one `DirectionSetSO` per logical animation currently pointed at by `UnitSO` fields (SE-slot-only wrapping today's clips — behaves exactly as before); to *see* real turning: one full Six walk set + one part with authored alt-view slices (owner, dovetails with migration phase 2).

## 8. Build phases

1. ✅ **Data + vocabulary.** `UnitFacing`, `DirectionSetSO` + blob + effective-count derivation, SO field re-types, `PartFacing` bake, `DirectionUtils` deletion, fill-pattern bake warning. Existing clips wrapped in SE-only sets — compiles and plays identically with zero new art.
2. ✅ **`UnitFacingSystem`** — movement quantize + aim override + `PartFacing` push; EditMode tests. Visible result with today's art: units mirror-flip left/right correctly (every set folds to Two).
3. ✅ **Directional clip selection** in assignment (per-set snap + east-side pick), plus the same facing-aware pick threaded through `AIUtils.GetAnimationByAction`'s two other callers (`PlayerAttackSystem`, the `PlayActionAnimation` behavior command) so they inherit it "for free" per §5. With a hand-authored multi-member test set, walking a circle cycles the members + mirrors.
4. ✅ **Direction Set Editor** (§6a) — standalone `EditorWindow` (`Assets/_Scripts/Editor/DirectionSetEditor/DirectionSetEditorWindow.cs`): preview panes (one `ClipPreviewController` instance per direction, west-side panes mirrored via a UI Toolkit `scale(-1,1)` on the rendered `Image`), live coverage readout sharing `DirectionSetSO.TryGetEffectiveDirections` with the bake warning, shared scrub slider, "Open in Clip Editor" per authored slot. Launched from a "Direction Sets" toolbar button in the Clip Editor, placed immediately before VAT Bake — wired through a new `ClipEditorWindow.OnDirectionSetsButtonClicked` static event so the toolkit package takes no dependency on this game (`PackagingConformanceTests` still green).
5. ⏳ **Owner art proof** (with migration phase 2, through the tool): one unit's walk/idle as full Six sets, one part with real alt views — then retire to `Verification/`.

## 9. Verification (→ `verify-directionfacing.md` at retire time)

- Walk a unit in a circle in `DOTSTestScene`: facing steps through all six members, west-side facings are exact mirrors of their east pair, no flicker at boundaries (the resolver's fixed-answer guarantees).
- A unit that stops keeps its last facing (no snap to default).
- A part with authored alt views swaps art on turn; a part with offsets 0 only mirrors; design variant change (zombify) preserves facing (`restSliceIndex + viewOffset` compose, neither clobbers).
- A unit whose sets have only the `southEast` slot filled behaves exactly as before this plan (folds to Two), and an invalid fill pattern bake-warns once, naming the set asset.
- A `south`-only set (One) never turns and never mirrors while playing.
- Aim override: unit faces its target while swinging even when strafing.
- Tool (§6a): open a set, fill `northEast` → coverage readout flips to Four and the NW pane appears as a live mirror; scrubbing moves all panes in sync.

## Open decisions (collected)

- [x] Direction art axis: **slices/viewOffset** (channel packing → state variants, §6b) — stamped 2026-08-29.
- [x] Roster turn granularity default: **`AnimationDirections.Six`** — stamped 2026-08-29.
- [x] Mirror route: **`PartFacing.mirrorX` only** — stamped 2026-08-29.
- [x] Per-animation coverage: **derived from filled `DirectionSetSO` slots** (no fallback member, no per-set declaration) — stamped 2026-08-29.
- [x] Facing space: **world-fixed `velocity.xz`** — stamped 2026-08-29.
- [x] Aim override toward combat target while attacking: **yes, phase 2/3** — stamped 2026-08-29.
- [x] Direction Set Editor is in-plan, phase 4, before the mass authoring pass — stamped 2026-08-29.
- [x] §6a — tool form: **standalone `EditorWindow`, launched from the Clip Editor** — stamped 2026-08-29 (build-time).
