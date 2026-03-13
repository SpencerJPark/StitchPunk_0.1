using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

[InternalBufferCapacity(8)]
public struct AnimationLayer : IBufferElementData
{
    public AnimationLayerType layer;
    public AnimationType animation;
    public float time;
    public float speed;
    public bool active;
    public bool looping;
}

[InternalBufferCapacity(32)]
public struct AnimatorTarget : IBufferElementData
{
    public Entity entity;
    public AnimationTarget target;
}

public struct Billboard : IComponentData
{
    public Entity parentEntity;
}

public struct AnimationTargetTag : IComponentData
{
    public AnimationTarget target;
}

public struct ParentAnimator : IComponentData
{
    public Entity animator;
}

public struct AnimationTargetRestPose : IComponentData
{
    public float3 localPosition;
    public float rotation;
    public float2 scale;
    public int baseImageIndex;
}

public struct AnimationTargetPose : IComponentData
{
    public float3 localPosition;
    public float rotation;
    public float2 scale;
    public int imageIndex;
}




[InternalBufferCapacity(8)]
public struct DesignLayer : IBufferElementData
{
    public AnimationLayerType layer;
    public AnimationType animation;

}

[InternalBufferCapacity(32)]
public struct DesignerTarget : IBufferElementData
{
    public Entity entity;
    public AnimationTarget target;
}

public struct DesignTargetTag : IComponentData
{
    public DesignTarget target;
}

public struct ParentDesigner : IComponentData
{
    public Entity designer;
}

public struct ImageIndex : IComponentData
{
    public int index;
    public bool onUpdate;
}

[MaterialProperty("_ImageIndex")]
public struct ImageIndexOverride : IComponentData
{
    public float Value;
}

// Damage color bool propertyBlock
// tint for effects
// disolve