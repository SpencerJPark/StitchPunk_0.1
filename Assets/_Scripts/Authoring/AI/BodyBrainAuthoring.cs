using Unity.Entities;
using UnityEngine;

public class BodyBrainAuthoring : MonoBehaviour
{
    public GameObject brain;

    public class Baker : Baker<BodyBrainAuthoring>
    {
        public override void Bake(BodyBrainAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            Entity brainEntity = authoring.brain != null 
                ? GetEntity(authoring.brain, TransformUsageFlags.Dynamic) 
                : Entity.Null;

            AddComponent(entity, new BodyBrain { brain = brainEntity });
            
            if (brainEntity != Entity.Null)
                AddComponent<HasBrain>(entity);
        }
    }
}

public struct BodyBrain : IComponentData
{
    public Entity brain;
}

public struct HasBrain : IComponentData { }