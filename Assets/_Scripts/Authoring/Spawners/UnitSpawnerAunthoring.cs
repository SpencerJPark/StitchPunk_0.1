using Unity.Entities;
using UnityEngine;

public class UnitSpawnerAuthoring : MonoBehaviour
{
    public UnitType unitType;
    public int spawnCount;
    public float range;

    [BakingVersion("stitchpunk", 1)]
    public class Baker : Baker<UnitSpawnerAuthoring>
    {
        public override void Bake(UnitSpawnerAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            
            AddComponent(entity, new UnitSpawner
            {
                unitType = authoring.unitType,
                spawnCount = authoring.spawnCount,
                range = authoring.range,
            });
            SetComponentEnabled<UnitSpawner>(entity, true);
        }
    }
}