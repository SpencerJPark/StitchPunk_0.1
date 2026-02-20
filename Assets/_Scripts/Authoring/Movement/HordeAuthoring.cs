using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class HordeAuthoring : MonoBehaviour
{
    [Header("Horde Settings")]
    [Tooltip("Initial target position (can be changed at runtime)")]
    public Vector3 initialTarget;
    
    [Tooltip("Custom behavior flags for AI decisions")]
    public int behaviorFlags = 0;

    public class Baker : Baker<HordeAuthoring>
    {
        public override void Bake(HordeAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            
            AddComponent(entity, new Horde
            {
                hordeId = 0, // Will be assigned by HordeSystem
                targetPosition = authoring.initialTarget,
                targetEntity = Entity.Null,
                flowFieldIndex = -1,
                memberCount = 0,
                isActive = true,
                needsPathUpdate = true,
                behaviorFlags = authoring.behaviorFlags
            });
            
            AddBuffer<HordeMemberBuffer>(entity);
        }
    }
}

public struct Horde : IComponentData
{
    public int hordeId;
    public float3 targetPosition;
    public Entity targetEntity;
    public int flowFieldIndex;
    public int memberCount;
    public bool isActive;
    public bool needsPathUpdate;
    public int behaviorFlags;
}

[InternalBufferCapacity(16)]
public struct HordeMemberBuffer : IBufferElementData
{
    public Entity memberEntity;
}