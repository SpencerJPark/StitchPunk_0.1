using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Environmental / sourceless damage producer (v2 example). For each HazardZone, once its whole-zone
// retrigger gate has elapsed, Enqueues a Hazard DamageEvent (sourceEntity = Null → no threat) for
// every alive unit within `radius` (XZ), then stamps the zone's lastTriggerTime.
//
// Runs UpdateBefore AttackRequestSystem so this synchronous main-thread write to DamageBus.raw never
// overlaps the parallel melee enqueue job (which is scheduled after and registered with the bus).
// ThrownItemHitSystem (ItemSystemGroup) already wrote synchronously earlier in the frame, so there is
// no outstanding raw-queue job when this runs — no handle registration needed.
[BurstCompile]
[UpdateInGroup(typeof(CombatExecutionSystemGroup))]
[UpdateBefore(typeof(AttackRequestSystem))]
public partial struct HazardZoneSystem : ISystem
{
    private ComponentLookup<Dead> deadLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<DamageBus>();
        state.RequireForUpdate<HazardZone>();

        deadLookup = state.GetComponentLookup<Dead>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        deadLookup.Update(ref state);

        NativeQueue<DamageEvent> damageQueue = SystemAPI.GetSingleton<DamageBus>().raw;
        float elapsedTime = (float)SystemAPI.Time.ElapsedTime;

        // Gather alive hittable units (Health + position) once.
        NativeList<HazardTarget> targets = new NativeList<HazardTarget>(64, state.WorldUpdateAllocator);
        foreach ((RefRO<LocalTransform> transform, Entity entity) in
            SystemAPI.Query<RefRO<LocalTransform>>().WithAll<Health>().WithEntityAccess())
        {
            if (deadLookup.HasComponent(entity) && deadLookup.IsComponentEnabled(entity))
                continue;
            targets.Add(new HazardTarget { entity = entity, position = transform.ValueRO.Position });
        }

        if (targets.Length == 0)
            return;

        foreach ((RefRO<LocalTransform> hazardTransform, RefRW<HazardZone> hazardRef) in
            SystemAPI.Query<RefRO<LocalTransform>, RefRW<HazardZone>>())
        {
            HazardZone hazard = hazardRef.ValueRO;

            // Whole-zone retrigger gate — the zone fires at most once per retriggerInterval.
            if (elapsedTime - hazard.lastTriggerTime < hazard.retriggerInterval)
                continue;

            float3 hazardPos = hazardTransform.ValueRO.Position;
            float  radiusSq  = hazard.radius * hazard.radius;
            bool   fired     = false;

            for (int i = 0; i < targets.Length; i++)
            {
                float deltaX = targets[i].position.x - hazardPos.x;
                float deltaZ = targets[i].position.z - hazardPos.z;
                float distanceSq = deltaX * deltaX + deltaZ * deltaZ;
                if (distanceSq > radiusSq)
                    continue;

                damageQueue.Enqueue(new DamageEvent
                {
                    targetEntity    = targets[i].entity,
                    sourceEntity    = Entity.Null,
                    damageSource    = hazard.damageSource,
                    damageAmount    = hazard.damageAmount,
                    distance        = math.sqrt(distanceSq),
                    hitSourceX      = hazardPos.x,   // §7: tip away from the hazard
                    ragdollForce    = hazard.ragdollForce,
                    launchForceY    = hazard.launchForceY,
                    launchForceX    = hazard.launchForceX,
                    damageBehaviour = DamageBehaviour.SinlgeTarget,
                    sourcePosition  = hazardPos,
                    range           = 0f,
                });
                fired = true;
            }

            if (fired)
            {
                hazard.lastTriggerTime = elapsedTime;
                hazardRef.ValueRW = hazard;
            }
        }
    }

    private struct HazardTarget
    {
        public Entity entity;
        public float3 position;
    }
}
