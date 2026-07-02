using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

public struct AnimationRequest : IComponentData, IEnableableComponent { }
public struct SetAnimation : IBufferElementData
{
    public AnimationLayerType layer;
    public AnimationType      animation;
    public float              speed;
    public bool               looping;
}


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

// Unit Visual Components
public struct Billboard : IComponentData
{
    public Entity parentEntity;
}

// Part identity (AnimationTargetTag) and the root part registry (AnimatorTarget buffer) were unified
// into BodyPartInfo / BodyPart — see Components/Units/BodyPartComponents.cs.

public struct BaseParent : IComponentData
{
    public Entity baseParentEntity;
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