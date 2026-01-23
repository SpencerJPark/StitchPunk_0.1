using Unity.Entities;
using UnityEngine;

public class ZombieBrainAuthoring : MonoBehaviour
{
    public float aggression = 1f;
    public float awareness = 0.3f;

    public class Baker : Baker<ZombieBrainAuthoring>
    {
        public override void Bake(ZombieBrainAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<ZombieBrain>(entity);
            AddComponent(entity, new ZombieState
            {
                aggression = authoring.aggression,
                awareness = authoring.awareness
            });
            AddComponent<CanAttack>(entity);
            AddComponent<CanWander>(entity);
        }
    }
}

public struct ZombieBrain : IComponentData { }

public struct ZombieState : IComponentData
{
    public float aggression;
    public float awareness;
}