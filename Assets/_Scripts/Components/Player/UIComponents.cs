using Unity.Entities;
using Unity.Mathematics;

public struct SelectionBoxData : IComponentData, IEnableableComponent
{
    public float PositionX;
    public float PositionY;
    public float Width;
    public float Height;
}

public struct CursorScreenPosition : IComponentData
{
    public float2 Value;
}
