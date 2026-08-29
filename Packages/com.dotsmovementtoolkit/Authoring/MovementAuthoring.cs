using DotsMovementToolkit;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace DotsMovementToolkit.Authoring
{
    // Adds the Movement component. For units with a brain authoring these values are
    // OVERRIDDEN from their UnitSO by UnitSpeedBakingSystem — UnitSO is the source of truth.
    // Values here are authoritative only for non-brain units (player, BaseUnit template).
    public class MovementAuthoring : MonoBehaviour
    {
        [Header("Movement (overridden by UnitSO on brain units)")]
        public float moveSpeed = 5f;
        public float runSpeed = 9f;
        public float rotationSpeed = 10f;

        public class Baker : Baker<MovementAuthoring>
        {
            public override void Bake(MovementAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(entity, new Movement
                {
                    moveSpeed              = authoring.moveSpeed,
                    runSpeed               = authoring.runSpeed,
                    rotationSpeed          = authoring.rotationSpeed,
                    targetPosition         = float3.zero,
                    isMoving               = false,
                    isRunning              = false,
                });
                AddComponent(entity, new SetupUnitMoverDefaultPosition());
            }
        }
    }
}
