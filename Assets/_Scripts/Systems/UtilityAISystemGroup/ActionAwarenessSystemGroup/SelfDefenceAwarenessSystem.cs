using Unity.Burst;
using Unity.Entities;

// Fight-or-flight interrupt: detects active threats and emits ActionInterruptRequest so the unit
// immediately reacts to being attacked rather than waiting for the next decision cycle.
// Full implementation (bravery, aggressor selection, attack options) is Phase 3 migration work.
[BurstCompile]
[UpdateInGroup(typeof(AIAwarenessSystemGroup))]
[UpdateAfter(typeof(EnemyAwarenessSystem))]
public partial struct SelfDefenceAwarenessSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state) { }
}
