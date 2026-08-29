using Unity.Entities;

namespace DotsMovementToolkit
{
    // Baked by GridConfigAuthoring.Baker. The root MovementSystemGroup requires this singleton
    // to update at all: no config baked in the world means the whole toolkit idles — the
    // generic, game-agnostic replacement for a scene-gate tag.
    public struct MovementGridSettings : IComponentData
    {
        public int width;
        public int height;
        public int layerCount;
        public float cellSize;
        public float layerHeight;
        public uint wallLayerMask;
        public uint heavyLayerMask;
        public uint groundLayerMask;
        public byte wallCost;
        public byte heavyCost;
        public byte defaultCost;
    }
}
