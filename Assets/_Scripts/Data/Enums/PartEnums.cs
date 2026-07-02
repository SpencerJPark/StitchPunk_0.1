using System;

// Character-rig part identity + config enums. Feeds the PartLibrary blob (Data-Blob-Pointer pattern,
// same shape as ItemType → ItemLibraryBlob). One PartDefId value per interchangeable part KIND — the
// L/R instances of a limb share a kind (LowerLeftArm and LowerRightArm both point at MaleArmLower).

public enum PartDefId : short
{
    None,
    MaleHead,
    MaleTorso,
    MaleArmUpper,
    MaleArmLower,
    MaleHand,
    MaleLegUpper,
    MaleLegLower,
    MaleFoot,
    MaleHair,
    MaleMustache,
    MaleRightEye,
    MaleLeftEye,
    MaleRightEyebrow,
    MaleLeftEyebrow,
    MaleMouth,
    MaleNose,
    MaleEar,
}

// Palette groups are now free-text string tags (authored on PartDefinitionSO.group and stored per
// character in CharacterPalette), not an enum — so new groups can be added without code changes.
// A part follows its group's rolled tag; e.g. group "Skin" → tag "Zombie" recolours all skin parts.

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
