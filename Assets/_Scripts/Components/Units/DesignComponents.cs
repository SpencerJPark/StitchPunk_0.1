using Unity.Collections;
using Unity.Entities;

// Unit Design System — per-instance visual identity, now semantic (shape + palette) instead of raw
// texture-array slice indices. Static per-part config (variant grid, colour axis) lives in the
// PartLibrary blob; the root carries only the rolled shape per part (PersistedDesign) and the
// character-level colours (CharacterPalette, in BodyPartComponents.cs). Slices are re-derived through
// the blob grid at apply time. Parts are resolved through the root's BodyPart buffer.
// PersistedDesign's format change breaks old saves — accepted, no migration.

// Blittable result entry: the chosen SHAPE OFFSET for a part — the position within the part's tag
// pool (the designs matching the character's tag for its group, plus tag-independent designs), so a
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

// How a ChangeDesignRequest touches the character's alternate-colour mode.
public enum AlternateColorMode : byte
{
    Unchanged,  // leave CharacterPalette.useAlternateColors as-is
    Enable,     // switch every palette slot to its alternative colour (zombify)
    Disable,    // back to the primary colours (revert)
}

// Runtime re-skin request — a caller sets palette tag changes (group → new tag), an alternate-colour
// mode switch, and/or explicit shape overrides and enables the component; DesignChangeSystem upserts
// them into CharacterPalette / PersistedDesign, re-derives every design slice + colour through the
// blobs, fans them to the child quads, then disables the request. Zombification =
// paletteChanges { group "Skin", tag "Zombie" } (parts with zombie shapes) +
// alternateColorMode = Enable (every palette entry shows its zombie `alternative`, rolled identity
// kept). NOT IPersist — the request itself is never saved (the upserted CharacterPalette state is
// what persists).
public struct ChangeDesignRequest : IComponentData, IEnableableComponent
{
    public FixedList512Bytes<PaletteEntry> paletteChanges;
    public FixedList128Bytes<ShapeOverride> shapeOverrides;
    public AlternateColorMode alternateColorMode;
}

// Bake-time authored roll pool on a character root (from CharacterRigAuthoring.randomTags): the
// shape tags a random spawn may roll, per group. The SO stays purely descriptive — designs whose
// tag appears in no entry (e.g. "Zombie") are reachable only via ChangeDesignRequest. Read by
// DesignRandomizeSystem; one entry per (group, tag) candidate.
[InternalBufferCapacity(8)]
public struct RandomTagOption : IBufferElementData
{
    public FixedString32Bytes group;
    public FixedString32Bytes tag;
}

// Bake-time only: added by CharacterRigAuthoring when `reloadDesign` is checked. Pre-placed
// (subscene-baked) units never pass through UnitSpawnerSystem, so NewlySpawned is never enabled and
// the spawn-init design pipeline never runs. DesignReloadBakingSystem (PostBakingSystemGroup) enables
// NewlySpawned on these flagged units after all bakers complete, so they roll + apply a random design
// once on load. Stripped from the runtime world.
[BakingType]
public struct DesignReloadOnBake : IComponentData { }
