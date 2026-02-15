using Unity.Entities;
using UnityEngine;

// Motivations Range -100 to 100
public struct Hunger : IComponentData
{
    [Range(-100, 100)] public int value;
}
public struct Energy : IComponentData
{
    [Range(-100, 100)] public int value;
}
public struct Fun : IComponentData
{
    [Range(-100, 100)] public int value;
}
public struct Social : IComponentData
{
    [Range(-100, 100)] public int value;
}
public struct Comfort : IComponentData
{
    [Range(-100, 100)] public int value;
}
public struct Bladder : IComponentData
{
    [Range(-100, 100)] public int value;
}
public struct Safety : IComponentData
{
    [Range(-100, 100)] public int value;
}
public struct Movement : IComponentData
{
    [Range(-100, 100)] public int value;
}

public struct SelfPreservation : IComponentData
{
    public float healthThreshold;
}



// Randomly Picked
public struct Bookworm : IComponentData
{
    [Range(-100, 100)] public int value;
}
public struct Work : IComponentData
{
    [Range(-100, 100)] public int value;
}
public struct NightOwl : IComponentData
{
    [Range(-100, 100)] public int value;
}
public struct EarlyBird : IComponentData
{
    [Range(-100, 100)] public int value;
}

public struct Glutton : IComponentData
{
    [Range(-100, 100)] public int value;
}

public struct Grumpy : IComponentData
{
    [Range(-100, 100)] public int value;
}

public struct Depressed : IComponentData
{
    [Range(-100, 100)] public int value;
}

public struct Lazy : IComponentData
{
    [Range(-100, 100)] public int value;
}

public struct Nervous : IComponentData
{
    [Range(-100, 100)] public int value;
}