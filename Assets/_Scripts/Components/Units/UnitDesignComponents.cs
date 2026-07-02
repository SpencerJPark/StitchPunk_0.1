using Unity.Entities;
using Unity.Mathematics;


public struct RandomizeDesign : IComponentData, IEnableableComponent {}

// UnitSkinColor / UnitHairColor / UnitHeadShape / UnitNoseShape were removed — CharacterPalette
// (BodyPartComponents.cs) is their semantic successor (skin/hair colour indices per character).

// Design Tags live on the parts
public struct UnitHairDesign : IComponentData { }
public struct UnitMustacheDesign : IComponentData { }




/// <summary>
/// Component that enables outline rendering on an entity.
/// Add this component to any entity that should have an outline.
/// </summary>
public struct Outline : IComponentData, IEnableableComponent
{
    public float4 outlineColor;
    public float outlineWidth;
}

public struct OutlineChild : IComponentData {
    public Entity parentEntity;
}

/// <summary>
/// When this component is present and enabled, the entity renders to the outline camera
/// </summary>
public struct OutlinedTag : IComponentData, IEnableableComponent
{
}