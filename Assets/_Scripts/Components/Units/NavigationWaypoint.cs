using Unity.Entities;

public struct NavigationWaypoint : IComponentData
{
    public float radius; // scatter radius; 0 = walk to exact center
}
