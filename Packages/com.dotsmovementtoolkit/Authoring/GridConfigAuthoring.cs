using DotsMovementToolkit;
using Unity.Entities;
using UnityEngine;

namespace DotsMovementToolkit.Authoring
{
    // Bakes the MovementGridSettings singleton the whole toolkit gates on. One per project —
    // add to a subscene alongside the rest of the game's baked config.
    public class GridConfigAuthoring : MonoBehaviour
    {
        [Header("Grid")]
        public int width = 100;
        public int height = 100;
        public int layerCount = 1;
        public float cellSize = 2f;
        public float layerHeight = 3f;

        [Header("Physics Layers")]
        public LayerMask wallLayerMask;
        public LayerMask heavyLayerMask;
        public LayerMask groundLayerMask;

        [Header("Costs")]
        public byte wallCost = byte.MaxValue;
        public byte heavyCost = 50;
        public byte defaultCost = 1;

        public class Baker : Baker<GridConfigAuthoring>
        {
            public override void Bake(GridConfigAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new MovementGridSettings
                {
                    width = authoring.width,
                    height = authoring.height,
                    layerCount = authoring.layerCount,
                    cellSize = authoring.cellSize,
                    layerHeight = authoring.layerHeight,
                    wallLayerMask = (uint)(int)authoring.wallLayerMask,
                    heavyLayerMask = (uint)(int)authoring.heavyLayerMask,
                    groundLayerMask = (uint)(int)authoring.groundLayerMask,
                    wallCost = authoring.wallCost,
                    heavyCost = authoring.heavyCost,
                    defaultCost = authoring.defaultCost,
                });
            }
        }
    }
}
