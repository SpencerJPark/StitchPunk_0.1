// Copyright (c) 2026 Spencer Park. All rights reserved.

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Which of the Clip Editor's views is showing. Values are display order — the tab bar is
    /// bound in this order, and nothing persists these numbers beyond one session.
    /// </summary>
    public enum ClipEditorTab
    {
        /// <summary>Pack greyscale images into one texture's channels over a node graph, with an image and recipe sidebar.</summary>
        TexturePacker = 0,

        SpriteSheets = 1,

        /// <summary>Browse, create and edit clip sets — which clips each one registers.</summary>
        ClipSets = 2,

        Rigs = 3,

        Events = 4,

        /// <summary>The dock — clip list, hierarchy, viewport, inspector and timeline.</summary>
        ClipEditor = 5,

        VatBake = 6,

        /// <summary>Layer/animation authoring over a composited multi-layer preview.</summary>
        ActorEditor = 7,

        CutsceneEditor = 8,

        Health = 9,

        Materials = 10,

        Retarget = 11,

        Capture = 12
    }
}
