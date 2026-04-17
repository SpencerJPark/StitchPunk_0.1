using Unity.Entities;
using UnityEngine;

// Randomly Picked
public struct BookwormMotivation : IComponentData
{
    [Range(-100, 100)] public int value;
}
public struct WorkMotivation : IComponentData
{
    [Range(-100, 100)] public int value;
}
public struct NightOwlMotivation : IComponentData
{
    [Range(-100, 100)] public int value;
}
public struct EarlyBirdMotivation : IComponentData
{
    [Range(-100, 100)] public int value;
}

public struct GluttonMotivation : IComponentData
{
    [Range(-100, 100)] public int value;
}

public struct GrumpyMotivation : IComponentData
{
    [Range(-100, 100)] public int value;
}

public struct DepressedMotivation : IComponentData
{
    [Range(-100, 100)] public int value;
}

public struct LazyMotivation : IComponentData
{
    [Range(-100, 100)] public int value;
}

public struct NervousMotivation : IComponentData
{
    [Range(-100, 100)] public int value;
}