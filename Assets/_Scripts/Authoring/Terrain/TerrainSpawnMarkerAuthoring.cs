using Unity.Entities;
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
            AddComponent(entity, new TerrainSpawnMarkerData
            {
                unitType = authoring.unitType,
                count = authoring.count,
                radiusMetres = authoring.radiusMetres,
                persistent = authoring.persistent,
                respawnAfterGameDays = authoring.respawnAfterGameDays
            });
        }
    }
}
