using Unity.Collections;
using Unity.Entities;

// Unit Design System — per-instance visual identity, now semantic (shape + palette) instead of raw
// texture-array slice indices. Static per-part config (variant grid, colour axis) lives in the
// PartLibrary blob; the root carries only the rolled shape per part (PersistedDesign) and the
// character-level colours (CharacterPalette, in BodyPartComponents.cs). Slices are re-derived through
// the blob grid at apply time. Parts are resolved through the root's BodyPart buffer.
// PersistedDesign's format change breaks old saves — accepted, no migration.

// Blittable result entry: the chosen SHAPE OFFSET for a part — the position within the part's tag
// pool (the ranges matching the character's tag for its group, plus colour-independent ranges), so a
// tag swap keeps the shape. Colour is not stored per-slot — it comes from CharacterPalette via the
// part's group. Two ints so it rides a FixedList.
public struct DesignSlot
{
    public int target;      // (int)AnimationTarget
    public int shapeIndex;  // offset into the part's tag pool (see DesignApplyUtil.SliceAtOffset)
}

// Result + persistence. Value-type IPersist IComponentData with no Entity/Blob fields → auto-discovered
// by PersistRegistry and round-tripped by the generic save pipeline with no per-field code.
public struct PersistedDesign : IComponentData, IPersist
{
    public FixedList512Bytes<DesignSlot> slots;
}

// A rare explicit shape swap that bypasses randomization (e.g. force a specific head shape on convert).
public struct ShapeOverride
{
    public int target;      // (int)AnimationTarget
    public int shapeIndex;  // offset into the part's tag pool
}

// Runtime re-skin request — a caller sets palette tag changes (group → new tag) and/or explicit shape
// overrides and enables the component; DesignChangeSystem upserts the tags into CharacterPalette and
// the overrides into PersistedDesign, re-derives every design slice through the blob, fans them to the
// child quads, then disables the request. Zombification = paletteChanges { group "Skin", tag "Zombie" }.
// NOT IPersist — the request itself is never saved.
public struct ChangeDesignRequest : IComponentData, IEnableableComponent
{
    public FixedList512Bytes<PaletteEntry> paletteChanges;
    public FixedList128Bytes<ShapeOverride> shapeOverrides;
}

// Bake-time only: added by CharacterRigAuthoring when `reloadDesign` is checked. Pre-placed
// (subscene-baked) units never pass through UnitSpawnerSystem, so NewlySpawned is never enabled and
// the spawn-init design pipeline never runs. DesignReloadBakingSystem (PostBakingSystemGroup) enables
// NewlySpawned on these flagged units after all bakers complete, so they roll + apply a random design
// once on load. Stripped from the runtime world.
[BakingType]
public struct DesignReloadOnBake : IComponentData { }
