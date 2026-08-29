using Unity.Entities;
using Unity.Mathematics;

namespace DotsMovementToolkit
{
    // Baked by NavGridAuthoring.Baker. The root MovementSystemGroup requires this singleton
    // to update at all: no config baked in the world means the whole toolkit idles — the
    // generic, game-agnostic replacement for a scene-gate tag.
    public struct NavGridSettings : IComponentData
    {
        public int width;
        public int height;
        public int layerCount;
        public float cellSize;
        public float layerHeight;

        // World position of cell (0,0)'s corner. NavGridAuthoring derives it from its own transform,
        // optionally centring the footprint on it rather than extending only into +X/+Z.
        public float3 gridOrigin;

        public uint wallLayerMask;
        public uint heavyLayerMask;
        public uint groundLayerMask;
        public byte wallCost;
        public byte heavyCost;
        public byte defaultCost;
    }
}
