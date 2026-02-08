using Unity.Entities;

public struct SwapBrainRequest : IComponentData
{
    public BrainType newBrainType;
}

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