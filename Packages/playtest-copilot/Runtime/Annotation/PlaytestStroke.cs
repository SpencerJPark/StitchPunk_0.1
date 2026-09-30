using System.Collections.Generic;
using UnityEngine;

namespace PlaytestCopilot
{
    /// One drawn stroke from the annotation overlay. Points are bottom-left-origin
    /// game-view pixels; the overlay does the IMGUI y-flip before building this.
    public sealed class PlaytestStroke
    {
        public PlaytestAnnotationTool Tool;
        public List<Vector2> ScreenPoints = new List<Vector2>();
        public Color Color = Color.white;
        public int ThicknessPixels = 3;
    }
}
