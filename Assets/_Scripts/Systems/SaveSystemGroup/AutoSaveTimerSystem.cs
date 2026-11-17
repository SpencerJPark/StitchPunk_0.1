using Unity.Burst;
using Unity.Entities;

/// <summary>
/// Ticks the auto-save interval. When the timer elapses, creates the AutoSavePending singleton;
/// AutoSaveCombatQuietSystem turns it into an Autosave SaveRequest once no unit has been in combat for a while.
/// Resets the timer immediately so the next interval starts from zero.
/// Does nothing if a SaveRequest is already pending (avoids stacked triggers).
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SaveSystemGroup))]
[UpdateAfter(typeof(PlayTimeTrackerSystem))]
public partial struct AutoSaveTimerSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<GameDataTag>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        Entity saveEntity = SystemAPI.GetSingletonEntity<GameDataTag>();

        RefRW<AutoSaveTimer> autoSaveTimer = SystemAPI.GetSingletonRW<AutoSaveTimer>();
        autoSaveTimer.ValueRW.elapsedSeconds += SystemAPI.Time.DeltaTime;

        if (autoSaveTimer.ValueRO.elapsedSeconds < autoSaveTimer.ValueRO.intervalSeconds)
            return;

        // Don't queue a second save if one is already pending
        if (SystemAPI.IsComponentEnabled<SaveRequest>(saveEntity))
            return;

        autoSaveTimer.ValueRW.elapsedSeconds = 0f;

        if (!SystemAPI.HasSingleton<AutoSavePending>())
        {
            Entity pendingEntity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponent<AutoSavePending>(pendingEntity);
        }
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state) { }
}
