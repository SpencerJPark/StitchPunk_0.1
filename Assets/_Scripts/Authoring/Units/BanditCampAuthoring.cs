using Unity.Entities;
using UnityEngine;

public class BanditCampAuthoring : MonoBehaviour
{
    [BakingVersion("stitchpunk", 1)]
    public class Baker : Baker<BanditCampAuthoring>
    {
        public override void Bake(BanditCampAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new BanditCamp { aliveGuards = 0, cleared = false });
        }
    }
}
