using Unity.Entities;

public struct CitizenBrain : IComponentData { }

public struct ZombieBrain : IComponentData { }

public enum BrainType
{
    None,
    Minion,
    Citizen,
    Character,
    Zombie,
    Guard,
    Merchant,
}

public struct SwapBrainRequest : IComponentData, IEnableableComponent
{
    public BrainType newBrainType;
}