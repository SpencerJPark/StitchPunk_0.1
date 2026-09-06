// Copyright (c) 2026 Spencer Park. All rights reserved.

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>How serious a validation finding is. Errors fail the bake; warnings are surfaced and the bake proceeds.</summary>
    public enum ValidationSeverity : byte
    {
        /// <summary>The asset is usable; the finding is advisory.</summary>
        Warning = 0,

        /// <summary>The asset is not bakeable as authored.</summary>
        Error = 1
    }

    /// <summary>
    /// The authoritative validation rule codes, shared by the inspectors, the clip editor, the bake,
    /// and the documentation. Never renamed for readability, since the code itself is the contract.
    /// </summary>
    public enum ValidationCode : byte
    {
        /// <summary>No finding.</summary>
        None = 0,

        /// <summary>Error: the clip's duration is below <see cref="ClipAsset.MinimumDuration"/>.</summary>
        V01 = 1,

        /// <summary>Error: a <see cref="VatTrack"/>'s <c>targetId</c> is not a target of the rig it is bound to.</summary>
        V02 = 2,

        /// <summary>Error: a track's keys are not in strictly ascending <c>normalizedTime</c> order.</summary>
        V03 = 3,

        /// <summary>Error: a key or marker has a <c>normalizedTime</c> outside [0, 1].</summary>
        V04 = 4,

        /// <summary>Error: two clips in one set share a <see cref="ClipId"/>, or two rig targets share a <see cref="TargetId"/>.</summary>
        V05 = 5,

        /// <summary>Retired: never emitted. Kept unused rather than reused, since old docs and changelogs still name it.</summary>
        V06 = 6,

        /// <summary>Error: the set has a VAT-sourced clip but no texture set, or the texture set has no range for that clip.</summary>
        V07 = 7,

        /// <summary>
        /// The texture set's <c>sourceHash</c> disagrees with the current sources. An error while
        /// authoring; downgraded to a warning at entity-bake time, where outdated textures still render.
        /// </summary>
        V08 = 8,

        /// <summary>Error: an event marker uses a key below <see cref="ReservedEventKeys.FirstUserKey"/>.</summary>
        V09 = 9,

        /// <summary>Warning: the clip has no tracks and no events. Legal — it holds the rest pose.</summary>
        V10 = 10,

        /// <summary>Warning: the same clip asset is listed more than once in one set. Deduplicated at bake.</summary>
        V11 = 11,

        /// <summary>Warning: a blend default is longer than the clip's duration. Clamped at bake.</summary>
        V12 = 12,

        /// <summary>Error: the rig defines no layers, or more than <see cref="RigAsset.MaxLayerCount"/>.</summary>
        V13 = 13,

        /// <summary>
        /// Warning: an <see cref="SpriteIndexMode.Absolute"/> sprite key's <c>sliceIndex</c> is
        /// below −1. Scoped to absolute keys because a relative key's stored number is a
        /// displacement, where any negative is a legitimate step backwards.
        /// </summary>
        V14 = 14,

        /// <summary>Error: a <see cref="BoneTrack"/>'s <c>boneName</c> is null or empty — a bone track has no stable id, so the name is its only identity.</summary>
        V15 = 15,

        /// <summary>Error: two bone tracks in one clip name the same bone. Mirrors V05's duplicate-identity shape, scoped to <see cref="BoneTrack.boneName"/>.</summary>
        V16 = 16,

        /// <summary>
        /// Error: a <see cref="Interpolation.Bezier"/> key's handles leave the unit square. x
        /// outside [0, 1] makes the curve non-functional; y outside it breaks the bake's bounds
        /// union, which assumes every mode is monotonic between keys.
        /// </summary>
        V17 = 17,

        /// <summary>
        /// Error: a <see cref="SpriteIndexMode.RelativeToBase"/> key resolves to a negative array
        /// index against its track's <c>baseIndex</c>. The −1 "no change" sentinel belongs to
        /// absolute keys only.
        /// </summary>
        V18 = 18,

        /// <summary>Error: an event marker's <c>windowSeconds</c> is negative. The bake clamps it to 0, turning a held state into a bare pulse.</summary>
        V19 = 19,

        /// <summary>
        /// Warning: an event marker authors a window on a key outside the maskable range
        /// 16–79, so it owns no <see cref="AnimEventMask"/> bit and the window can never be observed.
        /// </summary>
        V20 = 20,

        /// <summary>
        /// Error: a <see cref="BillboardRootDefinition"/> addresses a node that does not exist — a
        /// <see cref="RigNodeAddressKind.RigTarget"/> id that is not a target of this rig, or a
        /// <see cref="RigNodeAddressKind.HierarchyPath"/> with no path.
        /// </summary>
        V21 = 21,

        /// <summary>Error: two <see cref="BillboardRootDefinition"/> rows address the same node. Mirrors V05, scoped to the addressed node.</summary>
        V22 = 22,

        /// <summary>Error: a <see cref="BillboardMode.AxisConstrained"/> root's <c>constraintAxis</c> is zero-length, so there is no axis to turn about.</summary>
        V23 = 23,

        /// <summary>Error: a <see cref="BillboardTrack"/>'s <c>rootStableId</c> is not a billboard root of the clip's rig.</summary>
        V24 = 24,

        /// <summary>
        /// Error: a <see cref="RigAsset.billboardRoots"/> row carries a
        /// <see cref="RigNodeAddressKind.Bone"/> address. Billboarding has no bone path — a
        /// billboard root turns a node's transform, and a bone has none of its own.
        /// </summary>
        V25 = 25,

        /// <summary>
        /// Error: a <see cref="RagdollBodyDefinition"/>'s <c>address</c> names a
        /// <see cref="RigNodeAddressKind.RigTarget"/> id that is not a target of this rig.
        /// </summary>
        V26 = 26,

        /// <summary>Error: a <see cref="RagdollBodyDefinition"/>'s <c>stableId</c> is left at the reserved 0 value, or duplicates another body's id.</summary>
        V27 = 27,

        /// <summary>Error: two <see cref="RagdollBodyDefinition"/> rows address the same node. Mirrors V22's shape, scoped to a ragdoll body's node.</summary>
        V28 = 28,

        /// <summary>Error: a <see cref="RagdollBodyDefinition"/>'s <c>boxSize</c> has a component that is not greater than 0.</summary>
        V29 = 29,

        /// <summary>
        /// Error: a <see cref="RagdollBodyDefinition"/>'s joint limits are out of range —
        /// <c>limitMinDegrees</c> exceeds <c>limitMaxDegrees</c>, either one leaves [-180, 180], or
        /// <c>swingLimitDegrees</c> / <c>twistLimitDegrees</c> leaves [0, 180].
        /// </summary>
        V30 = 30,

        /// <summary>
        /// Warning: this rig's ragdoll bodies do not form a single tree — among the bodies whose
        /// place in the hierarchy this asset can itself confirm, more or fewer than exactly one has
        /// no ragdolled ancestor.
        /// </summary>
        V31 = 31,

        /// <summary>Error: a <see cref="RagdollBodyDefinition"/>'s <c>mass</c> is not greater than 0.</summary>
        V32 = 32,

        /// <summary>Error: a <see cref="TargetTagEntry"/>'s <c>stableId</c> is left at the reserved 0 value, or duplicates another entry's id.</summary>
        V33 = 33,

        /// <summary>Error: two <see cref="RigTargetDefinition"/> rows in one rig carry the same non-zero <c>tagId</c>. 0 ("untagged") is exempt.</summary>
        V34 = 34,

        /// <summary>
        /// Warning: a <see cref="TransformTrack"/> or <see cref="SpriteTrack"/> binds a tag that no
        /// target of the rig being checked against carries. A warning, not an error — this is what
        /// lets one shared clip animate only the parts of a roster that actually have them.
        /// </summary>
        V35 = 35,

        /// <summary>
        /// Error: a <see cref="TransformTrack"/> or <see cref="SpriteTrack"/> binds a tag id that no
        /// longer exists in the supplied <see cref="TargetTagRegistry"/> — a deleted tag, unlike V35's
        /// ordinary "not on this rig".
        /// </summary>
        V36 = 36,

        /// <summary>
        /// Warning: a <see cref="ClipAsset"/> referenced by more than one <see cref="ClipSetAsset"/>
        /// still carries at least one <see cref="TransformTrack"/> or <see cref="SpriteTrack"/>
        /// bound by target id rather than by tag — a target id is unique to the rig it was minted
        /// in, so it resolves to nothing on a second rig.
        /// </summary>
        V37 = 37,

        /// <summary>
        /// Warning: a <see cref="TransformTrack"/> or <see cref="SpriteTrack"/> is bound by target
        /// id to a target the rig of this bind does not declare. The id-bound mirror of V35.
        /// </summary>
        V38 = 38,

        /// <summary>Error: two or more clip sets bound to one actor each supply a <see cref="VatTextureSetAsset"/>; an actor addresses exactly one.</summary>
        V39 = 39,

        /// <summary>
        /// Error: a bound set's <c>vatTextures.sourceRigKey</c> names a different rig than the actor
        /// is bound to. A VAT texture cannot retarget. A <c>sourceRigKey</c> of 0 (baked before the
        /// field existed) passes.
        /// </summary>
        V40 = 40
    }

    /// <summary>Which caller is validating. Only changes the severity of the VAT-staleness check: a stale VAT bake blocks authoring but not entity baking.</summary>
    public enum ValidationStage : byte
    {
        /// <summary>Inspectors and the clip editor: every rule reports at its table severity.</summary>
        Authoring = 0,

        /// <summary>Entity baking: the VAT-staleness check reports as a warning instead of an error.</summary>
        Bake = 1
    }

    /// <summary>One validation finding: what rule fired, how serious it is, which asset it concerns, and a human-readable explanation.</summary>
    public readonly struct ValidationMessage
    {
        /// <summary>How serious this finding is.</summary>
        public readonly ValidationSeverity severity;

        /// <summary>Which rule produced it.</summary>
        public readonly ValidationCode code;

        /// <summary>The asset the finding concerns, for click-to-select in the inspector. May be null when the offending reference is itself missing.</summary>
        public readonly UnityEngine.Object assetContext;

        /// <summary>Human-readable explanation naming the offending data.</summary>
        public readonly string text;

        public ValidationMessage(
            ValidationSeverity severity,
            ValidationCode code,
            UnityEngine.Object assetContext,
            string text)
        {
            this.severity = severity;
            this.code = code;
            this.assetContext = assetContext;
            this.text = text;
        }

        /// <summary>True when this finding blocks baking.</summary>
        public bool IsError
        {
            get { return severity == ValidationSeverity.Error; }
        }

        /// <summary>Renders the finding as "<c>V07 (Error): text</c>" for logs and exception messages.</summary>
        public override string ToString()
        {
            return code.ToString() + " (" + severity.ToString() + "): " + text;
        }
    }
}
