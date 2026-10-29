using DotsAnimationToolkit;
using Unity.Burst;
using Unity.Entities;

// Turns vehicle state into clips on the vehicle's own toolkit actor (Caravan_System.md CV-D21): a door's
// Opening/Closing/BurstOpen plays its Open/Close/Slam clip once, the summon shake plays VehicleShakeBuildUp
// or VehicleShakeBurst while it runs, and VehicleBroken plays while the caravan is broken. A key of 0 means no
// clip: DoorSystem / VehiclePoseSystem do it in code instead.
[BurstCompile]
[UpdateInGroup(typeof(AnimationSystemGroup))]
public partial struct VehicleAnimationSystem : ISystem
{
    private BufferLookup<AnimationCommand> animationCommandLookup;
    private ComponentLookup<AnimationCommandPending> animationCommandPendingLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        animationCommandLookup = state.GetBufferLookup<AnimationCommand>();
        animationCommandPendingLookup = state.GetComponentLookup<AnimationCommandPending>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // Main-thread lookup writes to the actors below wait for nothing on their own (Gotchas.md).
        state.CompleteDependency();
        animationCommandLookup.Update(ref state);
        animationCommandPendingLookup.Update(ref state);

        foreach ((RefRW<DoorAnimationKeys> doorAnimationKeys, RefRO<Door> door, RefRO<DoorOwner> doorOwner)
                 in SystemAPI.Query<RefRW<DoorAnimationKeys>, RefRO<Door>, RefRO<DoorOwner>>())
        {
            DoorPhase phase = door.ValueRO.phase;
            if (phase == doorAnimationKeys.ValueRO.lastAnimatedPhase) continue;
            doorAnimationKeys.ValueRW.lastAnimatedPhase = phase;
            Play(doorOwner.ValueRO.owner, DoorClipFor(phase, doorAnimationKeys.ValueRO));
        }

        foreach ((RefRW<VehicleAnimationKeys> vehicleAnimationKeys, RefRO<CaravanShake> caravanShake, Entity vehicleEntity)
                 in SystemAPI.Query<RefRW<VehicleAnimationKeys>, RefRO<CaravanShake>>().WithEntityAccess())
        {
            VehicleAnimationKeys keys = vehicleAnimationKeys.ValueRO;

            uint wantedShake = 0;
            if (caravanShake.ValueRO.remainingSeconds > 0f)
                wantedShake = caravanShake.ValueRO.kind == CaravanShakeKind.Burst ? keys.shakeBurst : keys.shakeBuildUp;
            if (wantedShake != keys.playingShake)
            {
                Stop(vehicleEntity, keys.playingShake);
                Play(vehicleEntity, wantedShake);
                vehicleAnimationKeys.ValueRW.playingShake = wantedShake;
            }

            bool broken = SystemAPI.HasComponent<CaravanBroken>(vehicleEntity) && SystemAPI.IsComponentEnabled<CaravanBroken>(vehicleEntity);
            if (keys.broken != 0 && broken != keys.playingBroken)
            {
                if (broken) Play(vehicleEntity, keys.broken);
                else Stop(vehicleEntity, keys.broken);
                vehicleAnimationKeys.ValueRW.playingBroken = broken;
            }
        }
    }

    // Open and Closed are the clips' own end poses, so settling into them sends nothing.
    public static uint DoorClipFor(DoorPhase phase, in DoorAnimationKeys keys)
    {
        switch (phase)
        {
            case DoorPhase.Opening: return keys.open;
            case DoorPhase.Closing: return keys.close;
            case DoorPhase.BurstOpen: return keys.slam;
            default: return 0;
        }
    }

    private void Play(Entity actor, uint animationKey)
    {
        if (animationKey == 0 || !animationCommandLookup.HasBuffer(actor) || !animationCommandPendingLookup.HasComponent(actor)) return;
        DynamicBuffer<AnimationCommand> commands = animationCommandLookup[actor];
        PlaybackApi.PlayAnimation(ref commands, animationCommandPendingLookup.GetEnabledRefRW<AnimationCommandPending>(actor), animationKey);
    }

    private void Stop(Entity actor, uint animationKey)
    {
        if (animationKey == 0 || !animationCommandLookup.HasBuffer(actor) || !animationCommandPendingLookup.HasComponent(actor)) return;
        DynamicBuffer<AnimationCommand> commands = animationCommandLookup[actor];
        PlaybackApi.StopAnimation(ref commands, animationCommandPendingLookup.GetEnabledRefRW<AnimationCommandPending>(actor), animationKey);
    }
}
