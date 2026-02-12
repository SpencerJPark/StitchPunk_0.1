using Unity.Entities;
using Unity.Mathematics;

public struct ActionOption : IBufferElementData
{
    public Entity interactableEntity;
    public float score;
}

public struct Hurt : IBufferElementData
{
    public Entity attackerEntity;
    public float distance;
}
