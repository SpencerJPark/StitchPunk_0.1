using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class GuardPostAuthoring : MonoBehaviour
{
    public GameObject camp;
    public float idleRadius = 10f;
    public float leashRadius = 25f;

    public class Baker : Baker<GuardPostAuthoring>
    {
        public override void Bake(GuardPostAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            bool hasCamp = authoring.camp != null;
            if (hasCamp)
                DependsOn(authoring.camp.transform);

            AddComponent(entity, new GuardPost
            {
                camp           = hasCamp ? GetEntity(authoring.camp, TransformUsageFlags.Dynamic) : Entity.Null,
                anchorPosition = hasCamp ? (float3)authoring.camp.transform.position : (float3)authoring.transform.position,
                idleRadius     = authoring.idleRadius,
                leashRadius    = authoring.leashRadius,
            });
        }
    }
}
