using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class AnimatorAuthoring : MonoBehaviour
{
    public AnimationType startingAnimation = AnimationType.Idle;
    public float animationSpeed = 1f;
    
    public class Baker : Baker<AnimatorAuthoring>
    {
        public override void Bake(AnimatorAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            
            AddComponent(entity, new Animator
            {
                currentAnimation = authoring.startingAnimation,
                requestedAnimation = AnimationType.None,
                speed = authoring.animationSpeed,
            });
            
            // Add a secondary layer for overlays (facing, expressions)
            AddComponent(entity, new AnimatorLayer
            {
                active = false,
                weight = 1f,
            });
            
            AddBuffer<AnimatorTarget>(entity);
        }
    }
}

public struct Animator : IComponentData
{
    // Primary animation layer
    public AnimationType currentAnimation;
    public AnimationType requestedAnimation;
    public float time;
    public float speed;
    
    // Blend state
    public AnimationType blendFromAnimation;
    public float blendFromTime;
    public float blendWeight;      // 0 = fully blendFrom, 1 = fully current
    public float blendDuration;
    public bool isBlending;
}

// Secondary animation layer (for things like facing direction, expressions)
public struct AnimatorLayer : IComponentData
{
    public AnimationType animation;
    public float time;
    public float weight;    // How much this layer affects the final pose
    public bool active;
}

// Buffer of all body parts belonging to this character
[InternalBufferCapacity(32)]
public struct AnimatorTarget : IBufferElementData
{
    public Entity entity;
    public AnimationTarget target;
}