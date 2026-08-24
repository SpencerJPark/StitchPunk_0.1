// Copyright (c) 2026 Spencer Park. All rights reserved.

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// How serious a validation finding is (architecture section 3.5). Errors fail the bake;
    /// warnings are surfaced to the author and the bake proceeds.
    /// </summary>
    public enum ValidationSeverity : byte
    {
        /// <summary>The asset is usable; the finding is advisory.</summary>
        Warning = 0,

        /// <summary>The asset is not bakeable as authored.</summary>
        Error = 1
    }

    /// <summary>
    /// The authoritative validation rule codes of architecture section 3.5. The identifiers are the
    /// codes themselves because the code table is a shared normative contract — inspectors, the clip
    /// editor, the bake, and the documentation all name rules by these codes, so they are never
    /// renamed for readability.
    /// </summary>
    public enum ValidationCode : byte
    {
        /// <summary>No finding.</summary>
        None = 0,

        /// <summary>Error: the clip's duration is below <see cref="ClipAsset.MinimumDuration"/>.</summary>
        V01 = 1,

        /// <summary>Error: a track's <c>targetId</c> is not a target of the clip's rig.</summary>
        V02 = 2,

        /// <summary>Error: a track's keys are not in strictly ascending <c>normalizedTime</c> order.</summary>
        V03 = 3,

        /// <summary>Error: a key or marker has a <c>normalizedTime</c> outside [0, 1].</summary>
        V04 = 4,

        /// <summary>Error: two clips in one set share a <see cref="ClipId"/>, or two rig targets share a <see cref="TargetId"/>.</summary>
        V05 = 5,

        /// <summary>Error: a clip in the set is authored against a different rig than the set's.</summary>
        V06 = 6,

        /// <summary>Error: the set has a VAT-sourced clip but no texture set, or the texture set has no range for that clip.</summary>
        V07 = 7,

        /// <summary>
        /// The texture set's <c>sourceHash</c> disagrees with the current sources. An error while
        /// authoring; downgraded to a warning at entity-bake time, where outdated textures still
        /// render (architecture section 3.5).
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

        /// <summary>
        /// Error: a <see cref="BoneTrack"/>'s <c>boneName</c> is null or empty (Amendment A42). A
        /// bone track has no stable id — the name is its only identity — so an empty name names
        /// nothing for the VAT bake to pose.
        /// </summary>
        V15 = 15,

        /// <summary>
        /// Error: two bone tracks in one clip name the same bone (Amendment A42). Mirrors V05's
        /// duplicate-identity shape, scoped to <see cref="BoneTrack.boneName"/> instead of a stable
        /// id, since a bone's name is the only identity it has.
        /// </summary>
        V16 = 16,

        /// <summary>
        /// Error: a <see cref="Interpolation.Bezier"/> key's handles leave the unit square.
        /// </summary>
        /// <remarks>
        /// x outside [0, 1] makes the curve non-functional — one time mapping to two weights — and
        /// there is no correct value for the sampler to return. y outside it is overshoot, which is
        /// well defined but breaks the bake's bounds union: architecture section 4.6 unions the
        /// keys on the argument that every interpolation mode is monotonic between them, so a curve
        /// that travels past its own keys would produce a box too small and parts that cull while
        /// still visible.
        /// </remarks>
        V17 = 17,

        /// <summary>
        /// Error: a <see cref="SpriteIndexMode.RelativeToBase"/> key resolves to a negative array
        /// index against its track's <c>baseIndex</c> — a base and an offset that disagree. The −1
        /// "no change" sentinel belongs to absolute keys only, so there is nothing else this could
        /// mean.
        /// </remarks>
        V18 = 18,

        /// <summary>
        /// Error: an event marker's <c>windowSeconds</c> is negative. A window cannot run backwards,
        /// and the bake clamps it to 0 — which silently turns an event the author believed held a
        /// state into a bare pulse.
        /// </summary>
        V19 = 19,

        /// <summary>
        /// Warning: an event marker authors a window on a key outside the maskable range 16–79, so
        /// it owns no <see cref="AnimEventMask"/> bit and the window can never be observed. The
        /// marker still emits its pulse normally; only the duration is inert.
        /// </summary>
        V20 = 20,

        /// <summary>
        /// Error: a <see cref="BillboardRootDefinition"/> addresses a node that does not exist
        /// (amendment A44) — a <see cref="RigNodeAddressKind.RigTarget"/> id that is not a target
        /// of this rig, or a <see cref="RigNodeAddressKind.HierarchyPath"/> with no path.
        /// </summary>
        /// <remarks>
        /// An unresolved root is silent damage: it bakes nothing, so the node it was meant to turn
        /// simply does not billboard, and a rig that looks configured behaves as though it were not.
        /// Reported at authoring time, where the address can still be fixed against the rig in front
        /// of the author, rather than left for the entity bake to discover.
        /// </remarks>
        V21 = 21,

        /// <summary>
        /// Error: two <see cref="BillboardRootDefinition"/> rows address the same node
        /// (amendment A44).
        /// </summary>
        /// <remarks>
        /// Two roots on one node have no defined resolution — each would cancel the other's rotation
        /// as though it were an ancestor's, and which won would depend on list order, which is not
        /// identity for anything else on this rig. The duplicate-identity shape mirrors V05, scoped
        /// to the addressed node rather than to the row's own id.
        /// </remarks>
        V22 = 22,

        /// <summary>
        /// Error: a <see cref="BillboardMode.AxisConstrained"/> root's <c>constraintAxis</c> is
        /// zero-length (amendment A44), so there is no axis to turn about.
        /// </summary>
        /// <remarks>
        /// Reported rather than defaulted to world Y. A silent default is how half the callers stop
        /// honouring a mode they opted into — the <c>ClipSampler.CompositeLayers</c> precedent,
        /// amendment A34 — and an author who chose <c>AxisConstrained</c> and left the axis empty
        /// wanted an axis, not upright.
        /// </remarks>
        V23 = 23,

        /// <summary>
        /// Error: a <see cref="BillboardTrack"/>'s <c>rootStableId</c> is not a billboard root of
        /// the clip's rig (amendment A44).
        /// </summary>
        /// <remarks>
        /// V02's shape, scoped to billboard roots instead of rig targets. It is a separate code
        /// rather than a second case of V02 because the two address different id spaces, and a
        /// reader chasing "V02: unknown target" against a track that names no target at all would be
        /// looking in the wrong list.
        /// </remarks>
        V24 = 24,

        /// <summary>
        /// Error: a <see cref="RigAsset.billboardRoots"/> row carries a
        /// <see cref="RigNodeAddressKind.Bone"/> address (Phase D ragdoll spec, rule V-R8).
        /// </summary>
        /// <remarks>
        /// Billboarding has no bone path: a billboard root turns a node's transform, and a bone has
        /// no transform of its own to write — only the VAT bake samples it, into a texel, never into
        /// a facing. The <see cref="RigNodeAddressKind.Bone"/> kind exists for the ragdoll body list
        /// that shares this address struct, not for billboard roots, and a rig row that authors one
        /// anyway would bake a billboard that silently does nothing.
        /// </remarks>
        V25 = 25,

        /// <summary>
        /// Error: a <see cref="RagdollBodyDefinition"/>'s <c>address</c> names a
        /// <see cref="RigNodeAddressKind.RigTarget"/> id that is not a target of this rig (Phase D
        /// ragdoll spec, rule V-R1).
        /// </summary>
        /// <remarks>
        /// Only the <see cref="RigNodeAddressKind.RigTarget"/> half is reachable here, for exactly
        /// the reason V21 is only half-reachable for billboard roots: a
        /// <see cref="RigNodeAddressKind.HierarchyPath"/> or <see cref="RigNodeAddressKind.Bone"/>
        /// address names something outside this asset, and a <see cref="RigAsset"/> neither
        /// references a prefab nor carries a hierarchy of its own. Resolving those two kinds needs
        /// the actual prefab, which only the entity bake (Phase D3) holds - a future ragdoll-body
        /// resolver in that phase reports an unresolved path or bone the same way
        /// <c>BillboardRootResolver</c> already does for V21.
        /// </remarks>
        V26 = 26,

        /// <summary>
        /// Error: a <see cref="RagdollBodyDefinition"/>'s <c>stableId</c> is left at the reserved 0
        /// value, or duplicates another body's id, within one rig (Phase D ragdoll spec, rule V-R2).
        /// </summary>
        V27 = 27,

        /// <summary>
        /// Error: two <see cref="RagdollBodyDefinition"/> rows address the same node (Phase D
        /// ragdoll spec, rule V-R3).
        /// </summary>
        /// <remarks>
        /// Two bodies on one node have no defined resolution - the implied-parent hierarchy (spec
        /// section 3.3) cannot place one body relative to another that shares its own node, and the
        /// self-collision exclusion between "parent" and "child" would not know which pair it was
        /// looking at. The duplicate-identity shape mirrors V22's, scoped to a node that can carry a
        /// ragdoll body instead of a billboard root.
        /// </remarks>
        V28 = 28,

        /// <summary>
        /// Error: a <see cref="RagdollBodyDefinition"/>'s <c>boxSize</c> has a component that is not
        /// greater than 0 (Phase D ragdoll spec, rule V-R4). A collider with a zero or negative
        /// extent has no volume for the bake's inertia derivation to work from.
        /// </summary>
        V29 = 29,

        /// <summary>
        /// Error: a <see cref="RagdollBodyDefinition"/>'s joint limits are out of range (Phase D
        /// ragdoll spec, rule V-R5) - <c>limitMinDegrees</c> exceeds <c>limitMaxDegrees</c>, either
        /// one leaves [-180, 180], or <c>swingLimitDegrees</c> / <c>twistLimitDegrees</c> leaves
        /// [0, 180].
        /// </summary>
        V30 = 30,

        /// <summary>
        /// Warning: this rig's ragdoll bodies do not form a single tree (Phase D ragdoll spec, rule
        /// V-R6) - among the bodies whose place in the hierarchy this asset can itself confirm, more
        /// or fewer than exactly one has no ragdolled ancestor.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A warning rather than an error on purpose (spec section 4): two disconnected
        /// articulations on one rig is odd but simulable, and a rig mid-authoring passes through
        /// that state on the way to a finished one.
        /// </para>
        /// <para>
        /// <strong>Only <see cref="RigNodeAddressKind.HierarchyPath"/> bodies are checked.</strong>
        /// A path's ancestry is a fact the rig asset can verify on its own, by string prefix, with
        /// no prefab in hand. A <see cref="RigNodeAddressKind.RigTarget"/> address carries no
        /// path - <c>RigTargetDefinition.sourceNodePath</c> is an unvalidated editor convenience,
        /// never a source of truth, so treating it as one here would be reporting a fact this asset
        /// cannot actually back up - and a <see cref="RigNodeAddressKind.Bone"/> address lives in an
        /// armature this asset does not reference at all. A rig whose bodies are entirely
        /// target- or bone-addressed therefore never trips this rule at authoring time; the fully
        /// accurate check needs the real prefab, the same gap V-R1/V26 documents for address
        /// resolution.
        /// </para>
        /// <para>
        /// A body whose node is already reported as a V-R3/V28 duplicate is excluded from this
        /// count. Two bodies sharing one path are one mistake, not a second, unrelated-looking claim
        /// that the tree is also disconnected.
        /// </para>
        /// </remarks>
        V31 = 31,

        /// <summary>
        /// Error: a <see cref="RagdollBodyDefinition"/>'s <c>mass</c> is not greater than 0 (Phase D
        /// ragdoll spec, rule V-R7). A zero or negative mass has no closed-form inertia tensor.
        /// </summary>
        V32 = 32,

        /// <summary>
        /// Error: a <see cref="TargetTagEntry"/>'s <c>stableId</c> is left at the reserved
        /// 0 value, or duplicates another entry's id, within one
        /// <see cref="TargetTagRegistry"/> (Phase E target-tags spec §6, rule T5).
        /// </summary>
        /// <remarks>
        /// Mirrors V05's and V27's duplicate-identity shape, scoped to a tag registry's own rows
        /// instead of a rig's targets or ragdoll bodies. A zero or colliding tag id is not merely
        /// cosmetic here: E2 and E3 will let a rig target and a clip track reference a tag by this
        /// id alone, so an ambiguous id in the registry would make every consumer's resolution
        /// ambiguous too.
        /// </remarks>
        V33 = 33,

        /// <summary>
        /// Error: two <see cref="RigTargetDefinition"/> rows in one rig carry the same non-zero
        /// <c>tagId</c> (Phase E target-tags spec §6, rule T1).
        /// </summary>
        /// <remarks>
        /// A tag identifies a <em>role</em> a rig target plays, and once E3 lands a track can bind
        /// that role instead of a specific target's own id — the bake resolves it by finding "the
        /// target in this rig carrying this tag" (spec §5). Two targets sharing a tag makes that
        /// resolution ambiguous: there is no rule for which of the two a tag-bound track should
        /// animate, and picking one by list order would make the answer depend on authoring order
        /// rather than anything a person chose. The reserved 0 ("untagged") value is exempt by
        /// definition — every rig is expected to carry many untagged targets, and treating that as a
        /// collision would make T1 fire on almost every rig in the project.
        /// </remarks>
        V34 = 34,

        /// <summary>
        /// Warning: a <see cref="TransformTrack"/> or <see cref="SpriteTrack"/> binds a tag that no
        /// target of the rig being checked against carries (Phase E target-tags spec §6, rule T2).
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>Deliberately a warning, not an error — this is the owner decision recorded in
        /// spec §6.1, and it is the entire reason tag-bound tracks are useful.</strong> One
        /// "reactions" clip can cover a roster whose rigs genuinely differ: a blink tagging
        /// <c>EyeL</c>/<c>EyeR</c> plays on a character that has eyes and is simply ignored by a
        /// barrel that does not. The bake skips the track and moves on (spec §5, point 3) rather
        /// than failing the whole clip set over a part that was never expected to be there.
        /// </para>
        /// <para>
        /// <strong>Kept apart from V36 (T3) on purpose.</strong> A tag missing from <em>this rig</em>
        /// is an ordinary fact about a roster; a tag missing from <em>the registry entirely</em> is a
        /// dangling reference, broken against every rig it meets. Reporting them the same way would
        /// bury the second inside the noise of the first (spec §6.1).
        /// </para>
        /// <para>
        /// Spec §6.1 makes the message text itself part of the rule: it must name the clip, the
        /// track, the tag's name, and the rig, so the finding is actionable without opening
        /// anything, and it must surface in the Clip Editor's validation badge — not only the bake
        /// console — since this is expected to be the most common finding this feature produces.
        /// </para>
        /// </remarks>
        V35 = 35,

        /// <summary>
        /// Error: a <see cref="TransformTrack"/> or <see cref="SpriteTrack"/> binds a tag id that no
        /// longer exists in the <see cref="TargetTagRegistry"/> supplied to validation — a deleted
        /// tag (Phase E target-tags spec §6, rule T3).
        /// </summary>
        /// <remarks>
        /// Unlike V35 (T2), this is not about whether the tag applies to the rig at hand — it is
        /// about whether the tag exists <em>anywhere</em> any more. A track cannot recover from a
        /// deleted tag by trying a different rig, which is why this stays an error even though V35
        /// is lenient (spec §6.1). Only checked when a registry is supplied to validation; without
        /// one, a track's unresolved tag cannot be told apart from V35's ordinary case and is
        /// reported as that instead, never silently passed.
        /// </remarks>
        V36 = 36,

        /// <summary>
        /// Warning: a <see cref="ClipAsset"/> referenced by more than one <see cref="ClipSetAsset"/>
        /// still carries at least one <see cref="TransformTrack"/> or <see cref="SpriteTrack"/> bound
        /// by target id rather than by tag (Phase E target-tags spec §6, rule T4).
        /// </summary>
        /// <remarks>
        /// <strong>This is the rule that earns the feature.</strong> A target id is random and unique
        /// to the rig it was minted in (architecture section 3.4), so a target-id-bound track shared
        /// into a second set's rig resolves to nothing there — not an error, not a warning at the
        /// track itself, just an animation that quietly never plays on the second character. T4 turns
        /// that silent mystery into a message at authoring time, on the clip that is actually shared,
        /// before anyone has to notice the missing motion on screen (spec §6).
        /// </remarks>
        V37 = 37
    }

    /// <summary>
    /// Which caller is validating (architecture section 3.5). The stage only changes the severity of
    /// rule V08: a stale VAT bake blocks authoring but not entity baking, where the outdated
    /// textures still render.
    /// </summary>
    public enum ValidationStage : byte
    {
        /// <summary>Inspectors and the clip editor: every rule reports at its table severity.</summary>
        Authoring = 0,

        /// <summary>Entity baking: rule V08 reports as a warning instead of an error.</summary>
        Bake = 1
    }

    /// <summary>
    /// One validation finding (architecture section 3.5): what rule fired, how serious it is, which
    /// asset it concerns, and a human-readable explanation.
    /// </summary>
    public readonly struct ValidationMessage
    {
        /// <summary>How serious this finding is.</summary>
        public readonly ValidationSeverity severity;

        /// <summary>Which architecture section 3.5 rule produced it.</summary>
        public readonly ValidationCode code;

        /// <summary>
        /// The asset the finding concerns, for click-to-select in the inspector and for log context.
        /// May be null when the offending reference is itself missing.
        /// </summary>
        public readonly UnityEngine.Object assetContext;

        /// <summary>Human-readable explanation naming the offending data.</summary>
        public readonly string text;

        /// <summary>Creates a validation finding.</summary>
        /// <param name="severity">How serious the finding is.</param>
        /// <param name="code">The architecture section 3.5 rule that fired.</param>
        /// <param name="assetContext">The asset the finding concerns; may be null.</param>
        /// <param name="text">Human-readable explanation.</param>
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

        /// <summary>
        /// Renders the finding as "<c>V07 (Error): text</c>" for logs and exception messages.
        /// </summary>
        /// <returns>The formatted finding.</returns>
        public override string ToString()
        {
            return code.ToString() + " (" + severity.ToString() + "): " + text;
        }
    }
}
