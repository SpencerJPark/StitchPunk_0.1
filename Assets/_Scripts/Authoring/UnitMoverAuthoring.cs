using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class UnitMoverAuthoring : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 5f;
    public float rotationSpeed = 10f;
    //public GameObject collisionRayOriginTransform;

    public class Baker : Baker<UnitMoverAuthoring>
    {
        public override void Bake(UnitMoverAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new UnitMover
            {
                moveSpeed              = authoring.moveSpeed,
                rotationSpeed          = authoring.rotationSpeed,
                targetPosition         = float3.zero,
                isMoving               = false,
                //collisionRayOrigin = authoring.collisionRayOriginTransform.transform.position,
            });
        }
    }
}

public struct UnitMover : IComponentData
{
    public float moveSpeed;
    public float rotationSpeed;
    public float3 targetPosition;
    public bool isMoving;
    
   // public float3 collisionRayOrigin;
    public float collisionRadius;

    public float3 lastPosition;
    public bool blocked;
}
