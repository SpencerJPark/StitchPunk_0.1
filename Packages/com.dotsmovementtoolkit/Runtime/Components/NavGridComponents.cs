using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace DotsMovementToolkit
{
    // Runtime nav-grid state, all created by NavGridSystem on its own SystemHandle entity
    // (which is why every consumer reaches them through GetSingleton). NavGridSettings is the
    // baked authoring input; these three are the derived runtime products.

    /// <summary>Resolved grid dimensions every pathfinding system reads. Derived from NavGridSettings at first update.</summary>
    public struct NavGridConfig : IComponentData
    {
        public int width;
        public int height;
        public int layerCount;
        public float cellSize;
        public float layerHeight; // Vertical distance between layers

        /// <summary>
        /// World position of cell (0,0)'s corner, and of layer 0's floor. The grid extends into
        /// +X/+Z from here, so a grid centred on the authoring transform has a negative origin.
        /// </summary>
        public float3 gridOrigin;
    }

    /// <summary>
    /// Shared per-cell traversal cost for all layers. Layout: [layer * (width * height) + cellIndex].
    /// </summary>
    public struct NavGridCostMap : IComponentData
    {
        public NativeArray<byte> costs;

        // Bumped by NavGridSystem every time the cost map is rebuilt from physics. Consumers that
        // cache anything derived from costs (the debug renderer's mesh) compare against this
        // instead of diffing 10k+ bytes per frame.
        public uint costMapVersion;
    }

    /// <summary>Stair/portal connection between two layers.</summary>
    public struct NavGridStairConnection : IBufferElementData
    {
        public int2 gridPosition;
        public int fromLayer;
        public int toLayer;
        public float3 entryWorldPosition;
        public float3 exitWorldPosition;
        public bool bidirectional;
    }
}
