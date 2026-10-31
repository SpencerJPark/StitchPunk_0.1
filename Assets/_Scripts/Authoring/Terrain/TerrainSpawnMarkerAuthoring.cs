using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

// Place inside a tile subscene: the terrain spawn system reads the baked data when that tile loads.
public class TerrainSpawnMarkerAuthoring : MonoBehaviour
{
    public UnitType unitType;
    [Min(1)] public int count = 1;
    [Min(0f)] public float radiusMetres = 5f;
    public bool persistent;
    [Min(0f)] public float respawnAfterGameDays;

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = persistent ? Color.cyan : Color.yellow;
        Gizmos.DrawWireSphere(transform.position, radiusMetres);
    }

    public class Baker : Baker<TerrainSpawnMarkerAuthoring>
    {
        public override void Bake(TerrainSpawnMarkerAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            Vector3 position = authoring.transform.position;
            AddComponent(entity, new TerrainSpawnMarkerData
            {
                unitType = authoring.unitType,
                count = authoring.count,
                radiusMetres = authoring.radiusMetres,
                persistent = authoring.persistent,
                respawnAfterGameDays = authoring.respawnAfterGameDays,
                markerKey = math.hash(new int4(
                    (int)math.round(position.x * 10f), (int)math.round(position.y * 10f), (int)math.round(position.z * 10f), (int)authoring.unitType))
            });
            AddComponent(entity, new UnitSpawner { unitType = authoring.unitType, spawnCount = authoring.count, range = authoring.radiusMetres });
            SetComponentEnabled<UnitSpawner>(entity, false);
            AddComponent<TerrainSpawnMarkerHandled>(entity);
            SetComponentEnabled<TerrainSpawnMarkerHandled>(entity, false);
        }
    }
}
