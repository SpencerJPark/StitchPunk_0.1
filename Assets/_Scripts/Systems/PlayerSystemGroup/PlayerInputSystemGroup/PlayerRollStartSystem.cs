using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using DotsMovementToolkit;

// Turns a buffered roll press into a roll. Runs before PlayerAttackSystem so a roll pressed on the
// same frame as an attack wins, and cancels the player's own in-flight swing (PlayerRoll_System RL-O3).
// PlayerRollMoveSystem owns everything after the start frame.
[BurstCompile]
[UpdateInGroup(typeof(PlayerInputSystemGroup))]
[UpdateBefore(typeof(PlayerAttackSystem))]
public partial struct PlayerRollStartSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Player>();
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // A scene without the narrative singleton behaves as "no cutscene".
        bool cutsceneActive = SystemAPI.TryGetSingletonEntity<NarrativeEventTag>(out Entity narrativeEntity)
            && SystemAPI.IsComponentEnabled<CutsceneActiveTag>(narrativeEntity);

        float deltaTime = SystemAPI.Time.DeltaTime;
        EntityCommandBuffer entityCommandBuffer = SystemAPI
            .GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(state.WorldUnmanaged);

        foreach ((RefRW<OnRollPlayerInput> rollInput, EnabledRefRW<OnRollPlayerInput> onRollPlayerInputEnabled,
                     RefRW<PlayerRoll> playerRoll, EnabledRefRW<PlayerRoll> playerRollEnabled,
                     RefRO<PlayerRollSettings> rollSettings,
                     RefRO<LocalTransform> playerTransform, Entity playerEntity) in
                 SystemAPI.Query<
                         RefRW<OnRollPlayerInput>,
                         EnabledRefRW<OnRollPlayerInput>,
                         RefRW<PlayerRoll>,
                         EnabledRefRW<PlayerRoll>,
                         RefRO<PlayerRollSettings>,
                         RefRO<LocalTransform>>()
                     .WithAll<Player>()
                     .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                     .WithEntityAccess())
        {
            if (!onRollPlayerInputEnabled.ValueRO) continue;

            bool rollCoolingDown = SystemAPI.IsComponentEnabled<RollCooldown>(playerEntity);
            bool rollAllowedNow = !playerRollEnabled.ValueRO
                && !rollCoolingDown
                && !cutsceneActive
                && PlayerCanStartRoll(ref state, playerEntity);

            if (!rollAllowedNow)
            {
                // Buffered (RL-O2): the window is measured against landing, not against the end of the cooldown
                // (which is longer than the window). A press within the window of landing, or any press during
                // the cooldown, holds until the roll is allowed. Only a press made too early counts down and drops.
                float secondsUntilLanding = playerRollEnabled.ValueRO
                    ? math.max(0f, rollSettings.ValueRO.rollDuration - playerRoll.ValueRO.elapsed)
                    : 0f;
                bool blockedOnlyByRollTiming = (playerRollEnabled.ValueRO || rollCoolingDown)
                    && !cutsceneActive && PlayerCanStartRoll(ref state, playerEntity);
                if (blockedOnlyByRollTiming && secondsUntilLanding <= rollInput.ValueRO.bufferRemaining)
                    continue;

                rollInput.ValueRW.bufferRemaining -= deltaTime;
                if (rollInput.ValueRO.bufferRemaining <= 0f)
                {
                    rollInput.ValueRW.bufferRemaining = 0f;
                    onRollPlayerInputEnabled.ValueRW = false;
                }
                continue;
            }

            onRollPlayerInputEnabled.ValueRW = false;

            float3 rollDirection = ResolveRollDirection(ref state, playerEntity, playerTransform.ValueRO);

            playerRoll.ValueRW = new PlayerRoll
            {
                direction = rollDirection,
                elapsed = 0f,
                restoreMoveSpeed = SystemAPI.GetComponent<Movement>(playerEntity).moveSpeed,
                afterimagesSpawned = 0,
                landingSquashRemaining = 0f,
                attackQueued = false,
            };
            playerRollEnabled.ValueRW = true;
            SystemAPI.SetComponentEnabled<DodgeInvulnerable>(playerEntity, true);

            CancelOwnSwingAndAim(ref state, playerEntity);

            float3 feetPosition = playerTransform.ValueRO.Position;
            SoundUtil.PlayOn(ref entityCommandBuffer, SoundType.RollWhoosh, playerEntity);
            VfxUtil.Play(ref entityCommandBuffer, VfxType.RollDustStart, feetPosition, rollDirection);
        }
    }

    private bool PlayerCanStartRoll(ref SystemState state, Entity playerEntity)
    {
        if (SystemAPI.GetComponent<PlayerActionMap>(playerEntity).activeActionMap != ActionMaps.Player)
            return false;
        if (SystemAPI.HasComponent<Dead>(playerEntity) && SystemAPI.IsComponentEnabled<Dead>(playerEntity))
            return false;
        if (SystemAPI.HasComponent<PlayerKnockedOut>(playerEntity)
            && SystemAPI.IsComponentEnabled<PlayerKnockedOut>(playerEntity))
            return false;
        if (SystemAPI.HasComponent<WorkstationMounted>(playerEntity)
            && SystemAPI.IsComponentEnabled<WorkstationMounted>(playerEntity))
            return false;
        return SystemAPI.IsComponentEnabled<Movement>(playerEntity);
    }

    // Held move input wins; standing still rolls the way the player faces (RL-D4).
    private float3 ResolveRollDirection(ref SystemState state, Entity playerEntity, LocalTransform playerTransform)
    {
        if (SystemAPI.IsComponentEnabled<MovePlayerInput>(playerEntity))
        {
            float2 moveInput = SystemAPI.GetComponent<MovePlayerInput>(playerEntity).moveInput;
            float3 inputDirection = new float3(moveInput.x, 0f, moveInput.y);
            if (math.lengthsq(inputDirection) > 0.0001f)
                return math.normalize(inputDirection);
        }

        float3 facingDirection = playerTransform.Forward();
        facingDirection.y = 0f;
        return math.lengthsq(facingDirection) > 0.0001f ? math.normalize(facingDirection) : new float3(0f, 0f, 1f);
    }

    private void CancelOwnSwingAndAim(ref SystemState state, Entity playerEntity)
    {
        // The swing hasn't landed yet, so cancelling the request means it never does.
        if (SystemAPI.HasComponent<AttackRequest>(playerEntity))
            SystemAPI.SetComponentEnabled<AttackRequest>(playerEntity, false);
        if (SystemAPI.HasComponent<AttackCooldown>(playerEntity))
            SystemAPI.SetComponentEnabled<AttackCooldown>(playerEntity, false);
        if (SystemAPI.HasComponent<AimPlayerInput>(playerEntity))
            SystemAPI.SetComponentEnabled<AimPlayerInput>(playerEntity, false);
    }
}
