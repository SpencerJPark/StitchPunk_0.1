using DotsAnimationToolkit;
using DotsMovementToolkit;
using Unity.Entities;
using Unity.Burst;
using Unity.Collections;

// Decides which clip each layer should be playing and issues AnimationCommands only on change —
// commands are requests, not state, so re-issuing Play every frame would restart the clip's
// crossfade/queue machinery for no reason. PlaybackApi answers "what's actually playing" against
// the toolkit's own PlaybackLayer buffer instead of tracking a shadow copy here.
[BurstCompile]
[UpdateInGroup(typeof(AnimationAssignmentSystemGroup))]
public partial struct UnitAnimationAssignmentSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<UnitDataLibrary>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        BlobAssetReference<UnitLibraryBlob> library = SystemAPI.GetSingleton<UnitDataLibrary>().library;

        new UnitAnimationAssignmentJob
        {
            library = library,
        }.ScheduleParallel();
    }
}

[BurstCompile]
[WithPresent(typeof(Movement))] // must still assign the Death animation after Movement is disabled
// Without this the EnabledRefRW parameter below enrols the flag as enabled-only, and a unit with
// no command pending (every idle unit) silently never matches.
[WithPresent(typeof(AnimationCommandPending))]
public partial struct UnitAnimationAssignmentJob : IJobEntity
{
    [ReadOnly] public BlobAssetReference<UnitLibraryBlob> library;

    public void Execute(
        ref DynamicBuffer<AnimationCommand>   commands,
        EnabledRefRW<AnimationCommandPending> commandPendingEnabled,
        in DynamicBuffer<PlaybackLayer>       playbackLayers,
        in UnitData         unitData,
        in Movement         movement,
        in LocomotionStance locomotionStance)
    {
        int unitIndex = (int)unitData.unitType;
        if (unitIndex < 0 || unitIndex >= library.Value.units.Length)
            return;

        ref UnitDataBlob unitBlob = ref library.Value.units[unitIndex];

        AIUtils.GetLocomotionKeys(ref unitBlob, locomotionStance.stance, out uint idleKey, out uint walkKey);
        uint key = movement.isMoving ? walkKey : idleKey;
        if (key != 0 && !PlaybackApi.IsAnimationPlaying(playbackLayers, key))
        {
            PlaybackApi.PlayAnimation(ref commands, commandPendingEnabled, key);
        }
    }
}
