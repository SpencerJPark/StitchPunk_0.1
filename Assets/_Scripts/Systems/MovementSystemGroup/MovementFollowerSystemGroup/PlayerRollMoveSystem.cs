using DotsMovementToolkit;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

// Drives a roll started by PlayerRollStartSystem. Runs after PlayerMoveSystem so its target and
// speed win, and lets the toolkit's UnitMoverSystem do the moving so walls still slide (RL-D2).
// Also owns the end of the roll (i-frames off, cooldown, queued attack) and the afterimage spawns.
[BurstCompile]
[UpdateInGroup(typeof(MovementFollowerSystemGroup))]
[UpdateAfter(typeof(PlayerMoveSystem))]
public partial struct PlayerRollMoveSystem : ISystem
{
    // Far enough that UnitMoverSystem never "arrives" mid-roll; the per-frame speed caps the step.
    private const float ROLL_TARGET_LOOKAHEAD = 50f;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Player>();
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        if (deltaTime <= 0f) return;

        // A scene without the narrative singleton behaves as "no cutscene".
        bool cutsceneActive = SystemAPI.TryGetSingletonEntity<NarrativeEventTag>(out Entity narrativeEntity)
            && SystemAPI.IsComponentEnabled<CutsceneActiveTag>(narrativeEntity);

        EntityCommandBuffer entityCommandBuffer = SystemAPI
            .GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(state.WorldUnmanaged);

