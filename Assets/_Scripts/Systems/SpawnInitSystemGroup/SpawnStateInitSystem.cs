using DotsAnimationToolkit;
using DotsMovementToolkit;
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
    private ComponentLookup<Dead>              _deadLookup;
    private ComponentLookup<RagdollActor>      _ragdollActorLookup;
    private ComponentLookup<RagdollLaunch>     _ragdollLaunchLookup;
    private ComponentLookup<Undead>            _undeadLookup;
    private ComponentLookup<Minion>            _minionLookup;
    private ComponentLookup<ReviveRequest>     _reviveLookup;
    private ComponentLookup<Selected>          _selectedLookup;
    private ComponentLookup<PathRequest>       _pathRequestLookup;
    private ComponentLookup<DStarLiteFollower> _dStarLookup;
    private ComponentLookup<FlowFieldFollower> _flowFieldLookup;
    private ComponentLookup<HordeMembership>   _hordeLookup;
    private ComponentLookup<UtilityBrain>    _utilityBrainV2Lookup;
    private ComponentLookup<Movement>          _movementLookup;
    private ComponentLookup<Gravity>           _gravityLookup;
    private ComponentLookup<AnimationCommandPending> _animationCommandPendingLookup;
    private BufferLookup<AnimationCommand>     _animationCommandLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        _deadLookup            = state.GetComponentLookup<Dead>(false);
        _ragdollActorLookup    = state.GetComponentLookup<RagdollActor>(false);
        _ragdollLaunchLookup   = state.GetComponentLookup<RagdollLaunch>(false);
        _undeadLookup          = state.GetComponentLookup<Undead>(false);
        _minionLookup          = state.GetComponentLookup<Minion>(false);
        _reviveLookup          = state.GetComponentLookup<ReviveRequest>(false);
        _selectedLookup        = state.GetComponentLookup<Selected>(false);
        _pathRequestLookup     = state.GetComponentLookup<PathRequest>(false);
        _dStarLookup           = state.GetComponentLookup<DStarLiteFollower>(false);
        _flowFieldLookup       = state.GetComponentLookup<FlowFieldFollower>(false);
        _hordeLookup           = state.GetComponentLookup<HordeMembership>(false);
        _utilityBrainV2Lookup  = state.GetComponentLookup<UtilityBrain>(false);
        _movementLookup        = state.GetComponentLookup<Movement>(false);
        _gravityLookup         = state.GetComponentLookup<Gravity>(false);
        _animationCommandPendingLookup = state.GetComponentLookup<AnimationCommandPending>(false);
        _animationCommandLookup = state.GetBufferLookup<AnimationCommand>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        _deadLookup.Update(ref state);
        _ragdollActorLookup.Update(ref state);
        _ragdollLaunchLookup.Update(ref state);
        _undeadLookup.Update(ref state);
        _minionLookup.Update(ref state);
        _reviveLookup.Update(ref state);
        _selectedLookup.Update(ref state);
        _pathRequestLookup.Update(ref state);
        _dStarLookup.Update(ref state);
        _flowFieldLookup.Update(ref state);
        _hordeLookup.Update(ref state);
        _utilityBrainV2Lookup.Update(ref state);
        _movementLookup.Update(ref state);
        _gravityLookup.Update(ref state);
        _animationCommandPendingLookup.Update(ref state);
        _animationCommandLookup.Update(ref state);

        foreach (var (_, entity) in
            SystemAPI.Query<RefRO<NewlySpawned>>().WithEntityAccess())
        {
            // Health / life state — Dead disabled means alive; units start alive.
            if (_deadLookup.HasComponent(entity))
                _deadLookup.SetComponentEnabled(entity, false);

            // Ragdoll — disabled until death. A pool-reclaimed corpse must drop the ragdoll it was
            // launched with before it can be handed out as a fresh spawn.
            if (_ragdollActorLookup.HasComponent(entity))
                _ragdollActorLookup.SetComponentEnabled(entity, false);
            if (_ragdollLaunchLookup.HasComponent(entity))
                _ragdollLaunchLookup.SetComponentEnabled(entity, false);

            // Faction / revive state — citizens start as neither undead nor minion.
            if (_undeadLookup.HasComponent(entity))
                _undeadLookup.SetComponentEnabled(entity, false);
            if (_minionLookup.HasComponent(entity))
                _minionLookup.SetComponentEnabled(entity, false);
            if (_reviveLookup.HasComponent(entity))
                _reviveLookup.SetComponentEnabled(entity, false);
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

            // v2 utility brain — enabled on spawn so the new pipeline starts immediately.
            if (_utilityBrainV2Lookup.HasComponent(entity))
                _utilityBrainV2Lookup.SetComponentEnabled(entity, true);

            // Movement/Gravity — disabled by a previous death; a reclaimed pool unit must
            // start able to move and fall again.
            if (_movementLookup.HasComponent(entity))
                _movementLookup.SetComponentEnabled(entity, true);
            if (_gravityLookup.HasComponent(entity))
                _gravityLookup.SetComponentEnabled(entity, true);

            // Playback layers — a reclaimed actor may carry stale state from its previous life (a
            // corpse's finished Death clip, mid-blend Action layer). Hard-stop every layer in the
            // six-layer convention (§4) so UnitAnimationAssignmentSystem's IsPlaying check reads
            // false and re-issues the Base idle clip fresh, rather than reading a stale "is playing"
            // answer from the layer's leftover state.
            if (_animationCommandPendingLookup.HasComponent(entity) && _animationCommandLookup.HasBuffer(entity))
            {
                DynamicBuffer<AnimationCommand> resetCommands = _animationCommandLookup[entity];
                for (byte layerIndex = 0; layerIndex <= (byte)AnimationToolkitLayer.Mouth; layerIndex++)
                {
                    AnimationCommandUtil.Stop(
                        ref resetCommands,
                        _animationCommandPendingLookup.GetEnabledRefRW<AnimationCommandPending>(entity),
                        layerIndex,
                        blendDuration: 0f);
                }
            }
        }
    }
}
