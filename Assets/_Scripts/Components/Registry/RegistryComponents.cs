using Unity.Entities;

public struct HordeRegistry : IComponentData
{
    /// <summary>Next available horde ID</summary>
    public int nextHordeId;
}