        foreach ((RefRW<PlayerRoll> playerRoll, EnabledRefRW<PlayerRoll> playerRollEnabled,
                     RefRW<RollCooldown> rollCooldown, EnabledRefRW<RollCooldown> rollCooldownEnabled,
                     RefRO<PlayerRollSettings> rollSettings, RefRO<LocalTransform> playerTransform,
                     Entity playerEntity) in
                 SystemAPI.Query<
                         RefRW<PlayerRoll>,
                         EnabledRefRW<PlayerRoll>,
                         RefRW<RollCooldown>,
                         EnabledRefRW<RollCooldown>,
                         RefRO<PlayerRollSettings>,
                         RefRO<LocalTransform>>()
                     .WithAll<Player>()
                     .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                     .WithEntityAccess())
        {
            if (rollCooldownEnabled.ValueRO)
            {
                rollCooldown.ValueRW.remaining -= deltaTime;
                if (rollCooldown.ValueRO.remaining <= 0f)
                {
                    rollCooldown.ValueRW.remaining = 0f;
                    rollCooldownEnabled.ValueRW = false;
                }
            }

            if (!playerRollEnabled.ValueRO) continue;

            PlayerRollSettings settings = rollSettings.ValueRO;
            RefRW<Movement> movement = SystemAPI.GetComponentRW<Movement>(playerEntity);

            bool interrupted = cutsceneActive || PlayerIsDownOrDead(ref state, playerEntity);
            if (interrupted)
            {
                EndRoll(ref state, playerEntity, playerRoll, playerRollEnabled, movement, playerTransform.ValueRO,
                    queueAttack: false, playLanding: false, ref entityCommandBuffer);
                continue;
            }

            // End a frame after the last step: that step's speed has to survive until UnitMoverSystem runs.
            if (playerRoll.ValueRO.elapsed >= settings.rollDuration)
            {
                EndRoll(ref state, playerEntity, playerRoll, playerRollEnabled, movement, playerTransform.ValueRO,
                    queueAttack: playerRoll.ValueRO.attackQueued, playLanding: true, ref entityCommandBuffer);
                rollCooldown.ValueRW.remaining = settings.cooldownAfterRoll;
                rollCooldownEnabled.ValueRW = settings.cooldownAfterRoll > 0f;
                playerRoll.ValueRW.landingSquashRemaining = settings.landingSquashDuration;
                continue;
            }

            float previousFraction = math.saturate(playerRoll.ValueRO.elapsed / settings.rollDuration);
            playerRoll.ValueRW.elapsed += deltaTime;
            float currentFraction = math.saturate(playerRoll.ValueRO.elapsed / settings.rollDuration);

            // Exact distance for this frame from the integral of the ease-out curve (RL-D3), so the roll
            // covers rollDistance at any frame rate.
            float distanceThisFrame = settings.rollDistance
                * (RollDistanceCovered(currentFraction, settings.speedCurvePower)
                   - RollDistanceCovered(previousFraction, settings.speedCurvePower));
            movement.ValueRW.moveSpeed = distanceThisFrame / deltaTime;
            movement.ValueRW.isRunning = false;
            movement.ValueRW.targetPosition = playerTransform.ValueRO.Position
                + playerRoll.ValueRO.direction * ROLL_TARGET_LOOKAHEAD;

            if (currentFraction >= settings.invulnerableUntilFraction)
                SystemAPI.SetComponentEnabled<DodgeInvulnerable>(playerEntity, false);

            int nextAfterimage = playerRoll.ValueRO.afterimagesSpawned;
            if (nextAfterimage < settings.afterimageCount
                && currentFraction >= (nextAfterimage + 1f) / (settings.afterimageCount + 1f))
            {
                SpawnAfterimages(ref state, playerEntity, settings, ref entityCommandBuffer);
                playerRoll.ValueRW.afterimagesSpawned = nextAfterimage + 1;
            }

        }
    }

    // Fraction of the roll's distance covered at normalized time t for speed(t) = peak * (1 - t)^power.
    public static float RollDistanceCovered(float normalizedTime, float speedCurvePower)
    {
        return 1f - math.pow(1f - math.saturate(normalizedTime), speedCurvePower + 1f);
    }

    private bool PlayerIsDownOrDead(ref SystemState state, Entity playerEntity)
    {
        if (SystemAPI.HasComponent<Dead>(playerEntity) && SystemAPI.IsComponentEnabled<Dead>(playerEntity))
            return true;
        return SystemAPI.HasComponent<PlayerKnockedOut>(playerEntity)
            && SystemAPI.IsComponentEnabled<PlayerKnockedOut>(playerEntity);
    }

    private void EndRoll(ref SystemState state, Entity playerEntity, RefRW<PlayerRoll> playerRoll,
        EnabledRefRW<PlayerRoll> playerRollEnabled, RefRW<Movement> movement, LocalTransform playerTransform,
        bool queueAttack, bool playLanding, ref EntityCommandBuffer entityCommandBuffer)
    {
        movement.ValueRW.moveSpeed = playerRoll.ValueRO.restoreMoveSpeed;
        movement.ValueRW.targetPosition = playerTransform.Position;
        playerRollEnabled.ValueRW = false;
        playerRoll.ValueRW.attackQueued = false;
        SystemAPI.SetComponentEnabled<DodgeInvulnerable>(playerEntity, false);

        // PlayerAttackSystem picks the press up next frame through its normal path (RL-O5).
        if (queueAttack)
            SystemAPI.SetComponentEnabled<OnAttackPlayerInput>(playerEntity, true);

        if (!playLanding) return;
        SoundUtil.PlayOn(ref entityCommandBuffer, SoundType.RollLand, playerEntity);
        VfxUtil.Play(ref entityCommandBuffer, VfxType.RollDustLand, playerTransform.Position,
            playerRoll.ValueRO.direction);
    }

    // Copies every rendered entity under the player's billboarded Visual and freezes the copy at its
    // current world pose (RL-D8). The copy has no LocalTransform, so the transform system leaves its
    // LocalToWorld alone and AfterimageFadeSystem can shrink it in place.
    private void SpawnAfterimages(ref SystemState state, Entity playerEntity, PlayerRollSettings settings,
        ref EntityCommandBuffer entityCommandBuffer)
    {
        Entity visualEntity = FindBillboardedVisual(ref state, playerEntity);
        if (visualEntity == Entity.Null) return;

        NativeList<Entity> pendingEntities = new NativeList<Entity>(8, Allocator.Temp);
        pendingEntities.Add(visualEntity);
        while (pendingEntities.Length > 0)
        {
            Entity currentEntity = pendingEntities[pendingEntities.Length - 1];
            pendingEntities.RemoveAt(pendingEntities.Length - 1);

            if (SystemAPI.HasBuffer<Child>(currentEntity))
            {
                DynamicBuffer<Child> children = SystemAPI.GetBuffer<Child>(currentEntity);
                for (int childIndex = 0; childIndex < children.Length; childIndex++)
                    pendingEntities.Add(children[childIndex].Value);
            }

            if (!SystemAPI.HasComponent<MaterialMeshInfo>(currentEntity)) continue;
            if (!SystemAPI.HasComponent<LocalToWorld>(currentEntity)) continue;
            if (SystemAPI.HasComponent<AfterimageGhost>(currentEntity)) continue;

            LocalToWorld frozenPose = SystemAPI.GetComponent<LocalToWorld>(currentEntity);
            bool sourceHasTint = SystemAPI.HasComponent<BodyPartTint>(currentEntity);

            Entity ghostEntity = entityCommandBuffer.Instantiate(currentEntity);
            entityCommandBuffer.RemoveComponent<Parent>(ghostEntity);
            entityCommandBuffer.RemoveComponent<LocalTransform>(ghostEntity);
            entityCommandBuffer.RemoveComponent<PostTransformMatrix>(ghostEntity);
            entityCommandBuffer.RemoveComponent<VisualBillboard>(ghostEntity);
            entityCommandBuffer.SetComponent(ghostEntity, frozenPose);
            entityCommandBuffer.AddComponent(ghostEntity, new AfterimageGhost
            {
                age = 0f,
                lifetime = math.max(0.01f, settings.afterimageLifetime),
                startPose = frozenPose.Value,
                startTint = settings.afterimageTint,
                hasTint = sourceHasTint,
            });
            if (sourceHasTint)
                entityCommandBuffer.SetComponent(ghostEntity, new BodyPartTint { Value = settings.afterimageTint });
            entityCommandBuffer.AddComponent(ghostEntity,
                new Lifetime { secondsRemaining = math.max(0.01f, settings.afterimageLifetime) });
            entityCommandBuffer.AddComponent(ghostEntity, new Despawn { mode = DespawnMode.ForceDestroy });
            entityCommandBuffer.SetComponentEnabled<Despawn>(ghostEntity, false);
        }
        pendingEntities.Dispose();
    }

    private Entity FindBillboardedVisual(ref SystemState state, Entity playerEntity)
    {
        if (!SystemAPI.HasBuffer<Child>(playerEntity)) return Entity.Null;
        DynamicBuffer<Child> children = SystemAPI.GetBuffer<Child>(playerEntity);
        for (int childIndex = 0; childIndex < children.Length; childIndex++)
        {
            if (SystemAPI.HasComponent<VisualBillboard>(children[childIndex].Value))
                return children[childIndex].Value;
        }
        return Entity.Null;
    }
}
