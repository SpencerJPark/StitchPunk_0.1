using DotsMovementToolkit;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

// Hostile units body-block the walking player; a roll passes straight through them (PlayerRoll_System
// RL-O6/RL-D10). Physics layers can't do this: enemies and minions share the Units layer and the player's
// collider ignores it entirely. So after UnitMoverSystem has moved the player, push them back out of any
// hostile in range, capped per frame so a roll that ends inside an enemy eases out instead of snapping.
[BurstCompile]
[UpdateInGroup(typeof(MovementExecutionSystemGroup))]
[UpdateAfter(typeof(UnitMoverSystem))]
public partial struct PlayerBodyBlockSystem : ISystem
{
    // TagManager layer "Units", the layer every unit root's capsule is baked on.
    private const int UNITS_PHYSICS_LAYER = 6;
    // Roughly a unit capsule's mid-height, so the overlap sphere meets the capsule, not the floor.
    private const float OVERLAP_HEIGHT = 0.9f;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Player>();
        state.RequireForUpdate<PhysicsWorldSingleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        if (deltaTime <= 0f) return;

        CollisionWorld collisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;
        CollisionFilter unitsOnlyFilter = new CollisionFilter
        {
            BelongsTo = ~0u,
            CollidesWith = 1u << UNITS_PHYSICS_LAYER,
        };
        NativeList<DistanceHit> nearbyUnitHits = new NativeList<DistanceHit>(16, Allocator.Temp);

        foreach ((RefRW<LocalTransform> playerTransform, RefRO<PlayerRollSettings> rollSettings,
                     RefRO<PhysicsCollider> playerCollider, Entity playerEntity) in
                 SystemAPI.Query<RefRW<LocalTransform>, RefRO<PlayerRollSettings>, RefRO<PhysicsCollider>>()
                     .WithAll<Player>()
                     .WithDisabled<PlayerRoll>()
                     .WithEntityAccess())
        {
            if (SystemAPI.HasComponent<Dead>(playerEntity) && SystemAPI.IsComponentEnabled<Dead>(playerEntity))
                continue;

            float blockRadius = rollSettings.ValueRO.bodyBlockRadius;
            if (blockRadius <= 0f) continue;

            float3 playerPosition = playerTransform.ValueRO.Position;
            nearbyUnitHits.Clear();
            collisionWorld.OverlapSphere(playerPosition + new float3(0f, OVERLAP_HEIGHT, 0f), blockRadius,
                ref nearbyUnitHits, unitsOnlyFilter);

            float3 totalPushOut = float3.zero;
            for (int hitIndex = 0; hitIndex < nearbyUnitHits.Length; hitIndex++)
            {
                DistanceHit unitHit = nearbyUnitHits[hitIndex];
                if (unitHit.Entity == playerEntity) continue;
                if (!UnitIsHostileToPlayer(ref state, unitHit.Entity)) continue;

                // The body's pose, not LocalTransform: this query already holds LocalTransform read-write.
                float3 unitPosition = collisionWorld.Bodies[unitHit.RigidBodyIndex].WorldFromBody.pos;
                float3 awayFromUnit = playerPosition - unitPosition;
                awayFromUnit.y = 0f;
                float centreDistance = math.length(awayFromUnit);
                if (centreDistance >= blockRadius) continue;

                // Standing exactly on the unit's centre: back out the way the player came from.
                float3 pushDirection = centreDistance > 0.0001f
                    ? awayFromUnit / centreDistance
                    : -math.normalizesafe(new float3(playerTransform.ValueRO.Forward().x, 0f,
                        playerTransform.ValueRO.Forward().z), new float3(0f, 0f, -1f));
                totalPushOut += pushDirection * (blockRadius - centreDistance);
            }

            float pushLength = math.length(totalPushOut);
            if (pushLength < 0.0001f) continue;

            float maxPushThisFrame = rollSettings.ValueRO.bodyBlockPushSpeed * deltaTime;
            if (pushLength > maxPushThisFrame)
                totalPushOut *= maxPushThisFrame / pushLength;

            // Cast with the player's own collider, which ignores units, so the push stops at walls.
            ColliderCastInput pushCast = new ColliderCastInput(playerCollider.ValueRO.Value, playerPosition,
                playerPosition + totalPushOut, playerTransform.ValueRO.Rotation);
            if (collisionWorld.CastCollider(pushCast, out ColliderCastHit wallHit))
                totalPushOut *= math.max(0f, wallHit.Fraction - 0.01f);

            playerTransform.ValueRW.Position = playerPosition + totalPushOut;
        }

        nearbyUnitHits.Dispose();
    }

    // Same test DamageEventSystem.SourceHostileToTarget uses, seen from the unit: it wants to attack the player.
    private bool UnitIsHostileToPlayer(ref SystemState state, Entity unitEntity)
    {
        if (SystemAPI.HasComponent<Dead>(unitEntity) && SystemAPI.IsComponentEnabled<Dead>(unitEntity))
            return false;
        if (!SystemAPI.HasBuffer<AttackFaction>(unitEntity)) return false;

        DynamicBuffer<AttackFaction> attackFactions = SystemAPI.GetBuffer<AttackFaction>(unitEntity);
        for (int factionIndex = 0; factionIndex < attackFactions.Length; factionIndex++)
        {
            if (attackFactions[factionIndex].faction == FactionType.Player)
                return true;
        }
        return false;
    }
}
