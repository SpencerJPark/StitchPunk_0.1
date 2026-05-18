using Unity.Entities;

public struct Personality : IComponentData
{
    public float bravery; // [0, 1]: 0 = flees at any sign of danger, 1 = fights to the death
}
