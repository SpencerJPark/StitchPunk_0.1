// Copyright (c) 2026 Stitch Punk. All rights reserved.

namespace StitchPunk.AnimationToolkit.Authoring
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
        V20 = 20
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
