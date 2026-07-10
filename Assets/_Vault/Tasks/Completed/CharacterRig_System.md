# CharacterRig System — Design Spec

> **Status:** 🔨 built (2026-07-01) · all code landed via `execute-plan`; awaits one compile + rebake + play pass and the Editor-side asset/prefab/scene migration (see `verify-characterrig.md`). Locked decisions: naming `BodyPart` / `BodyPartInfo` / `CharacterRigAuthoring`; full `PartDefId` set; eyes follow `SkinColor`; ragdoll overrides kept on authoring; Animation Editor compile-fix only; `AnimationTargetNoIndexAuthoring` folded into `BodyPartAuthoring`.
> **Prior status:** ✅ spec ready · edit the inline **← DECISION** markers, then hand back to start the build.
> **Raw source:** [`../futureneedsplan.md`](../futureneedsplan.md) → "add random unit designs" / character permanence; supersedes the runtime-layout half of [UnitDesign_System](../Completed/UnitDesign_System.md) and unblocks the ⬜ *Human → Zombie Conversion* row.

---

**Skills Needed** (project skills in `.claude/skills/` — see [Skills index](../../Memories/Code/Skills.md)):
- `dots-blob-library` — the `PartDefinitionSO → PartLibrarySO → PartLibraryBlob` five-file pipeline (§4)
- `dots-authoring-baker` — `BodyPartAuthoring`, `CharacterRigAuthoring` + the cross-entity `CharacterRigBakingSystem` (§5)
- `dots-system-scaffold` — `BodyPartInitSystem` and the reworked design systems (§5)

---

## 1. Purpose & v1 scope

Unify the four disconnected per-part registries (`AnimatorTarget`, `DesignPart`+`DesignRange`, `Ragdoll2DJointRef`+`Ragdoll2DJointZone`, `EquipSocket`) into **one `BodyPart` buffer on the character root**, move all shared static per-part config (design variant layouts, ragdoll zones/settle speeds) out of per-instance chunk data into an enum-indexed **`PartLibrary` blob**, and make design state **semantic** (shape + palette group) instead of raw texture-array slice indices — so zombification is "set SkinColor → Zombie, keep every shape" instead of caller-computed index math.

**Locked decisions (Q&A 2026-07-01):**
- `AnimationTarget` stays the single part-identity key everywhere (no new part enum).
- Static config lives in `PartDefinitionSO` assets baked into a `PartLibrary` blob; entities carry only an index.
- Texture layout is a **shape × color grid** with two modes: stride formula *and* explicit slice table (no forced art re-export).
- **Full replacement**: `DesignAuthoring`, `Ragdoll2DAuthoring`, `AnimationTargetAuthoring`, `AnimatorAuthoring` are deleted after migration.
- Palette values (skin, hair) are rolled **once per character** into `CharacterPalette`; parts derive their color axis from it.
- Sockets and quad-less ragdoll pivots register in the unified buffer too (flag-tagged).
- `PersistedDesign` format change **breaks old saves** — acceptable, no migration code.

**v1 handles:**
- Unified `BodyPart` registry, assembled at bake (subscene units) and at spawn (prefab instantiate, `LinkedEntityGroup` rebuild).
- Design randomize / apply / change running off the grid + palette model.
- Ragdoll config (zones, settle speeds) read from the blob.
- One reusable part prefab proven end-to-end (e.g. `HumanMaleHead.prefab` nested in two character prefabs).
- Semantic conversion request (zombification path) exercised in `DOTSTestScene`.

**Out of v1:** actual Human→Zombie gameplay trigger (brain swap etc. — separate plan), art re-export of existing arrays to the clean stride (explicit-table mode covers them), Animation Editor tooling rework beyond keeping it compiling.

## 2. Architecture

Pure ECS; no MonoBehaviour bridge. The shape mirrors the project's library systems (`AttackLibrary`, `ItemLibrary`):

```
PartDefinitionSO (per modular part)        BodyPartAuthoring (per part GO)
        │                                          │ bakes BodyPartInfo on the child
        ▼                                          ▼
PartLibrarySO (_PartLibrary)              CharacterRigAuthoring (root GO)
        │ PartLibraryBakingSystem                  │ root config + empty BodyPart buffer
        ▼        (PostBakingSystemGroup)           ▼
PartLibraryBlob  ◄──── partDefIndex ──── CharacterRigBakingSystem (PostBakingSystemGroup)
   (shared, immutable)                     assembles BodyPart buffer from descendants,
                                           stamps ragdoll child components
```

