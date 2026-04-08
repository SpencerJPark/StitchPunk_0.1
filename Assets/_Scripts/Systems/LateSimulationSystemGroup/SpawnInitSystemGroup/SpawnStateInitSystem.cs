using Unity.Burst;
using Unity.Entities;

/// <summary>
/// Resets every root-entity enableable component to its correct spawn state on
/// newly spawned or reclaimed body entities.
///
/// Runs after UnitSpawnerSystem.ecb.Playback() has created the real entities,
/// so all ComponentLookup operations are direct and immediate (no ECB required).
/// Filtering on [WithAll&lt;NewlySpawned&gt;] keeps this to a no-op on all other frames.
///
/// To add a component that needs a specific initial state at spawn:
///   1. Add a ComponentLookup field for it in OnCreate / OnUpdate.
///   2. Add a HasComponent + SetComponentEnabled call in OnUpdate.
///   No edits to UnitSpawnerSystem are needed.
/// </summary>
[UpdateInGroup(typeof(SpawnInitSystemGroup))]
[BurstCompile]
public partial struct SpawnStateInitSystem : ISystem
{
    private ComponentLookup<Alive>             _aliveLookup;
    private ComponentLookup<Dead>              _deadLookup;
    private ComponentLookup<Ragdoll2DLaunch>   _ragdollLaunchLookup;
    private ComponentLookup<Undead>            _undeadLookup;
    private ComponentLookup<Minion>            _minionLookup;
    private ComponentLookup<Revive>            _reviveLookup;
    private ComponentLookup<SwapBrainRequest>  _swapBrainLookup;
    private ComponentLookup<Selected>          _selectedLookup;
    private ComponentLookup<PathRequest>       _pathRequestLookup;
    private ComponentLookup<DStarLiteFollower> _dStarLookup;
    private ComponentLookup<FlowFieldFollower> _flowFieldLookup;
    private ComponentLookup<HordeMembership>   _hordeLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        _aliveLookup         = state.GetComponentLookup<Alive>(false);
        _deadLookup          = state.GetComponentLookup<Dead>(false);
        _ragdollLaunchLookup = state.GetComponentLookup<Ragdoll2DLaunch>(false);
        _undeadLookup        = state.GetComponentLookup<Undead>(false);
        _minionLookup        = state.GetComponentLookup<Minion>(false);
        _reviveLookup        = state.GetComponentLookup<Revive>(false);
        _swapBrainLookup     = state.GetComponentLookup<SwapBrainRequest>(false);
        _selectedLookup      = state.GetComponentLookup<Selected>(false);
        _pathRequestLookup   = state.GetComponentLookup<PathRequest>(false);
        _dStarLookup         = state.GetComponentLookup<DStarLiteFollower>(false);
        _flowFieldLookup     = state.GetComponentLookup<FlowFieldFollower>(false);
        _hordeLookup         = state.GetComponentLookup<HordeMembership>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        _aliveLookup.Update(ref state);
        _deadLookup.Update(ref state);
        _ragdollLaunchLookup.Update(ref state);
        _undeadLookup.Update(ref state);
        _minionLookup.Update(ref state);
        _reviveLookup.Update(ref state);
        _swapBrainLookup.Update(ref state);
        _selectedLookup.Update(ref state);
        _pathRequestLookup.Update(ref state);
        _dStarLookup.Update(ref state);
        _flowFieldLookup.Update(ref state);
        _hordeLookup.Update(ref state);

        foreach (var (_, entity) in
            SystemAPI.Query<RefRO<NewlySpawned>>().WithEntityAccess())
        {
            // Health / life state — must start alive.
            if (_aliveLookup.HasComponent(entity))
                _aliveLookup.SetComponentEnabled(entity, true);
            if (_deadLookup.HasComponent(entity))
                _deadLookup.SetComponentEnabled(entity, false);

            // Ragdoll root — disabled until death.
            if (_ragdollLaunchLookup.HasComponent(entity))
                _ragdollLaunchLookup.SetComponentEnabled(entity, false);

            // Faction / revive state — citizens start as neither undead nor minion.
            if (_undeadLookup.HasComponent(entity))
                _undeadLookup.SetComponentEnabled(entity, false);
            if (_minionLookup.HasComponent(entity))
                _minionLookup.SetComponentEnabled(entity, false);
            if (_reviveLookup.HasComponent(entity))
                _reviveLookup.SetComponentEnabled(entity, false);
            if (_swapBrainLookup.HasComponent(entity))
                _swapBrainLookup.SetComponentEnabled(entity, false);
            if (_selectedLookup.HasComponent(entity))
                _selectedLookup.SetComponentEnabled(entity, false);

            // Pathfinding — disabled until a path is requested.
            if (_pathRequestLookup.HasComponent(entity))
                _pathRequestLookup.SetComponentEnabled(entity, false);
            if (_dStarLookup.HasComponent(entity))
                _dStarLookup.SetComponentEnabled(entity, false);
            if (_flowFieldLookup.HasComponent(entity))
                _flowFieldLookup.SetComponentEnabled(entity, false);
            if (_hordeLookup.HasComponent(entity))
                _hordeLookup.SetComponentEnabled(entity, false);
        }
    }
}
