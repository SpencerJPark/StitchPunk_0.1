using Unity.Entities;
using UnityEngine;

public enum MotivationType
{
    Hunger,
    Energy,
    Fun,
    Social,
    Comfort,
    Bladder,
    Safety,
    Movement,
    SelfPreservation,
    Bookworm,
    Work,
    NightOwl,
    EarlyBird,
    Glutton,
    Grumpy,
    Depressed,
    Lazy,
    Nervous,
}

// Motivations Range -100 to 100
public struct HungerMotivation : IComponentData
{
    [Range(-100, 100)] public int value;
}
public struct EnergyMotivation : IComponentData
{
    [Range(-100, 100)] public int value;
}
public struct FunMotivation : IComponentData
{
    [Range(-100, 100)] public int value;
}
public struct SocialMotivation : IComponentData
{
    [Range(-100, 100)] public int value;
}
public struct ComfortMotivation : IComponentData
{
    [Range(-100, 100)] public int value;
}
public struct BladderMotivation : IComponentData
{
    [Range(-100, 100)] public int value;
}
public struct SafetyMotivation : IComponentData
{
    [Range(-100, 100)] public int value;
}
public struct MovementMotivation : IComponentData
{
    [Range(-100, 100)] public int value;
}

public struct SelfPreservationMotivation : IComponentData
{
    [Range(-100, 100)] public int value;
}



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