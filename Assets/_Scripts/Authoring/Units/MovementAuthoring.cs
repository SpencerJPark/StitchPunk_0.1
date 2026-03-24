using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class MovementAuthoring : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 5f;
    public float rotationSpeed = 10f;

    public class Baker : Baker<MovementAuthoring>
    {
        public override void Bake(MovementAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new Movement
            {
                moveSpeed              = authoring.moveSpeed,
                rotationSpeed          = authoring.rotationSpeed,
                targetPosition         = float3.zero,
                isMoving               = false,
            });
            AddComponent(entity, new SetupUnitMoverDefaultPosition());
        }
    }
}


