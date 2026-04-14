using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class FactoryGridAuthoring : MonoBehaviour
{
    public float cellSize = 1f;
    public int width  = 20;
    public int height = 20;

    public class Baker : Baker<FactoryGridAuthoring>
    {
        public override void Bake(FactoryGridAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);

            AddComponent(entity, new FactoryGridConfig
            {
                cellSize    = authoring.cellSize,
                width       = authoring.width,
                height      = authoring.height,
                worldOrigin = (float3)authoring.transform.position,
            });

            DynamicBuffer<FactoryGridCell> cells = AddBuffer<FactoryGridCell>(entity);
            int total = authoring.width * authoring.height;
            for (int i = 0; i < total; i++)
                cells.Add(new FactoryGridCell { occupant = Entity.Null });
        }
    }
}
