using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

// Unified character-rig registry. Replaces the four disconnected per-part registries
// (AnimatorTarget, DesignPart/DesignRange, Ragdoll2DJointRef/Zone, EquipSocket) with one BodyPart
// buffer on the root plus a self-describing BodyPartInfo on each part child. The root buffer is
// assembled identically from two sources: a descendant scan at bake (CharacterRigBakingSystem) and
// a LinkedEntityGroup scan at spawn (BodyPartInitSystem) — the proven remap fix, generalized.

// Root buffer entry. `entity` is the live child part; the rest is copied from its BodyPartInfo so
// readers never need a second lookup. Replaces AnimatorTarget (animation reads `.target` here).
[InternalBufferCapacity(32)]
public struct BodyPart : IBufferElementData
{
    public Entity        entity;
    public AnimationTarget target;
    public UnitPartId     unitPart;
    public BodyPartFlags flags;
}

// Self-description on each part child (quad, joint pivot, or socket). Absorbs AnimationTargetTag —
// animation systems read `.target` from it. `partDef` indexes the PartLibrary blob for design +
// ragdoll config; `flags` says which roles this part fills.
public struct BodyPartInfo : IComponentData
{
    public AnimationTarget target;
    public UnitPartId     unitPart;
    public BodyPartFlags flags;
}

// One resolved (group → tag) assignment on a character, e.g. group "Skin" → tag "Tan".
public struct PaletteEntry
{
    public FixedString32Bytes group;
    public FixedString32Bytes tag;
}

// One rolled (or explicitly set) colour on a character: the active index into one palette's colour
// list. Keyed by the palette type — the palette IS the colour sharing group (see ColorEnums.cs).
public struct ColorChoice
{
    public ColorPaletteType palette;
    public byte index;
}

// Per-character rolled visual identity on the root: the active SHAPE tag per free-text group, the
// active COLOUR index per palette type, and the alternate-colour mode. Rolled once by
// DesignRandomizeSystem and shared by every part in the group/palette (so hair + mustache always
// match, skin is uniform across arms/face). Blittable (fixed strings + bytes, no Entity/Blob) so it
// rides the generic IPersist save path with no per-field code — layout changes break old saves
// (accepted precedent, see DesignComponents.cs). Zombification = a ChangeDesignRequest with
// paletteChanges { "Skin" → "Zombie" } (parts with zombie shapes) + alternateColorMode = enable —
// every palette entry carries a corresponding alternative colour (its zombie variant), so the
// character keeps its rolled identity: pale skin turns into ITS pale-zombie green.
// Get/set entries through DesignApplyUtil (GetTag/SetTag, GetColorIndex/SetColorIndex).
public struct CharacterPalette : IComponentData, IPersist
{
    // CEILING: FixedList512Bytes<PaletteEntry> holds at most ~7 entries (PaletteEntry is 64B).
    // Palette groups are free-text and designed to grow without code changes — the 8th distinct
    // group would fail at RUNTIME, not bake. DesignApplyUtil.SetTag guards + warns at capacity
    // instead of throwing. Revisit the backing size before shipping >6 distinct groups.
    public FixedList512Bytes<PaletteEntry> groups;

    // Rolled colour index per palette type (~31 cap at 2 bytes/entry — plenty for the enum).
    public FixedList64Bytes<ColorChoice> colors;

    // 1 = every palette slot resolves to its ALTERNATIVE colour (the zombie/converted variant).
    public byte useAlternateColors;
}

// Baking-only marker on a character root, added by CharacterRigAuthoring. Lets CharacterRigBakingSystem
// identify rig roots when assembling the BodyPart buffer. [BakingType] → stripped from the runtime world.
[BakingType]
public struct CharacterRigConfig : IComponentData { }

// The character root a part belongs to — baked by BodyPartAuthoring, read by CharacterRigBakingSystem
// when assembling the root's BodyPart buffer.
public struct BaseParent : IComponentData
{
    public Entity baseParentEntity;
}

// Per-instance sprite tint. Drives the _BaseColor shader property (Hybrid Per
// Instance in the 2D graphs) so every body-part quad can carry its own colour
// while the whole crowd still batches into one draw call. Multiply-tint: the
// sprite is baked white-fill / black-outline, so Value multiplies the fill and
// the black outline (0 * tint = 0) survives untouched. White (1,1,1,1) = the
// authored sprite unchanged. Baked white by BodyPartAuthoring; a skin/design
// system writes per-part colours.
[MaterialProperty("_BaseColor")]
public struct BodyPartTint : IComponentData
{
    public float4 Value; // RGBA, multiplies the sprite
}

// Per-instance colour for the packed mask's G layer (drives _SecondaryColor on the packed 2D
// graphs, Hybrid Per Instance). PackedChannelRecolor uses RGB as the layer tint and ALPHA as the
// layer blend strength (0 hides the layer). Baked white by BodyPartAuthoring with alpha from its
// useLayerChannels flag (0 = the part's mask doesn't use G/B — stray channel data can never
// composite); DesignApplyUtil.ApplyDesign writes palette colours over it.
[MaterialProperty("_SecondaryColor")]
public struct BodyPartSecondaryTint : IComponentData
{
    public float4 Value; // RGB layer tint, A = blend strength
}

// Per-instance colour for the packed mask's B layer (drives _TertiaryColor). Same semantics as
// BodyPartSecondaryTint.
[MaterialProperty("_TertiaryColor")]
public struct BodyPartTertiaryTint : IComponentData
{
    public float4 Value; // RGB layer tint, A = blend strength
}
