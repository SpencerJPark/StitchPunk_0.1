using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class SensedEntityAuthoring : MonoBehaviour
{
    public class Baker : Baker<SensedEntityAuthoring>
    {
        public override void Bake(SensedEntityAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddBuffer<SensedEntity>(entity);
        }
    }
}

public struct SensedEntity : IBufferElementData
{
    public Entity entity;
    public InteractableType type;
    public float distance;
    public float3 position;
}