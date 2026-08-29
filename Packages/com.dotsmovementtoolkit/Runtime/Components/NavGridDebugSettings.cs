using Unity.Entities;
using Unity.Mathematics;

namespace DotsMovementToolkit
{
    public enum NavGridDebugDisplayMode : byte
    {
        Off = 0,

        /// <summary>Only cells the cost map marks as more expensive than defaultCost — walls and heavy terrain.</summary>
        ObstaclesOnly = 1,

        /// <summary>Every cell, tinted by its cost. Bounded by NavGridDebugSettings.maxDrawnCells.</summary>
        FullGrid = 2,
    }

    // Baked by NavGridAuthoring alongside NavGridSettings. Absent (or Off) means
    // NavGridDebugRenderSystem builds nothing and allocates nothing.
    public struct NavGridDebugSettings : IComponentData
    {
        public NavGridDebugDisplayMode displayMode;

        /// <summary>Which grid layer to draw; -1 draws every layer at its own world height.</summary>
        public int layerToDraw;

        /// <summary>World-space lift above the layer's floor, so the tiles don't z-fight with the ground.</summary>
        public float heightOffset;

        /// <summary>0..0.45 of a cell, shrunk from every edge — the gap that makes individual tiles readable.</summary>
        public float cellPadding;

        public bool drawCellOutlines;

        /// <summary>How long a cell keeps the "just changed" tint after a cost-map rebuild. 0 disables change tracking.</summary>
        public float changeHighlightSeconds;

        /// <summary>Hard ceiling on drawn cells. Exceeding it downgrades FullGrid to ObstaclesOnly rather than stalling the editor.</summary>
        public int maxDrawnCells;

        public float4 walkableColor;
        public float4 discouragedColor;
        public float4 blockedColor;
        public float4 recentlyChangedColor;
    }
}
