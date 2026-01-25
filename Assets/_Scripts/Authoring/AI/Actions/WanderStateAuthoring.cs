using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class WanderStateAuthoring : MonoBehaviour
{
    public float wanderRadius = 10f;

    public class Baker : Baker<WanderStateAuthoring>
    {
        public override void Bake(WanderStateAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<WanderState>(entity, new WanderState
            {
                wanderRadius = authoring.wanderRadius,
                wanderTarget = float3.zero
            });
        }
    }
}

public struct WanderState : IComponentData
{
    public float wanderRadius;
    public float3 wanderTarget;
}