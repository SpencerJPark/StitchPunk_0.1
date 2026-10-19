using Unity.Collections;
using Unity.Entities;

// Singleton rebuilt every frame by TargetOccupancySystem: reservable target → each unit whose StateMachine
// (active or pending) targets it. Reservations are derived from this, never stored, so a dead or
// retargeted holder frees its slot without any release step (AF-D6).
public struct TargetOccupancy : IComponentData
{
    public NativeParallelMultiHashMap<Entity, Entity> occupantsByTarget;
}
