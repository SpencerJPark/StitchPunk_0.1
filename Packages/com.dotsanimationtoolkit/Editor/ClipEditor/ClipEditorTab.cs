// Copyright (c) 2026 Spencer Park. All rights reserved.

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Which of the Clip Editor's views is showing. Values are display order — the tab bar is
    /// bound in this order, and nothing persists these numbers beyond one session.
    /// </summary>
    public enum ClipEditorTab
    {
        Rigs = 0,

        /// <summary>Browse, create and edit clip sets — which clips each one registers.</summary>
        ClipSets = 1,

        /// <summary>The dock — clip list, hierarchy, viewport, inspector and timeline.</summary>
        ClipEditor = 2,

        VatBake = 3,

        /// <summary>Layer/animation authoring over a composited multi-layer preview.</summary>
        ActorEditor = 4,

        CutsceneEditor = 5
    }
}
