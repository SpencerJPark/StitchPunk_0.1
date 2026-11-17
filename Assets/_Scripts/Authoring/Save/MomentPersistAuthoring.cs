using Unity.Entities;
using UnityEngine;

// Opts this prefab's runtime instances (and their LinkedEntityGroup) into the exact-moment save snapshot.
[DisallowMultipleComponent]
public class MomentPersistAuthoring : MonoBehaviour
{
    [BakingVersion("stitchpunk", 1)]
    public class Baker : Baker<MomentPersistAuthoring>
    {
        public override void Bake(MomentPersistAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<MomentPersist>(entity);
        }
    }
}
