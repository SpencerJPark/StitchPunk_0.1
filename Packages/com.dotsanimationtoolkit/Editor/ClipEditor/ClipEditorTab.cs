// Copyright (c) 2026 Spencer Park. All rights reserved.

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Which of the Clip Editor's views is showing. Values are display order — the tab bar is
    /// bound in this order, and nothing persists these numbers beyond one session.
    /// </summary>
    public enum ClipEditorTab
    {
        NewRig = 0,

        /// <summary>The dock — clip list, hierarchy, viewport, inspector and timeline.</summary>
        ClipEditor = 1,

        VatBake = 2,

        /// <summary>
        /// 2D facing coverage today. Owner intent (2026-09-07): this absorbs layer authoring
        /// later and becomes "Actor Editor" in full, not just in the tab label.
        /// </summary>
        DirectionSets = 3,

        CutsceneEditor = 4
    }
}
