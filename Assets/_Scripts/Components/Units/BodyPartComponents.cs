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
    public PartDefId     partDef;
    public BodyPartFlags flags;
}

// Self-description on each part child (quad, joint pivot, or socket). Absorbs AnimationTargetTag —
// animation systems read `.target` from it. `partDef` indexes the PartLibrary blob for design +
// ragdoll config; `flags` says which roles this part fills.
public struct BodyPartInfo : IComponentData
{
    public AnimationTarget target;
    public PartDefId     partDef;
    public BodyPartFlags flags;
}

// One resolved (group → tag) assignment on a character, e.g. group "Skin" → tag "Tan".
public struct PaletteEntry
{
    public FixedString32Bytes group;
    public FixedString32Bytes tag;
}

// Per-character rolled colour identity on the root: the active tag per palette group. Rolled once by
// DesignRandomizeSystem and shared by every part in that group (so hair + mustache always match).
// Blittable (fixed strings, no Entity/Blob) so it rides the generic IPersist save path with no
// per-field code. Zombification = a ChangeDesignRequest that sets the "Skin" group's tag to "Zombie";
// every Skin-group part with a matching range re-derives its slice, shape preserved. Get/set the tag
// for a group through DesignApplyUtil.GetTag / SetTag.
public struct CharacterPalette : IComponentData, IPersist
{
    // CEILING: FixedList512Bytes<PaletteEntry> holds at most ~7 entries (PaletteEntry is 64B).
    // Palette groups are free-text and designed to grow without code changes — the 8th distinct
    // group would fail at RUNTIME, not bake. DesignApplyUtil.SetTag guards + warns at capacity
    // instead of throwing. Revisit the backing size before shipping >6 distinct groups.
    public FixedList512Bytes<PaletteEntry> groups;
}

// Baking-only marker on a character root, added by CharacterRigAuthoring. Lets CharacterRigBakingSystem
// identify rig roots when assembling the BodyPart buffer. [BakingType] → stripped from the runtime world.
[BakingType]
public struct CharacterRigConfig : IComponentData { }

// Baking-only carrier for a ragdoll joint's per-placement overrides. Written by BodyPartAuthoring on
// each RagdollJoint part; read by CharacterRigBakingSystem to resolve the joint's settle speed
// (override > 0 wins over the PartLibrary blob default). [BakingType] → stripped from the runtime world.
[BakingType]
public struct RagdollJointBakeData : IComponentData
{
    public float settleSpeedOverride;
    public float groundBufferOverride;
}
