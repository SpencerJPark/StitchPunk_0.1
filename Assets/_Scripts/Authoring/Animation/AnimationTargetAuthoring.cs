using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;

public class AnimationTargetAuthoring : MonoBehaviour
{
    public AnimationTarget animationTarget;
    public GameObject characterRoot;
    public int baseImageIndex;
    
    public class Baker : Baker<AnimationTargetAuthoring>
    {
        public override void Bake(AnimationTargetAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic | TransformUsageFlags.NonUniformScale);
            Entity characterEntity = GetEntity(authoring.characterRoot, TransformUsageFlags.Dynamic);
            
            var transform = authoring.transform;
            
            AddComponent(entity, new AnimationTargetTag { target = authoring.animationTarget });
            AddComponent(entity, new ParentAnimator { animator = characterEntity });
            
            AddComponent(entity, new AnimationTargetRestPose
            {
                localPosition = transform.localPosition,
                rotation = transform.localEulerAngles.z,
                scale = new float2(transform.localScale.x, transform.localScale.y),
                baseImageIndex = authoring.baseImageIndex,
            });
            
            AddComponent(entity, new AnimationTargetPose());
            AddComponent(entity, new PostTransformMatrix { Value = float4x4.identity });
        }
    }
}

public struct AnimationTargetTag : IComponentData
{
    public AnimationTarget target;
}

public struct ParentAnimator : IComponentData
{
    public Entity animator;
}

// Rest pose - set during authoring, doesn't change at runtime
public struct AnimationTargetRestPose : IComponentData
{
    public float3 localPosition;
    public float rotation;
    public float2 scale;
    public int baseImageIndex;
}

// Computed each frame by the animation system
public struct AnimationTargetPose : IComponentData
{
    public float3 localPosition;
    public float rotation;
    public float2 scale;
    public int imageIndex;
}
