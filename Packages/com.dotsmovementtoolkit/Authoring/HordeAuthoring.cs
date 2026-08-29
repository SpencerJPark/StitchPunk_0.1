using DotsMovementToolkit;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace DotsMovementToolkit.Authoring
{
    public class HordeAuthoring : MonoBehaviour
    {
        [Header("Horde Settings")]
        [Tooltip("Initial target position (can be changed at runtime)")]
        public Vector3 initialTarget;

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
                });

                AddBuffer<HordeMemberBuffer>(entity);
            }
        }
    }
}
