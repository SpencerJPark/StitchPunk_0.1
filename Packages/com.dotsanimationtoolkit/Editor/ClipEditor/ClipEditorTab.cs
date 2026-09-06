// Copyright (c) 2026 Spencer Park. All rights reserved.

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Which of the Clip Editor's four views is showing. Values are display order — the tab bar
    /// is built by walking them, and nothing persists these numbers beyond one session.
    /// </summary>
    public enum ClipEditorTab
    {
        /// <summary>The dock — clip list, hierarchy, viewport, inspector and timeline.</summary>
        ClipEditor = 0,

        /// <summary>A placeholder — the pane says so and holds nothing yet.</summary>
        CutsceneEditor = 1,

        NewRig = 2,

        DirectionSets = 3,

        VatBake = 4
    }
}
