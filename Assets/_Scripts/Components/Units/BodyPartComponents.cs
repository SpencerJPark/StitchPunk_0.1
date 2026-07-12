using Unity.Collections;
using Unity.Entities;

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

// Baking-only carrier for a ragdoll joint's resolved config. Written by RagdollJointAuthoring on
// each dedicated joint empty (values from its RagdollJointSO, per-placement settle override already
// applied); read by CharacterRigBakingSystem when stamping Ragdoll2DJoint. Ragdoll config is fully
// separate from the design pipeline — nothing ragdoll lives on UnitPartSO / the PartLibrary blob.
// [BakingType] → stripped from the runtime world.
[BakingType]
public struct RagdollJointBakeData : IComponentData
{
    public float settleSpeed;          // deg/s toward the landing angle (override > 0 already applied)
    public float segmentLength;        // pendulum length (world units) for the flail
    public float weight;               // scales inherited motion + landing impulse
    public float groundBufferOverride; // reserved per-placement ground buffer override
}
