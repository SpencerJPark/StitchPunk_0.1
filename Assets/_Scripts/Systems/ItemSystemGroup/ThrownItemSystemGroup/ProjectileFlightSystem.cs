using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Moves bullets in a straight XZ line and runs the shared swept hit test (SmartHumanAI H5-D4/D6); friendly fire is on (RC-D1).
// Same main-thread DamageBus write as ThrownItemHitSystem: ItemSystemGroup runs before every parallel raw producer.
[BurstCompile]
[UpdateInGroup(typeof(ThrownItemSystemGroup))]
[UpdateAfter(typeof(ThrownItemHitSystem))]
public partial struct ProjectileFlightSystem : ISystem
{
    private ComponentLookup<Dead> deadLookup;
    private ComponentLookup<DodgeInvulnerable> dodgeInvulnerableLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<DamageBus>();
        state.RequireForUpdate<AttackLibrary>();
        state.RequireForUpdate<Projectile>();
        deadLookup = state.GetComponentLookup<Dead>(true);
        dodgeInvulnerableLookup = state.GetComponentLookup<DodgeInvulnerable>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        deadLookup.Update(ref state);
        dodgeInvulnerableLookup.Update(ref state);

        float deltaTime = SystemAPI.Time.DeltaTime;
        BlobAssetReference<AttackLibraryBlob> attackLibrary = SystemAPI.GetSingleton<AttackLibrary>().library;
        NativeQueue<DamageEvent> damageQueue = SystemAPI.GetSingleton<DamageBus>().raw;

        NativeList<ProjectileHitCandidate> candidates = new NativeList<ProjectileHitCandidate>(64, state.WorldUpdateAllocator);
        foreach ((RefRO<LocalTransform> transform, Entity entity) in
            SystemAPI.Query<RefRO<LocalTransform>>()
                .WithAll<Health>()
                .WithEntityAccess())
        {
            if (deadLookup.HasComponent(entity) && deadLookup.IsComponentEnabled(entity)) continue;
            if (dodgeInvulnerableLookup.HasComponent(entity) && dodgeInvulnerableLookup.IsComponentEnabled(entity)) continue;
            candidates.Add(new ProjectileHitCandidate { entity = entity, position = transform.ValueRO.Position });
        }
        NativeArray<ProjectileHitCandidate> candidateArray = candidates.AsArray();

        foreach ((RefRW<LocalTransform> transform,
                  RefRO<Projectile> projectile,
                  EnabledRefRW<Despawn> despawnEnabled) in
            SystemAPI.Query<RefRW<LocalTransform>, RefRO<Projectile>, EnabledRefRW<Despawn>>()
                .WithPresent<Despawn>())
        {
            if (despawnEnabled.ValueRO) continue;

            float3 previousPosition = transform.ValueRO.Position;
            float3 currentPosition  = previousPosition + projectile.ValueRO.velocity * deltaTime;
            transform.ValueRW.Position = currentPosition;

            if (!ProjectileHitUtils.FindFirstHitAlongSegment(
                    previousPosition, currentPosition, projectile.ValueRO.radius,
                    projectile.ValueRO.shooter, projectile.ValueRO.origin,
                    candidateArray, out int hitCandidateIndex, out float hitFraction))
                continue;

            despawnEnabled.ValueRW = true;

            int attackIndex = (int)projectile.ValueRO.damageSource;
            if (attackIndex <= 0 || attackIndex >= attackLibrary.Value.attacks.Length) continue;
            ref AttackBlob attackBlob = ref attackLibrary.Value.attacks[attackIndex];

            ProjectileHitCandidate victim = candidateArray[hitCandidateIndex];
            float3 hitPoint = math.lerp(previousPosition, currentPosition, hitFraction);
            damageQueue.Enqueue(new DamageEvent
            {
                targetEntity    = victim.entity,
                sourceEntity    = projectile.ValueRO.shooter,
                damageSource    = projectile.ValueRO.damageSource,
                damageAmount    = attackBlob.damageAmount,
                distance        = math.distance(hitPoint.xz, victim.position.xz),
                ragdollForce    = attackBlob.ragdollForce,
                launchForceY    = attackBlob.launchForceY,
                launchForceX    = attackBlob.launchForceX,
                flailIntensity  = attackBlob.flailIntensity,
                spin            = attackBlob.spin,
                restitution     = attackBlob.restitution,
                damageBehaviour = DamageBehaviour.SinlgeTarget,
                sourcePosition  = hitPoint,
                range           = 0f,
            });
        }
    }
}
