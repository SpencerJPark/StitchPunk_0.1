using DotsAnimationToolkit;
using DotsMovementToolkit;
using Unity.Burst;
using Unity.Entities;

// Resets enableable components on NewlySpawned entities, including pool-reclaimed bodies.
// Runs after spawner playback, so lookup writes are direct (no ECB).
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
    private ComponentLookup<ZombifyRequest>    _zombifyLookup;
    private ComponentLookup<Selected>          _selectedLookup;
    private ComponentLookup<PathRequest>       _pathRequestLookup;
    private ComponentLookup<DStarLiteFollower> _dStarLookup;
    private ComponentLookup<AStarFollower>     _aStarLookup;
    private ComponentLookup<FlowFieldFollower> _flowFieldLookup;
    private ComponentLookup<HordeMembership>   _hordeLookup;
    private ComponentLookup<UtilityBrain>    _utilityBrainV2Lookup;
    private ComponentLookup<Movement>          _movementLookup;
    private ComponentLookup<Gravity>           _gravityLookup;
    private ComponentLookup<AnimationCommandPending> _animationCommandPendingLookup;
    private BufferLookup<AnimationCommand>     _animationCommandLookup;
    private ComponentLookup<CutsceneActor>     _cutsceneActorLookup;
    private ComponentLookup<CutsceneMarkIssued> _cutsceneMarkIssuedLookup;
    private ComponentLookup<FakeRagdoll> _fakeRagdollLookup;

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
        _zombifyLookup         = state.GetComponentLookup<ZombifyRequest>(false);
        _selectedLookup        = state.GetComponentLookup<Selected>(false);
        _pathRequestLookup     = state.GetComponentLookup<PathRequest>(false);
        _dStarLookup           = state.GetComponentLookup<DStarLiteFollower>(false);
        _aStarLookup           = state.GetComponentLookup<AStarFollower>(false);
        _flowFieldLookup       = state.GetComponentLookup<FlowFieldFollower>(false);
        _hordeLookup           = state.GetComponentLookup<HordeMembership>(false);
        _utilityBrainV2Lookup  = state.GetComponentLookup<UtilityBrain>(false);
        _movementLookup        = state.GetComponentLookup<Movement>(false);
        _gravityLookup         = state.GetComponentLookup<Gravity>(false);
        _animationCommandPendingLookup = state.GetComponentLookup<AnimationCommandPending>(false);
        _animationCommandLookup = state.GetBufferLookup<AnimationCommand>(false);
        _cutsceneActorLookup   = state.GetComponentLookup<CutsceneActor>(false);
        _cutsceneMarkIssuedLookup = state.GetComponentLookup<CutsceneMarkIssued>(false);
        _fakeRagdollLookup = state.GetComponentLookup<FakeRagdoll>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // Main-thread lookup writes don't wait for in-flight jobs (ZombifyJob writes Undead/ZombifyRequest).
        state.CompleteDependency();

        _deadLookup.Update(ref state);
        _ragdollActorLookup.Update(ref state);
        _ragdollLaunchLookup.Update(ref state);
        _undeadLookup.Update(ref state);
        _minionLookup.Update(ref state);
        _reviveLookup.Update(ref state);
        _zombifyLookup.Update(ref state);
        _selectedLookup.Update(ref state);
        _pathRequestLookup.Update(ref state);
        _dStarLookup.Update(ref state);
        _aStarLookup.Update(ref state);
        _flowFieldLookup.Update(ref state);
        _hordeLookup.Update(ref state);
        _utilityBrainV2Lookup.Update(ref state);
        _movementLookup.Update(ref state);
        _gravityLookup.Update(ref state);
        _animationCommandPendingLookup.Update(ref state);
        _animationCommandLookup.Update(ref state);
        _cutsceneActorLookup.Update(ref state);
        _cutsceneMarkIssuedLookup.Update(ref state);
        _fakeRagdollLookup.Update(ref state);

        foreach ((RefRO<NewlySpawned> _, Entity entity) in
            SystemAPI.Query<RefRO<NewlySpawned>>().WithEntityAccess())
        {
            if (_deadLookup.HasComponent(entity))
                _deadLookup.SetComponentEnabled(entity, false);

            // A pool-reclaimed corpse must drop the ragdoll it was launched with.
            if (_ragdollActorLookup.HasComponent(entity))
                _ragdollActorLookup.SetComponentEnabled(entity, false);
            if (_ragdollLaunchLookup.HasComponent(entity))
                _ragdollLaunchLookup.SetComponentEnabled(entity, false);

            if (_undeadLookup.HasComponent(entity))
                _undeadLookup.SetComponentEnabled(entity, false);
            if (_minionLookup.HasComponent(entity))
                _minionLookup.SetComponentEnabled(entity, false);
            if (_reviveLookup.HasComponent(entity))
                _reviveLookup.SetComponentEnabled(entity, false);
            // A reclaimed body must not carry the previous life's conversion request.
            if (_zombifyLookup.HasComponent(entity))
                _zombifyLookup.SetComponentEnabled(entity, false);
            if (_selectedLookup.HasComponent(entity))
                _selectedLookup.SetComponentEnabled(entity, false);
            // Stale cutscene binding or mark-issued flag would swallow the next order.
            if (_cutsceneActorLookup.HasComponent(entity))
                _cutsceneActorLookup.SetComponentEnabled(entity, false);
            if (_cutsceneMarkIssuedLookup.HasComponent(entity))
                _cutsceneMarkIssuedLookup.SetComponentEnabled(entity, false);
            // Restoring makes FakeRagdollPoseSystem put the rest pose back for a reclaimed corpse.
            if (_fakeRagdollLookup.HasComponent(entity) && _fakeRagdollLookup.IsComponentEnabled(entity))
                _fakeRagdollLookup.GetRefRW(entity).ValueRW.phase = FakeRagdollPhase.Restoring;

            if (_pathRequestLookup.HasComponent(entity))
                _pathRequestLookup.SetComponentEnabled(entity, false);
            if (_dStarLookup.HasComponent(entity))
                _dStarLookup.SetComponentEnabled(entity, false);
            if (_aStarLookup.HasComponent(entity))
                _aStarLookup.SetComponentEnabled(entity, false);
            if (_flowFieldLookup.HasComponent(entity))
                _flowFieldLookup.SetComponentEnabled(entity, false);
            if (_hordeLookup.HasComponent(entity))
                _hordeLookup.SetComponentEnabled(entity, false);

            if (_utilityBrainV2Lookup.HasComponent(entity))
                _utilityBrainV2Lookup.SetComponentEnabled(entity, true);

            // Death disables Movement and Gravity; a reclaimed unit must move and fall again.
            if (_movementLookup.HasComponent(entity))
                _movementLookup.SetComponentEnabled(entity, true);
            if (_gravityLookup.HasComponent(entity))
                _gravityLookup.SetComponentEnabled(entity, true);

            // Hard-stop every layer so UnitAnimationAssignmentSystem's IsPlaying check reads false
            // and re-issues the Base idle clip, not a stale clip from the previous life.
            if (_animationCommandPendingLookup.HasComponent(entity) && _animationCommandLookup.HasBuffer(entity))
            {
                DynamicBuffer<AnimationCommand> resetCommands = _animationCommandLookup[entity];
                // Layers 0-5: Base/Action/Override/Face/Eyes/Mouth.
                for (byte layerIndex = 0; layerIndex <= 5; layerIndex++)
                {
                    PlaybackApi.Stop(
                        ref resetCommands,
                        _animationCommandPendingLookup.GetEnabledRefRW<AnimationCommandPending>(entity),
                        layerIndex,
                        blendDuration: 0f);
                }
            }
        }
    }
}
