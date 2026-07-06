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

// Per-instance sprite tint. Drives the _BaseColor shader property (Hybrid Per
// Instance in the 2D graphs) so every body-part quad can carry its own colour
// while the whole crowd still batches into one draw call. Multiply-tint: the
// sprite is baked white-fill / black-outline, so Value multiplies the fill and
// the black outline (0 * tint = 0) survives untouched. White (1,1,1,1) = the
// authored sprite unchanged. Baked white by BodyPartAuthoring; a skin/design
// system writes per-part colours.
[MaterialProperty("_BaseColor")]
public struct BodyPartTint : IComponentData
{
    public float4 Value; // RGBA, multiplies the sprite
}

// Damage color bool propertyBlock
// tint for effects
// disolve