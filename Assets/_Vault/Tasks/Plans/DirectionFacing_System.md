# Direction & Facing — Design Spec (toolkit adoption)

> **Status:** 📝 spec drafted 2026-08-29 — core decisions stamped in the same session (owner Q&A); the remaining **← DECISION** markers are sub-choices, resolvable at build time.
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
- [x] **Channel packing is re-purposed to intra-slice *state variants*** — e.g. hair's *turns* are different slices, but hair-resized-for-a-hat is a second channel pair in the same slice: RG = (shape-mask × hair color, alpha), BA = the alternate variant's (mask, alpha). Follow-up spec (§6), not built here.
- [x] **Roster default `AnimationDirections.Six`** — diagonals + head-on + head-away, no true profile. 4 authored clips per locomotion state (SE, NE, S, N); W/E fold to the three-quarters, SW/NW are mirrors.
- [x] **Mirror only ever via `PartFacing.mirrorX`.** The old spec's `scale.x` flip is now a silent-failure trap — `TransformApplySystem` stomps part transforms every frame. Also: **never** play a `MirrorClipUtility`-authored mirrored clip with runtime `mirrorX` set — double reflection fails silently ("wrong-footed", not broken).
- [x] **`DirectionUtils` + `DirectionUtilsTests` are deleted** — fully superseded by `FacingResolver`. The only game-side math that remains is the world→facing-space mapping (§5).

## 3. Entry points

No request component. Facing is **derived state**, same as design apply: movement (and optionally aim, §5) → quantize → store → consumers read.

## 4. Data model

- **`UnitFacing : IComponentData { Direction current; }`** — new, on unit roots. Written only by `UnitFacingSystem`; read by assignment + the `PartFacing` push.
- **`UnitSO.animationDirections : AnimationDirections = Six`** → baked into `UnitDataBlob`. Per the toolkit's own doc this is a property of the *content* (a citizen and a boss can share a rig and differ), which is exactly what `UnitSO` is.
- **`DirectionalClipSet`** (serializable struct): `{ ClipAsset south; ClipAsset southEast; ClipAsset northEast; ClipAsset north; }` — east-side members only, mirrors come free. Replaces every locomotion-facing `ClipAsset` field: `UnitSO.idleAnimation` / `movingAnimation`, `StanceAnimationMapping`'s pair, and `ActionAnimationMapping.animation`. Baked as 4 `ulong`s. **A missing member falls back to `southEast`** (the canonical front three-quarter) with a bake-time warning — that's the degeneracy that lets today's single-clip art keep working until the direction variants are drawn. ← DECISION: confirm southEast as the fallback member (alternative: fall back to whatever single member is authored).
- **`PartDefinitionSO` per-direction view offsets:** `int viewOffsetSouthEast / NorthEast / South / North` (default 0) → baked into `PartLibraryBlob` per part def. 0 everywhere = the part never changes art with facing (nose); non-zero = alt-view slices exist inside the variant block (ear from behind). **This is the part-authoring convention the audit warned about — settled now, before more part SOs or atlases are authored:** a part's variant block reserves its turn views as consecutive slices, offsets recorded here.
- **`PartFacing` baked (0, false) on every quad part** by `CharacterRigAuthoring`/`BodyPartAuthoring` — it's opt-in in the toolkit (a part without it never mirrors and never offsets), so the game bakes it wherever turning should apply, i.e. all body-part quads.

## 5. Systems

