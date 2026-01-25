using Unity.Entities;
using UnityEngine;

public class CitizenBrainAuthoring : MonoBehaviour
{
    public float wanderRadius = 10f;

    public class Baker : Baker<CitizenBrainAuthoring>
    {
        public override void Bake(CitizenBrainAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent<CitizenBrain>(entity);

            // Base actions all citizens can do
            AddComponent<CanEat>(entity);
            AddComponent<CanSleep>(entity);
            AddComponent<CanSocialize>(entity);
            AddComponent<CanWander>(entity);

            AddComponent<WanderState>(entity, new WanderState
            {
                wanderRadius = authoring.wanderRadius,
                wanderTarget = Unity.Mathematics.float3.zero
            });
        }
    }
}

public struct CitizenBrain : IComponentData { }