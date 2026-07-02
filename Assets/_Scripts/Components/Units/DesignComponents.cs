using Unity.Collections;
using Unity.Entities;

// Unit Design System — per-instance visual identity, now semantic (shape + palette) instead of raw
// texture-array slice indices. Static per-part config (variant grid, colour axis) lives in the
// PartLibrary blob; the root carries only the rolled shape per part (PersistedDesign) and the
// character-level colours (CharacterPalette, in BodyPartComponents.cs). Slices are re-derived through
// the blob grid at apply time. Parts are resolved through the root's BodyPart buffer.
// PersistedDesign's format change breaks old saves — accepted, no migration.

// Blittable result entry: the chosen SHAPE for a part (was: raw imageIndex). Two ints so it rides a
// FixedList. Colour is not stored per-slot — it comes from CharacterPalette via the part's colorAxis.
public struct DesignSlot
{
    public int target;      // (int)AnimationTarget
    public int shapeIndex;  // chosen shape variant, in [0, PartDef.shapeCount)
}

// Result + persistence. Value-type IPersist IComponentData with no Entity/Blob fields → auto-discovered
// by PersistRegistry and round-tripped by the generic save pipeline with no per-field code.
public struct PersistedDesign : IComponentData, IPersist
{
    public FixedList512Bytes<DesignSlot> slots;
}

// A requested palette shift per group. NoChange (-1) leaves that group untouched. Zombification =
// paletteChanges.skin = zombie column; every SkinColor-axis part re-derives its slice automatically.
public struct PaletteChange
{
    public const short NoChange = -1;

    public short skin;   // new CharacterPalette.skinColor, or NoChange
    public short hair;   // new CharacterPalette.hairColor, or NoChange
}

// A rare explicit shape swap that bypasses randomization (e.g. force a specific head shape on convert).
public struct ShapeOverride
{
    public int target;      // (int)AnimationTarget
    public int shapeIndex;
}

// Runtime re-skin request — a caller sets palette shifts and/or explicit shape overrides and enables
// the component; DesignChangeSystem applies them to CharacterPalette / PersistedDesign, re-derives all
// design slices through the blob grid, fans them to the child quads, then disables the request.
// NOT IPersist — the request itself is never saved.
public struct ChangeDesignRequest : IComponentData, IEnableableComponent
{
    public PaletteChange paletteChanges;
    public FixedList128Bytes<ShapeOverride> shapeOverrides;
}

// Bake-time only: added by CharacterRigAuthoring when `reloadDesign` is checked. Pre-placed
// (subscene-baked) units never pass through UnitSpawnerSystem, so NewlySpawned is never enabled and
// the spawn-init design pipeline never runs. DesignReloadBakingSystem (PostBakingSystemGroup) enables
// NewlySpawned on these flagged units after all bakers complete, so they roll + apply a random design
// once on load. Stripped from the runtime world.
[BakingType]
public struct DesignReloadOnBake : IComponentData { }
