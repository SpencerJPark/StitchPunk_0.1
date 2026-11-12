using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

// The root BodyPart buffer is built identically from a bake-time descendant scan and a spawn-time LinkedEntityGroup scan.

// Root buffer entry; fields are copied from BodyPartInfo so readers skip a second lookup.
[InternalBufferCapacity(32)]
public struct BodyPart : IBufferElementData
{
    public Entity        entity;
    public AnimationTarget target;
    public UnitPartId     unitPart;
    public BodyPartFlags flags;
}

public struct BodyPartInfo : IComponentData
{
    public AnimationTarget target;
    public UnitPartId     unitPart;
    public BodyPartFlags flags;
}

// One resolved (group, tag) assignment, e.g. group "Skin", tag "Tan".
public struct PaletteEntry
{
    public FixedString32Bytes group;
    public FixedString32Bytes tag;
}

// Keyed by palette type; the palette is the colour sharing group.
public struct ColorChoice
{
    public ColorPaletteType palette;
    public byte index;
}

// Blittable so it rides the generic IPersist save path; layout changes break old saves (accepted).
// Get/set entries through DesignApplyUtil.
public struct CharacterPalette : IComponentData, IPersist
{
    // CEILING: about 7 entries (PaletteEntry is 64B). An 8th distinct group fails at RUNTIME, not bake;
    // DesignApplyUtil.SetTag warns at capacity instead of throwing.
    public FixedList512Bytes<PaletteEntry> groups;

    // Cap is about 31 entries at 2 bytes each.
    public FixedList64Bytes<ColorChoice> colors;

    // 1 = every palette slot resolves to its alternative (zombie) colour.
    public byte useAlternateColors;
}

[BakingType]
public struct CharacterRigConfig : IComponentData { }

public struct BaseParent : IComponentData
{
    public Entity baseParentEntity;
}

// Multiply tint: sprite is baked white fill, black outline, so the outline survives. White = unchanged.
[MaterialProperty("_BaseColor")]
public struct BodyPartTint : IComponentData
{
    public float4 Value; // RGBA, multiplies the sprite
}

// Alpha is layer blend strength (0 hides the layer). Baked alpha 0 when the part's mask does not use G/B,
// so stray channel data never composites.
[MaterialProperty("_SecondaryColor")]
public struct BodyPartSecondaryTint : IComponentData
{
    public float4 Value; // RGB layer tint, A = blend strength
}

[MaterialProperty("_TertiaryColor")]
public struct BodyPartTertiaryTint : IComponentData
{
    public float4 Value; // RGB layer tint, A = blend strength
}

// BodyPartTint can't whiten, so the 2D graphs lerp toward rgb by alpha. Baked (1,1,1,0) = no flash.
[MaterialProperty("_FlashColor")]
public struct BodyPartFlash : IComponentData
{
    public float4 Value;
}
