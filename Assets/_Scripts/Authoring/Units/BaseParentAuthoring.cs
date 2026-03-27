using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;

public class BaseParentAuthoring : MonoBehaviour
{
    [FormerlySerializedAs("characterRoot")] public GameObject parentEntity;
    
    public class Baker : Baker<BaseParentAuthoring>
    {
        public override void Bake(BaseParentAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic | TransformUsageFlags.NonUniformScale);
            Entity parentEntity = GetEntity(authoring.parentEntity, TransformUsageFlags.Dynamic);
            
            AddComponent(entity, new BaseParent { baseParentEntity = parentEntity });
        }
    }
}
