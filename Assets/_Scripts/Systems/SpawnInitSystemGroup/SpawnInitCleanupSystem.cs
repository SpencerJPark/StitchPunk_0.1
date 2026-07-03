using Unity.Burst;
using Unity.Entities;

/// <summary>
/// Disables NewlySpawned on every body entity at the end of SpawnSystemGroup,
/// after all downstream init systems have processed it for the current frame.
///
/// NewlySpawned is NOT removed — it persists on the entity so it can be re-enabled
/// when the entity is reclaimed from the pool on a future spawn.
/// </summary>
[UpdateInGroup(typeof(SpawnInitSystemGroup), OrderLast = true)]
[BurstCompile]
public partial struct SpawnInitCleanupSystem : ISystem
{
    private ComponentLookup<NewlySpawned> _newlySpawnedLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        _newlySpawnedLookup = state.GetComponentLookup<NewlySpawned>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        _newlySpawnedLookup.Update(ref state);

        foreach (var (_, entity) in
            SystemAPI.Query<RefRO<NewlySpawned>>().WithEntityAccess())
        {
            _newlySpawnedLookup.SetComponentEnabled(entity, false);
        }
    }
}
