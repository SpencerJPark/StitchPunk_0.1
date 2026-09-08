// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// The one set of colours every Clip Editor tab draws from, mirrored in
    /// ClipEditorWindow.uss as matching --toolkit-color-* custom properties.
    /// </summary>
    public static class ToolkitPalette
    {
        public static readonly Color Accent = new Color32(88, 148, 216, 255);
        public static readonly Color Selected = new Color32(77, 158, 242, 255);
        public static readonly Color SelectedRow = new Color32(61, 96, 133, 128);
        public static readonly Color Playing = new Color32(70, 130, 90, 255);
        public static readonly Color LoopOn = new Color32(150, 128, 52, 255);
        public static readonly Color Recording = new Color32(190, 70, 60, 255);
        public static readonly Color Holding = new Color32(242, 217, 77, 255);
        public static readonly Color Warning = new Color32(235, 170, 60, 255);
        public static readonly Color Error = new Color32(230, 90, 82, 255);
        public static readonly Color Clean = new Color32(115, 200, 122, 255);
        public static readonly Color RigEdit = new Color32(226, 138, 44, 255);
        public static readonly Color BoxBorder = new Color32(255, 255, 255, 20);
        public static readonly Color BoxFill = new Color32(255, 255, 255, 5);
        public static readonly Color BoxHeader = new Color32(255, 255, 255, 10);

        public static readonly Color LaneActor = new Color32(88, 148, 216, 255);
        public static readonly Color LaneProp = new Color32(120, 190, 120, 255);
        public static readonly Color LaneCamera = new Color32(90, 190, 190, 255);
        public static readonly Color LaneEvents = new Color32(230, 150, 70, 255);
        public static readonly Color LaneHolds = new Color32(230, 200, 80, 255);

        public static readonly Color MarkerRoot = new Color32(166, 217, 140, 255);
        public static readonly Color MarkerMark = new Color32(115, 166, 242, 255);
        public static readonly Color MarkerFacing = new Color32(217, 191, 102, 255);
        public static readonly Color MarkerPart = new Color32(191, 140, 217, 255);
        public static readonly Color MarkerAttach = new Color32(115, 204, 204, 255);
        public static readonly Color MarkerCamera = new Color32(140, 179, 242, 255);
        public static readonly Color MarkerCut = new Color32(242, 115, 115, 255);
        public static readonly Color MarkerHold = new Color32(242, 217, 77, 255);

        public static readonly Color[] EventColors = new Color[]
        {
            new Color32(235, 170, 60, 255),
            new Color32(235, 110, 90, 255),
            new Color32(215, 110, 190, 255),
            new Color32(150, 120, 230, 255),
            new Color32(70, 190, 180, 255),
            new Color32(160, 205, 80, 255),
            new Color32(240, 150, 170, 255),
            new Color32(120, 220, 170, 255),
        };

        public const float EventWindowAlpha = 0.30f;

        private static readonly Dictionary<string, Color> TokensBacking = new Dictionary<string, Color>
        {
            { "accent", Accent },
            { "selected", Selected },
            { "selected-row", SelectedRow },
            { "playing", Playing },
            { "loop-on", LoopOn },
            { "recording", Recording },
            { "holding", Holding },
            { "warning", Warning },
            { "error", Error },
            { "clean", Clean },
            { "rig-edit", RigEdit },
            { "box-border", BoxBorder },
            { "box-fill", BoxFill },
            { "box-header", BoxHeader },
            { "lane-actor", LaneActor },
            { "lane-prop", LaneProp },
            { "lane-camera", LaneCamera },
            { "lane-events", LaneEvents },
            { "lane-holds", LaneHolds },
            { "marker-root", MarkerRoot },
            { "marker-mark", MarkerMark },
            { "marker-facing", MarkerFacing },
            { "marker-part", MarkerPart },
            { "marker-attach", MarkerAttach },
            { "marker-camera", MarkerCamera },
            { "marker-cut", MarkerCut },
            { "marker-hold", MarkerHold },
            { "event-amber", EventColors[0] },
            { "event-coral", EventColors[1] },
            { "event-magenta", EventColors[2] },
            { "event-violet", EventColors[3] },
            { "event-teal", EventColors[4] },
            { "event-lime", EventColors[5] },
            { "event-pink", EventColors[6] },
            { "event-mint", EventColors[7] },
        };

        public static IReadOnlyDictionary<string, Color> Tokens
        {
            get { return TokensBacking; }
        }

        // Knuth's multiplicative hash on the top three bits: adjacent registry keys scatter
        // across the eight-entry table instead of marching through it in order.
        public static Color ColorForEventKey(uint eventKey)
        {
            unchecked
            {
                int paletteIndex = (int)((eventKey * 2654435761u) >> 29);
                return EventColors[paletteIndex];
            }
        }
    }
}
