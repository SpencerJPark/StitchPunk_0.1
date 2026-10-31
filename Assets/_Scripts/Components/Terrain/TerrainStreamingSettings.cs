using Unity.Entities;

// Chebyshev distances in tiles from the focus tile. unloadRadiusTiles must exceed loadRadiusTiles so a unit
// hovering on a border does not thrash (hysteresis); the load radius must exceed what the camera shows.
public struct TerrainStreamingSettings : IComponentData
{
    public int loadRadiusTiles;
    public int unloadRadiusTiles;
    public int maxLoadsPerFrame;
}
