using System;

// Character-rig part identity + config enums. Feeds the PartLibrary blob (Data-Blob-Pointer pattern,
// same shape as ItemType → ItemLibraryBlob). One PartDefId value per interchangeable part KIND — the
// L/R instances of a limb share a kind (LowerLeftArm and LowerRightArm both point at HumanArmLower).

public enum PartDefId : short
{
    None,
    HumanHead,
    HumanTorso,
    HumanArmUpper,
    HumanArmLower,
    HumanHand,
    HumanLegUpper,
    HumanLegLower,
    HumanFoot,
    HumanHair,
    HumanMustache,
    HumanEye,
    HumanEyebrow,
    HumanMouth,
    HumanNose,
    HumanEar,
}

// Which per-character palette axis a part derives its colour column from. Zombification enables a
// ChangeDesignRequest that sets CharacterPalette.skinColor → the zombie column; every part whose
// colorAxis is SkinColor (head, arms, eyes, …) re-derives its slice automatically. HairColor parts
// (hair, mustache) follow the hair axis and stay untouched by a skin conversion.
public enum PaletteGroup : byte
{
    None,
    SkinColor,
    HairColor,
}

// How a PartDef resolves a (shape, color) pair to a texture-array slice.
public enum GridMode : byte
{
    StrideFormula,   // baseSlice + shape * colorCount + color   (clean re-exported arrays)
    ExplicitTable,   // sliceTable[shape * colorCount + color]    (existing irregular arrays)
}

// Role flags carried per BodyPart entry so the root buffer describes each part's capabilities
// without a separate lookup. HasQuad = renders (has ImageIndex/pose); DesignSlot = randomizable
// shape; RagdollJoint = a bend pivot that gets Ragdoll2DJoint; ItemSocket = an equip attach point.
[Flags]
public enum BodyPartFlags : byte
{
    None         = 0,
    HasQuad      = 1 << 0,
    DesignSlot   = 1 << 1,
    RagdollJoint = 1 << 2,
    ItemSocket   = 1 << 3,
}
