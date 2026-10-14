using Unity.Entities;

// Sets the bits ReviveJob (ReviveRequestSystem) sets on a revived corpse. Runs after SpawnStateInitSystem,
// which resets Minion/Undead off on every NewlySpawned entity.
[UpdateInGroup(typeof(SpawnInitSystemGroup))]
[UpdateAfter(typeof(SpawnStateInitSystem))]
public partial struct PlayerMinionSpawnInitSystem : ISystem
{
    private ComponentLookup<Undead> _undeadLookup;
    private ComponentLookup<Minion> _minionLookup;
    private ComponentLookup<PlayerUnitBrain> _playerUnitBrainLookup;
    private ComponentLookup<UtilityBrain> _utilityBrainLookup;

    public void OnCreate(ref SystemState state)
    {
        _undeadLookup = state.GetComponentLookup<Undead>(false);
        _minionLookup = state.GetComponentLookup<Minion>(false);
        _playerUnitBrainLookup = state.GetComponentLookup<PlayerUnitBrain>(false);
        _utilityBrainLookup = state.GetComponentLookup<UtilityBrain>(false);
    }

    public void OnUpdate(ref SystemState state)
    {
        _undeadLookup.Update(ref state);
        _minionLookup.Update(ref state);
        _playerUnitBrainLookup.Update(ref state);
        _utilityBrainLookup.Update(ref state);

        EntityCommandBuffer ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);

        foreach ((RefRO<NewlySpawned> _, RefRO<SpawnAsPlayerMinion> _, Entity entity) in
            SystemAPI.Query<RefRO<NewlySpawned>, RefRO<SpawnAsPlayerMinion>>().WithEntityAccess())
        {
            if (_undeadLookup.HasComponent(entity))
                _undeadLookup.SetComponentEnabled(entity, true);
            if (_minionLookup.HasComponent(entity))
                _minionLookup.SetComponentEnabled(entity, true);
            if (_playerUnitBrainLookup.HasComponent(entity))
                _playerUnitBrainLookup.SetComponentEnabled(entity, true);
            if (_utilityBrainLookup.HasComponent(entity))
                _utilityBrainLookup.SetComponentEnabled(entity, false);

            ecb.RemoveComponent<SpawnAsPlayerMinion>(entity);
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }
}
