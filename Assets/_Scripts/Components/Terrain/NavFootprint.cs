using Unity.Entities;
using Unity.Mathematics;

// A runtime structure's blocked rectangle on the nav grid, centred on the entity's LocalTransform; rotationRadians is added to the entity's yaw.
// With NavGridSettings.externalCostRegions this is the only way a structure reaches the cost map.
public struct NavFootprint : IComponentData
{
    public float2 halfExtents;
    public float rotationRadians;
    public byte cost;
}
