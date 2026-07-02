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

// Per-character rolled colour identity on the root. Indices into each part's colour axis: skinColor
// indexes the SkinColor column set, hairColor the HairColor column set. Blittable (two bytes, no
// Entity/Blob) so it rides the generic IPersist save path with no per-field code. Zombification =
// a ChangeDesignRequest that sets skinColor to the zombie column; SkinColor-axis parts re-derive.
public struct CharacterPalette : IComponentData, IPersist
{
    public byte skinColor;
    public byte hairColor;
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