- **`UnitFacingSystem`** (new — `AnimationSystemGroup/AnimationAssignmentSystemGroup`, `[UpdateBefore(typeof(UnitAnimationAssignmentSystem))]`; facing must resolve before clip selection):
  1. Map world movement to the toolkit's facing space (+x east, +y away-from-camera). The world is 2.5D with movement on XZ, so this is `velocity.xz` — ← DECISION: world-fixed axes (recommended; revisit only if the Cinemachine rig ever yaws) vs camera-relative.
  2. `FacingResolver.FromMovement(screenXY, blob.animationDirections, facing.current)` → write `UnitFacing` on change.
  3. ← DECISION: aim override — while `unitAction.current` is an attack and a combat target exists, face the target's direction instead of velocity (recommended: yes, phase 3; talking-partner facing can reuse the same override seam later).
  4. On facing change: `ResolveClipFacing` → for each `BodyPart` entry with `PartFacing`, write `{ viewOffset: blob lookup by clipFacing, mirrorX }` via `ComponentLookup` (with `HasComponent` check, per the toolkit's own warning — never as a job query parameter).
- **`UnitAnimationAssignmentSystem`** (edit): `GetBaseAnimation`/`GetAnimationForAction` take the resolved east-side `clipFacing` and index the `DirectionalClipSet` blob. No re-issue logic needed — `PlaybackQuery.IsPlaying` compares `ClipId`, so a facing change makes the current clip "not playing" and the existing play-on-change path swaps it.
- **`PlayerAttackSystem` / behavior action clips:** directional for free — both resolve through `actionAnimations`. Explicit `PlayAnimation` behavior commands (a single authored `ClipAsset`) stay non-directional in v1; a behavior that needs facing uses an action mapping instead.
- **New tests (EditMode):** pin the world→facing-space mapping and the `DirectionalClipSet` fallback resolution. Do **not** re-test `FacingResolver` — the toolkit's `FacingResolverTests` already pin snap/mirror/quantize.

## 6. Follow-up (not built here): channel-pair state variants

What survives of `DirectionalTexturePacking_System.md`, re-scoped per the owner's call: **channels select same-slice state variants** (hatted hair, squashed/resized poses), not directions. Two variants per slice — RG pair and BA pair, each `(grayscale shape mask, alpha)`, tinted by the part's color at shade time — selected by a game-owned MaterialProperty the toolkit never touches. The packer tool (`PainterlyMaskPacker` pattern) and the mask-times-color recolor thinking carry over from the deleted spec (git history has the full draft, including the `PaletteColorSO` ramp option). Draft that spec when the first real case (hat) is on the art bench — it needs zero decisions from this plan beyond "direction is on the slice axis, so channels are yours".

## 7. Proposed file manifest

**New:** `Components/Units/FacingComponents.cs` (`UnitFacing`) · `Systems/AnimationSystemGroup/AnimationAssignmentSystemGroup/UnitFacingSystem.cs` · `Tests/FacingSpaceTests.cs`
**Edited:** `Data/SOs/UnitSO.cs` (+`animationDirections`, `DirectionalClipSet` fields) · `Data/Structs/UnitBlob.cs` · `PostBakingSystemGroup/UnitLibraryBakingSystem.cs` (bake + fallback warning) · `Data/SOs/PartDefinitionSO.cs` (+4 view offsets) · `PartLibraryBakingSystem.cs` · `Authoring/BodyPartAuthoring.cs` (or `CharacterRigAuthoring`) (+`PartFacing` bake) · `UnitAnimationAssignmentSystem.cs`
**Deleted:** `Utils/DirectionUtils.cs` · `Tests/DirectionUtilsTests.cs`
**Assets:** none required to compile (fallback covers single-clip units); to *see* it: one 4-member walk `DirectionalClipSet` + one part with authored alt-view slices (owner, dovetails with migration phase 2).

## 8. Build phases

1. **Data + vocabulary.** `UnitFacing`, SO/blob fields, `PartFacing` bake, `DirectionUtils` deletion, bake-time fallback warning. Compiles with zero directional art.
2. **`UnitFacingSystem`** — movement quantize + `PartFacing` push; EditMode tests. Visible result with today's art: units mirror-flip left/right correctly (Six folds to mirrors when only SE members exist).
3. **Directional clip selection** in assignment + the aim-override decision. With 4-member sets authored, walking a circle cycles SE/NE/S/N + mirrors.
4. **Owner art proof** (with migration phase 2): one unit's walk/idle as full Six sets, one part with real alt views — then retire to `Verification/`.

## 9. Verification (→ `verify-directionfacing.md` at retire time)

- Walk a unit in a circle in `DOTSTestScene`: facing steps through all six members, west-side facings are exact mirrors of their east pair, no flicker at boundaries (the resolver's fixed-answer guarantees).
- A unit that stops keeps its last facing (no snap to default).
- A part with authored alt views swaps art on turn; a part with offsets 0 only mirrors; design variant change (zombify) preserves facing (`restSliceIndex + viewOffset` compose, neither clobbers).
- A unit with only `southEast` members authored behaves exactly as before this plan (fallback path) and the bake warning names the missing members once.
- Attack (if aim override lands): unit faces its target while swinging even when strafing.

## Open decisions (collected)

- [x] Direction art axis: **slices/viewOffset** (channel packing → state variants, §6) — stamped 2026-08-29.
- [x] Roster default: **`AnimationDirections.Six`** — stamped 2026-08-29.
- [x] Mirror route: **`PartFacing.mirrorX` only** — stamped 2026-08-29.
- [ ] §4 — `DirectionalClipSet` fallback member: `southEast` (recommended) vs first-authored.
- [ ] §5 — facing space: world-fixed `velocity.xz` (recommended) vs camera-relative.
- [ ] §5 — aim override toward combat target while attacking: yes in phase 3 (recommended) vs movement-only v1.
