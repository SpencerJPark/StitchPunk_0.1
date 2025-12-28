using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class CharacterAnimationAuthoring : MonoBehaviour
{
    public AnimationType startingAnimation = AnimationType.Idle;
    public float animationSpeed = 1f;
    
    public class Baker : Baker<CharacterAnimationAuthoring>
    {
        public override void Bake(CharacterAnimationAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            
            AddComponent(entity, new CharacterAnimation
            {
                currentAnimation = authoring.startingAnimation,
                requestedAnimation = AnimationType.None,
                speed = authoring.animationSpeed,
            });
            
            // Add a secondary layer for overlays (facing, expressions)
            AddComponent(entity, new CharacterAnimationLayer
            {
                active = false,
                weight = 1f,
            });
            
            AddBuffer<CharacterBodyPart>(entity);
        }
    }
}

public struct CharacterAnimation : IComponentData
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
public struct CharacterAnimationLayer : IComponentData
{
    public AnimationType animation;
    public float time;
    public float weight;    // How much this layer affects the final pose
    public bool active;
}

// Buffer of all body parts belonging to this character
[InternalBufferCapacity(16)]
public struct CharacterBodyPart : IBufferElementData
{
    public Entity entity;
    public BodyPart part;
}