- **Per-instance, mutable (entities):** `BodyPart` buffer (root), `CharacterPalette` (root), `PersistedDesign` (root), the existing per-quad pose/`ImageIndex` components (unchanged).
- **Shared, immutable (blob):** variant grids, ragdoll landing zones + settle speeds, per `PartDefId`.
- Children **self-describe** via `BodyPartInfo`, so the root buffer can be assembled identically from two sources: descendant scan at bake, `LinkedEntityGroup` scan at spawn — this keeps the existing `AnimatorTargetInitSystem` remap fix intact, just generalized.
- System-group placement is unchanged: spawn-init work stays in `SpawnInitSystemGroup`, runtime re-skin stays in `DesignSystemGroup` (after Health, before Animation — see `SystemGroups.cs`), library baking in `PostBakingSystemGroup`.

**← DECISION:** naming — `BodyPart` / `BodyPartInfo` / `CharacterRigAuthoring` as proposed, or your preferred names (e.g. `RigPart` / `PartInfo` / `UnitRigAuthoring`). Pick once; it's find-replace before build, painful after.

## 3. Entry points

Both existing entry patterns are preserved (request model, on the entity acted on):

- **`RandomizeDesign : IComponentData, IEnableableComponent`** *(existing, unchanged)* — baked enabled; `DesignRandomizeSystem` rolls palette + shapes on first spawn, disables it.
- **`ChangeDesignRequest : IComponentData, IEnableableComponent`** *(existing struct, new payload)* — becomes semantic:
  ```csharp
  public struct ChangeDesignRequest : IComponentData, IEnableableComponent
  {
      public PaletteChange paletteChanges;          // e.g. skin → Zombie (NoChange sentinel per group)
      public FixedList128Bytes<ShapeOverride> shapeOverrides; // (AnimationTarget, shapeIndex) — rare explicit swaps
  }
  ```
  `DesignChangeSystem` upserts into `PersistedDesign`/`CharacterPalette`, re-derives slices through the blob grid, fans out to quads, disables the request. Zombification = enable with `paletteChanges.skin = Zombie` — every part whose color axis follows `PaletteGroup.SkinColor` (including eyes, see §4) re-derives automatically.

## 4. Data model

**New enums** (`Assets/_Scripts/Data/Enums/PartEnums.cs`):
- `PartDefId : short` — one value per `PartDefinitionSO`, blob index key (Data-Blob-Pointer pattern, same as `ItemType`). **← DECISION:** initial values — proposal: `None, HumanHead, HumanTorso, HumanArmUpper, HumanArmLower, HumanHand, HumanLegUpper, HumanLegLower, HumanFoot, HumanHair, HumanMustache, HumanEye, HumanMouth, HumanNose, …` (one def per *interchangeable part kind*, not per L/R instance — `LowerLeftArm` and `LowerRightArm` both point at `HumanArmLower`).
- `PaletteGroup : byte { None, SkinColor, HairColor }` — **← DECISION:** confirm groups; do eyes follow `SkinColor` (zombie color column of the eye array = zombie eyes, recommended) or stay `None` with an explicit `shapeOverride` at conversion time?
- `BodyPartFlags : byte [Flags] { None, HasQuad, DesignSlot, RagdollJoint, ItemSocket }`.

**SO → Blob** (five-file `dots-blob-library` pipeline):
- `PartDefinitionSO` — fields: `PartDefId id`; **design block** (`GridMode mode` (StrideFormula | ExplicitTable), `int baseSlice`, `int shapeCount`, `int colorCount`, `PaletteGroup colorAxis`, `int[] sliceTable` (Table mode, length = shapeCount × colorCount)); **ragdoll block** (`float defaultSettleSpeed`, `LandingZone[] zones {min,max}`).
- `PartLibrarySO` (`_PartLibrary.asset`) — the list, indexed by `PartDefId`.
- `PartLibraryBlob` — `BlobArray<PartDef>` with nested `BlobArray<int>` table / `BlobArray<float2>` zones. Slice resolution: `Stride: baseSlice + shape * colorCount + color`; `Table: sliceTable[shape * colorCount + color]`.
- `PartLibrary` + `PartLibraryReference` components, `PartLibraryAuthoring` (scene GO, `DependsOn(so)`), `PartLibraryBakingSystem` (`PostBakingSystemGroup`, `IsCreated` dispose guard).

