using Unity.Burst;
using Unity.Entities;

// Initiator path: units seeking social interaction append a Talk UtilityActions entry.
// Phase 3 migration work: convert from AIBrain/ActionOption to UtilityBrain/UtilityActions.
[BurstCompile]
[UpdateInGroup(typeof(AIAwarenessSystemGroup))]
public partial struct SocialAwarenessSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state) { }
}
