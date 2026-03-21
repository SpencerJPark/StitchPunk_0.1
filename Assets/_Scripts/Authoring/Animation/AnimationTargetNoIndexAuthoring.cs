using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;

public class AnimationTargetNoIndexAuthoring : MonoBehaviour
{
    public AnimationTarget animationTarget;
    public GameObject characterRoot;
    
    public class Baker : Baker<AnimationTargetNoIndexAuthoring>
    {
        public override void Bake(AnimationTargetNoIndexAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic | TransformUsageFlags.NonUniformScale);
            Entity characterEntity = GetEntity(authoring.characterRoot, TransformUsageFlags.Dynamic);
            
            var transform = authoring.transform;
            
            AddComponent(entity, new AnimationTargetTag { target = authoring.animationTarget });
            AddComponent(entity, new BaseParent { baseParentEntity = characterEntity });
            
            AddComponent(entity, new AnimationTargetRestPose
            {
                localPosition = transform.localPosition,
                rotation = transform.localEulerAngles.z,
                scale = new float2(transform.localScale.x, transform.localScale.y),
                baseImageIndex = 0,
            });
            
            AddComponent(entity, new AnimationTargetPose());
            AddComponent(entity, new PostTransformMatrix { Value = float4x4.identity });
        }
    }
}