**New/changed components** (`Assets/_Scripts/Components/Units/BodyPartComponents.cs`):
- `BodyPart : IBufferElementData` (root): `Entity entity; AnimationTarget target; PartDefId partDef; BodyPartFlags flags;` — **replaces `AnimatorTarget`** (all readers migrate).
- `BodyPartInfo : IComponentData` (each part child): same fields minus `entity` — **absorbs `AnimationTargetTag`** (animation systems read `.target` from it).
- `CharacterPalette : IComponentData, IPersist` (root): `byte skinColor; byte hairColor;` (indices into each part's color axis).
- `PersistedDesign : IComponentData, IPersist` — slots become `(target, shapeIndex)`; palette lives in `CharacterPalette`. `FixedList512Bytes` stays (headroom is fine). Save break accepted.
- Deleted: `DesignPart`, `DesignRange`, `AnimatorTarget`, `AnimationTargetTag`, `Ragdoll2DJointRef`, `Ragdoll2DJointZone` (root copies), and the orphaned `UnitSkinColor` / `UnitHairColor` / `UnitHeadShape` / `UnitNoseShape` (`CharacterPalette` is their spiritual successor).

**Runtime context stays on entities; nothing managed goes in the blob.** Texture arrays themselves keep being bound via material + `ImageIndex`/MPB exactly as today.

## 5. Systems

**New:**
- `PartLibraryBakingSystem` — `PostBakingSystemGroup` (per `dots-blob-library`).
- `CharacterRigBakingSystem` — `PostBakingSystemGroup`, `[WorldSystemFilter(BakingSystem)]`. Replaces `Ragdoll2DBakingSystem`. For each root with `CharacterRigConfig`: walk descendants (collect into `NativeList` first — never structural-change inside the query, see [[Gotchas]]), build the `BodyPart` buffer from `BodyPartInfo` children, stamp `Ragdoll2D`/`Ragdoll2DJoint` on flagged joints with zone/settle data resolved from the blob, and honor the carried-over `reloadDesign` → `NewlySpawned` enable (absorbs `DesignReloadBakingSystem`'s trigger).
- `BodyPartInitSystem` — `SpawnInitSystemGroup`, replaces `AnimatorTargetInitSystem`. On `NeedsRigInit` (renamed from `NeedsAnimatorInit`, added by `UnitSpawnerSystem`): rebuild the `BodyPart` buffer by scanning `LinkedEntityGroup` for `BodyPartInfo` — the proven remap fix, now carrying `partDef` + `flags` through too.

**Edited (cutover, same groups/order as today):**
- `DesignRandomizeSystem` (`SpawnInitSystemGroup`) — rolls `CharacterPalette` once per character (one `Random` stream), then per `DesignSlot`-flagged part a `shapeIndex ∈ [0, shapeCount)` from the blob; writes `PersistedDesign`.
- `DesignApplySystem` (`SpawnInitSystemGroup`) + `DesignApplyUtil` (`_Scripts/Utils/`) — resolve slice via blob grid (`shape`, palette value for the part's `colorAxis`), write `ImageIndex` through the existing `ComponentLookup` pattern (`CompleteDependency()` guard stays).
- `DesignChangeSystem` (`DesignSystemGroup`) — consumes the semantic request (§3).
- Ragdoll runtime systems (`Ragdoll2DSystem`/launch/spawn-init) — read zones + settle speed from the blob via the joint's `partDef` instead of root buffers; per-joint authored *overrides* still win when set (§ authoring).
- Every other `DynamicBuffer<AnimatorTarget>` / `AnimationTargetTag` reader (grep both symbols — includes `ApplyAnimatedPoseSystem`, equip socket resolution in `ItemEquipSystem`, `ItemAttachPointAuthoring` consumers) — mechanical rename to `BodyPart` / `BodyPartInfo`.

**Authoring (new, replaces four files):**
- `BodyPartAuthoring` — one per part GO (quad, joint pivot, or socket). Fields: `AnimationTarget target; PartDefinitionSO partDef;` (optional — null ⇒ no design/ragdoll config), `bool isRagdollJoint; bool isItemSocket; int baseImageIndex;` plus per-instance ragdoll overrides `float settleSpeedOverride; float groundBufferOverride;` (0 = use blob default). Bakes `BodyPartInfo` + the existing quad set (`AnimationTargetRestPose`, `AnimationTargetPose`, `PostTransformMatrix`, `ImageIndex`, `ImageIndexOverride`) when it has a renderer. `TransformUsageFlags.Dynamic | NonUniformScale` for quads, `Dynamic` for pivots/sockets. **← DECISION:** keep the two ragdoll overrides on the authoring (recommended — they're genuinely per-placement) or force everything through the SO.
- `CharacterRigAuthoring` — root GO. Absorbs: `AnimatorAuthoring`'s starting layers (bakes `AnimationLayer` buffer + `SetAnimation` + `AnimationRequest` disabled), `Ragdoll2DAuthoring`'s root config (`visualChild`, fall speed, ground buffers, tilt offsets → `Ragdoll2DConfig` + `Ragdoll2DLaunch` disabled), `DesignAuthoring`'s `reloadDesign` flag, and bakes the empty `BodyPart` buffer + `RandomizeDesign` (enabled) + `PersistedDesign` + `CharacterPalette` + `ChangeDesignRequest` (disabled) + a `[BakingType] CharacterRigConfig` for the baking system.

**Modular part prefabs:** a part prefab (e.g. `HumanMaleHead.prefab`) is just a GO subtree whose nodes carry `BodyPartAuthoring` — nest it under any `CharacterRigAuthoring` root and the baking system finds it. No cross-prefab wiring, no drag-lists on the root.

## 7. Integration points

- **Animation** — consumes `BodyPartInfo.target` / `BodyPart` buffer instead of `AnimationTargetTag` / `AnimatorTarget`; clip pipeline (`AnimationClipSO`, `AnimationLibraryBlob`, layers) untouched since the key stays `AnimationTarget`.
- **Save** — `PersistedDesign` (new layout) + `CharacterPalette` ride the generic `IPersist` auto-discovery; no per-field code. Old save files invalid — delete on first run.
- **Items/equip** — `ItemLeftHand`/`ItemRightHand` sockets register with `BodyPartFlags.ItemSocket`; `ItemEquipSystem`/`EquipSocketAuthoring` resolve the socket entity from the unified buffer, equip logic itself unchanged.
- **Health/death → ragdoll** — `Ragdoll2DLaunch` request flow unchanged; only where the per-joint config comes from changes.
- **Editor tooling** — `Editor/AnimationEditor/` references `AnimationTargetAuthoring`-baked components for preview; keep it compiling against `BodyPartInfo`. **← DECISION:** minimal compile-fix only (recommended) vs. proper editor support for part prefabs this pass.
- **`HierarchyParentAuthoring` / `BillboardAuthoring`** — unaffected, stay as-is. `AnimationTargetNoIndexAuthoring` — **← DECISION:** fold into `BodyPartAuthoring` (a "no renderer" branch, recommended) or keep.

## 8. Proposed file manifest

**New:**
- `Assets/_Scripts/Data/Enums/PartEnums.cs` (`PartDefId`, `PaletteGroup`, `BodyPartFlags`)
- `Assets/_Scripts/Data/PartDefinitionSO.cs`, `Assets/_Scripts/Data/PartLibrarySO.cs`, `Assets/_Scripts/Data/Blobs/PartLibraryBlob.cs`
- `Assets/_Scripts/Components/Units/BodyPartComponents.cs`
- `Assets/_Scripts/Authoring/Units/BodyPartAuthoring.cs`, `Assets/_Scripts/Authoring/Units/CharacterRigAuthoring.cs`
- `Assets/_Scripts/Authoring/EntityLibraries/PartLibraryAuthoring.cs`
- `Assets/_Scripts/Systems/PostBakingSystemGroup/PartLibraryBakingSystem.cs`, `.../CharacterRigBakingSystem.cs`
- `Assets/_Scripts/Systems/SpawnSystemGroup/SpawnInitSystemGroup/BodyPartInitSystem.cs`

**Edited:** `DesignRandomizeSystem.cs`, `DesignApplySystem.cs`, `DesignChangeSystem.cs`, `DesignApplyUtil.cs`, ragdoll runtime systems, `UnitSpawnerSystem.cs` (`NeedsRigInit`), `DesignComponents.cs` (request/persist rework), all `AnimatorTarget`/`AnimationTargetTag` readers (grep), `Editor/AnimationEditor/*` compile fixes.

**Deleted after migration:** `DesignAuthoring.cs`, `Ragdoll2DAuthoring.cs`, `AnimationTargetAuthoring.cs`, `AnimatorAuthoring.cs`, `Ragdoll2DBakingSystem.cs`, `DesignReloadBakingSystem.cs`, `AnimatorTargetInitSystem.cs`, `UnitDesignComponents.cs` orphans.

**Assets:** `_PartLibrary.asset`, one `PartDefinitionSO` per part kind, part prefabs (`HumanMaleHead.prefab` first), migrated character prefabs (citizen, rotter, minion, player body), `DOTSTestScene` rebake.

## 9. Build phases

1. **Data layer** — enums + five-file `PartLibrary` pipeline + `BodyPartComponents.cs`. Author `_PartLibrary` with 2–3 real `PartDefinitionSO`s (head in Table mode against the *existing* array layout, one limb in Stride mode). Gate: clean console, blob visible on the library entity.
2. **Unified registry** — `BodyPartAuthoring` + `CharacterRigAuthoring` + `CharacterRigBakingSystem` + `BodyPartInitSystem`; migrate the citizen prefab only; cut all `AnimatorTarget`/`AnimationTargetTag` readers over. Gate: citizen animates + spawns correctly from both subscene and runtime spawner.
3. **Design cutover** — randomize/apply/change on grid + palette; semantic `ChangeDesignRequest`. Gate: random consistent designs on spawn; manual zombify request re-skins skin-axis parts only.
4. **Ragdoll cutover** — blob-sourced zones/settle; delete `Ragdoll2DBakingSystem` path. Gate: death ragdoll behaves identically to pre-refactor.
5. **Breadth + cleanup** — migrate remaining prefabs, build `HumanMaleHead.prefab` and nest it in a second character, delete the four legacy authorings + orphan components, update `_Vault/Memories/Code/` (Authoring, Components, Systems_Animation, Systems) and `Skills.md` cross-refs.

## 10. Verification

Per phase, the standard loop: save `.cs` → Console clean (user check after focusing Unity; no Unity MCP) → rebake (`DOTSTestScene` reopen) → Play.
- **P1:** Entities window → library entity has `PartLibrary` with expected def count.
- **P2:** citizen idles/walks (pose fan-out works); spawn via `UnitSpawnerSystem` → `BodyPart` buffer correct from frame 2 (inspect in Entities window); no collapsed quads on spawn frame.
- **P3:** spawn 10 citizens → each has uniform skin across head/arms, varied between citizens; enable a `ChangeDesignRequest { skin = Zombie }` on one in the inspector → head/arms/eyes flip to zombie variants, hair/clothes untouched; save + reload → same look (`PersistedDesign` + `CharacterPalette` round-trip).
- **P4:** kill a unit → joints settle into blob-authored zones; per-joint override on one pivot visibly wins.
- **P5:** the shared head prefab renders correctly on both characters; a design roll on each stays independent.
- **Spencer-only:** visual quality of zombie variants per array (Table-mode entries match the real slice layout), Animation Editor preview usability.

## Open decisions (collected)

- [ ] §2 — Naming: `BodyPart` / `BodyPartInfo` / `CharacterRigAuthoring` (or alternatives).
- [ ] §4 — `PartDefId` initial value list (one per interchangeable part kind).
- [ ] §4 — `PaletteGroup` set; do eyes follow `SkinColor` (recommended) or use explicit shape overrides at conversion?
- [ ] §5 — Per-instance ragdoll overrides (`settleSpeed`, `groundBuffer`) stay on `BodyPartAuthoring` (recommended) or SO-only.
- [ ] §7 — Animation Editor: compile-fix only (recommended) or full part-prefab support this pass.
- [ ] §7 — `AnimationTargetNoIndexAuthoring`: fold into `BodyPartAuthoring` (recommended) or keep separate.
