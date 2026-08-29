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
    public enum ClipEditorTab
    {
        /// <summary>The dock — clip list, hierarchy, viewport, inspector and timeline.</summary>
        ClipEditor = 0,

        /// <summary>The New Rig creation flow.</summary>
        NewRig = 1,

        /// <summary>The 2D Direction Sets authoring pane.</summary>
        DirectionSets = 2,

        /// <summary>The VAT bake settings.</summary>
        VatBake = 3
    }
}
