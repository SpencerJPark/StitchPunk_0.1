// Copyright (c) 2026 Spencer Park. All rights reserved.

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Which of the Clip Editor's four views is showing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One enum rather than a bool per pane, because exactly one is true and four bools that must
    /// sum to one is the shape that goes wrong — two lit tabs over one pane, or none lit over the
    /// dock, with nothing in the type system objecting.
    /// </para>
    /// <para>
    /// <see cref="ClipEditor"/> is the absence of a cover pane, not a pane of its own: the dock is
    /// what the other three are drawn over, so returning to it is hiding all three.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// The values are display order — the tab bar is built by walking them — so inserting a member
    /// moves the ones after it. That is fine and deliberate: nothing persists these numbers beyond
    /// one session (<c>sessionTab</c> across a domain reload, <c>CarriedState.tab</c> across a
    /// re-dock), so the worst an insertion costs is one restore landing on the neighbouring tab, in
    /// the single session that spans the change.
    /// </remarks>
    public enum ClipEditorTab
    {
        /// <summary>The dock — clip list, hierarchy, viewport, inspector and timeline.</summary>
        ClipEditor = 0,

        /// <summary>The cutscene editor. A placeholder — the pane says so and holds nothing yet.</summary>
        CutsceneEditor = 1,

        /// <summary>The New Rig creation flow.</summary>
        NewRig = 2,

        /// <summary>The 2D Direction Sets authoring pane.</summary>
        DirectionSets = 3,

        /// <summary>The VAT bake settings.</summary>
        VatBake = 4
    }
}
