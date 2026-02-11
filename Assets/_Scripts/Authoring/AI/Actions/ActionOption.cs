using Unity.Entities;
using Unity.Mathematics;

public struct ActionOption : IBufferElementData
{
    public Entity waypoint;         // Entity.Null for innate actions like Wander
    public ActionType actionType;
    public AnimationType animation;
    public float duration;
    public NeedModifiers needModifiers;
    public float3 position;
    public float interactionRange;
    public float score;             // Calculated score based on needs
}