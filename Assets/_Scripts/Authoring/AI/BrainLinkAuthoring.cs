using Unity.Entities;
using UnityEngine;

public class BrainLinkAuthoring : MonoBehaviour
{
    public GameObject brain;

    public class Baker : Baker<BrainLinkAuthoring>
    {
        public override void Bake(BrainLinkAuthoring linkAuthoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            Entity brainEntity = linkAuthoring.brain != null 
                ? GetEntity(linkAuthoring.brain, TransformUsageFlags.Dynamic) 
                : Entity.Null;

            AddComponent(entity, new BrainLink { brain = brainEntity });
            AddComponent<HasBrain>(entity);
        }
    }
}

