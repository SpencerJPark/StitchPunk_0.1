using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Component that enables outline rendering on an entity.
/// Add this component to any entity that should have an outline.
/// </summary>
public struct Outline : IComponentData
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