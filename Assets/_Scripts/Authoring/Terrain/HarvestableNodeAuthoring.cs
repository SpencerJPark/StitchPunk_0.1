using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

// Place on a tree/node inside a tile subscene next to ResourceNodeAuthoring; the harvested-node ledger keys off the baked position.
public class HarvestableNodeAuthoring : MonoBehaviour
{
    [Tooltip("Game days until a harvested node grows back. 0 = never respawns.")]
    [Min(0f)] public float respawnAfterGameDays = 3f;

    [BakingVersion("stitchpunk", 1)]
    public class Baker : Baker<HarvestableNodeAuthoring>
    {
        public override void Bake(HarvestableNodeAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            Vector3 position = authoring.transform.position;
            ResourceNodeAuthoring resourceNodeAuthoring = GetComponent<ResourceNodeAuthoring>();
            int fullAmount = 0;
            int resourceTypeIndex = 0;
            if (resourceNodeAuthoring != null)
            {
                fullAmount = resourceNodeAuthoring.amount;
                resourceTypeIndex = (int)resourceNodeAuthoring.resourceType;
            }

            AddComponent(entity, new HarvestableNodeKey
            {
                nodeKey = math.hash(new int4(
                    (int)math.round(position.x * 10f), (int)math.round(position.y * 10f), (int)math.round(position.z * 10f), resourceTypeIndex)),
                fullAmount = fullAmount,
                respawnAfterGameDays = authoring.respawnAfterGameDays
            });
            AddComponent<HarvestedNodeStump>(entity);
            SetComponentEnabled<HarvestedNodeStump>(entity, false);
        }
    }
}
