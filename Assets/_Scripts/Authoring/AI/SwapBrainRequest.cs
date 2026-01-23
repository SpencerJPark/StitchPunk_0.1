using Unity.Entities;

public struct SwapBrainRequest : IComponentData
{
    public BrainType newBrainType;
}

public enum BrainType
{
    Citizen,
    Zombie,
    Guard,
    Merchant,
    None
}