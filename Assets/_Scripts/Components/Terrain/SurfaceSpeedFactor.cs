using Unity.Entities;

// factor = wanted multiplier (1 = none); appliedFactor = what the apply system multiplied Movement.moveSpeed by this frame (0 = nothing applied).
// Apply and revert bracket UnitMoverSystem so no other system sees a scaled moveSpeed: Drunk/Roll/Behavior systems capture moveSpeed as a base.
public struct SurfaceSpeedFactor : IComponentData
{
    public float factor;
    public float appliedFactor;
}

// Singleton read by TerrainSurfaceSpeedSampleSystem; created with defaults when absent.
public struct TerrainSurfaceSpeedSettings : IComponentData
{
    public float roadMultiplier;
    public float shallowMultiplier;
}
