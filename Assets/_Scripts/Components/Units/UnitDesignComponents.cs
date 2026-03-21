using Unity.Entities;

public struct RandomizeDesign : IComponentData, IEnableableComponent {}

// Buffer element for parts

public struct UnitSkinColor : IComponentData
{
    public SkinColor skinColor;
}

public struct UnitHairColor : IComponentData
{
    public HairColor hairColor;
}

public struct UnitHeadShape : IComponentData
{
    public HeadShape headShape;
}

public struct UnitNoseShape : IComponentData
{
    public NoseShape noseShape;
}


// Design Tags live on the parts
public struct UnitHairDesign : IComponentData { }
public struct UnitMustacheDesign : IComponentData { }

