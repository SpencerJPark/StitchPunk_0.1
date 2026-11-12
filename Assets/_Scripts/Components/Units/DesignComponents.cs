using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

// Static per-part config lives in the PartLibrary blob; the root carries only the rolled shape per
// part (PersistedDesign) and the character colours (CharacterPalette, in BodyPartComponents.cs).

// Shape offset within the part's tag pool, so a tag swap keeps the shape. Colour lives in CharacterPalette.
public struct DesignSlot
{
    public int target;      // (int)AnimationTarget
    public int shapeIndex;  // offset into the part's tag pool (see DesignApplyUtil.SliceAtOffset)
}

// No Entity/Blob fields, so PersistRegistry auto-discovers it for saves. Format changes break old saves.
public struct PersistedDesign : IComponentData, IPersist
{
    public FixedList512Bytes<DesignSlot> slots;
}

// Explicit shape swap that bypasses randomization.
public struct ShapeOverride
{
    public int target;      // (int)AnimationTarget
    public int shapeIndex;  // offset into the part's tag pool
}

public enum AlternateColorMode : byte
{
    Unchanged,  // leave CharacterPalette.useAlternateColors as-is
    Enable,     // switch every palette slot to its alternative colour (zombify)
    Disable,    // back to the primary colours (revert)
}

// Enable it to re-skin; DesignChangeSystem upserts into CharacterPalette / PersistedDesign, re-applies, then disables it.
// Zombify = paletteChanges { "Skin", "Zombie" } + alternateColorMode Enable. Not IPersist: only the upserted state is saved.
public struct ChangeDesignRequest : IComponentData, IEnableableComponent
{
    public FixedList512Bytes<PaletteEntry> paletteChanges;
    public FixedList128Bytes<ShapeOverride> shapeOverrides;
    public AlternateColorMode alternateColorMode;
}

// Tags a random spawn may roll, per group. Designs whose tag is in no entry (e.g. "Zombie") are reachable only via ChangeDesignRequest.
[InternalBufferCapacity(8)]
public struct RandomTagOption : IBufferElementData
{
    public FixedString32Bytes group;
    public FixedString32Bytes tag;
}

// Pre-placed subscene units skip UnitSpawnerSystem, so DesignReloadBakingSystem enables NewlySpawned on
// flagged ones to roll a design once on load. Bake-time only.
[BakingType]
public struct DesignReloadOnBake : IComponentData { }

public struct NeckLength : IComponentData
{
    public float3 headPosition;
}

public struct UnitHeight : IComponentData
{
    public float scale;
}
