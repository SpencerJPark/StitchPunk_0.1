using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

// AOE expand pre-pass (v2). Sits between the producers and DamageEventSystem: it drains the raw
// DamageBus queue, expands every AreaOfEffect event into one single-target event per in-range,
// non-source, alive victim, and copies single-target events straight through — writing the result
// into DamageBus.resolved for the consumer to apply.
//
// A NativeQueue handed through a singleton bypasses ECS automatic dependency tracking, so this
// system combines DamageBusSystem.ProducerHandle into its dependency and completes it before reading
// raw on the main thread (the EntityCommandBufferSystem pattern). The heavy per-event work — the
// radius scan for AOE — runs in a Burst ScheduleParallel job; this system then completes that job so
// the immediately-following main-thread consumer can drain `resolved` safely.
//
// Friendly fire: AOE hits EVERYONE in radius except the source entity (no faction filter on damage —
// the faction gate lives in DamageEventSystem's threat step). Cone/Line/Chain have no dedicated
// resolver yet and fall back to single-target on their pre-set targetEntity.
[BurstCompile]
[UpdateInGroup(typeof(CombatReactionSystemGroup))]
[UpdateBefore(typeof(DamageEventSystem))]
public partial struct DamageResolutionSystem : ISystem
{
    private ComponentLookup<Dead> deadLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<DamageBus>();

        deadLookup = state.GetComponentLookup<Dead>(true);
    }

    // Not [BurstCompile] — fetches the managed DamageBusSystem for the manual producer-handle wiring.
    public void OnUpdate(ref SystemState state)
    {
        DamageBus bus = SystemAPI.GetSingleton<DamageBus>();

        // Manual dependency: fold in every producer's write handle, then complete so `raw` is safe to
        // read on the main thread below (producers ScheduleParallel-write it in other system groups).
        state.Dependency = JobHandle.CombineDependencies(
            state.Dependency,
            state.World.GetExistingSystemManaged<DamageBusSystem>().ProducerHandle);
        state.Dependency.Complete();

        NativeArray<DamageEvent> rawEvents = bus.raw.ToArray(Allocator.TempJob);
        if (rawEvents.Length == 0)
        {
            rawEvents.Dispose();
            return;
        }

        // Only gather the target snapshot when an AOE event is actually present — the common
        // all-single-target case skips the gather entirely.
        bool hasAreaOfEffect = false;
        for (int i = 0; i < rawEvents.Length; i++)
        {
            if (rawEvents[i].damageBehaviour == DamageBehaviour.AreaOfEffect)
            {
                hasAreaOfEffect = true;
                break;
            }
        }

        NativeList<DamageTargetSnapshot> targets = new NativeList<DamageTargetSnapshot>(
            hasAreaOfEffect ? 64 : 0, Allocator.TempJob);

        if (hasAreaOfEffect)
        {
            foreach ((RefRO<LocalTransform> transform, Entity entity) in
                SystemAPI.Query<RefRO<LocalTransform>>().WithAll<Health>().WithEntityAccess())
            {
                targets.Add(new DamageTargetSnapshot
                {
                    entity   = entity,
                    position = transform.ValueRO.Position,
                });
            }
        }

        deadLookup.Update(ref state);

        JobHandle expandHandle = new DamageExpandJob
        {
            events         = rawEvents,
            targets        = targets.AsArray(),
            deadLookup     = deadLookup,
            resolvedWriter = bus.resolved.AsParallelWriter(),
        }.Schedule(rawEvents.Length, 16, state.Dependency);

        // The consumer drains `resolved` on the main thread right after this, so join here — the
        // expand still parallelised the radius scans across worker threads.
        expandHandle.Complete();

        // raw is copied into rawEvents; clear it now (DamageBusSystem also clears next frame).
        bus.raw.Clear();

        rawEvents.Dispose();
        targets.Dispose();

        state.Dependency = expandHandle;
    }
}

// Entity + position snapshot of a possible AOE victim, gathered on the main thread and read by the
// parallel expand job (top-level so the job struct can reference it).
public struct DamageTargetSnapshot
{
    public Entity entity;
    public float3 position;
}

[BurstCompile]
public struct DamageExpandJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<DamageEvent>         events;
    [ReadOnly] public NativeArray<DamageTargetSnapshot> targets;
    [ReadOnly] public ComponentLookup<Dead>     deadLookup;
    public NativeQueue<DamageEvent>.ParallelWriter resolvedWriter;

    public void Execute(int index)
    {
        DamageEvent damageEvent = events[index];

        if (damageEvent.damageBehaviour != DamageBehaviour.AreaOfEffect)
        {
            // SingleTarget (and, until dedicated resolvers exist, Cone/Line/Chain/None) flow straight
            // through on their pre-resolved targetEntity — normalised so the consumer sees one shape.
            damageEvent.damageBehaviour = DamageBehaviour.SinlgeTarget;
            resolvedWriter.Enqueue(damageEvent);
            return;
        }

        // AreaOfEffect — one single-target event per in-range, non-source, alive victim. XZ radius
        // (2.5D: ignore height). Friendly fire: no faction filter, only the source is exempt.
        float rangeSq = damageEvent.range * damageEvent.range;
        for (int t = 0; t < targets.Length; t++)
        {
            Entity candidate = targets[t].entity;

            if (candidate == damageEvent.sourceEntity)
                continue;

            if (deadLookup.HasComponent(candidate) && deadLookup.IsComponentEnabled(candidate))
                continue;

            float deltaX = targets[t].position.x - damageEvent.sourcePosition.x;
            float deltaZ = targets[t].position.z - damageEvent.sourcePosition.z;
            float distanceSq = deltaX * deltaX + deltaZ * deltaZ;
            if (distanceSq > rangeSq)
                continue;

            DamageEvent perTarget = damageEvent;
            perTarget.targetEntity    = candidate;
            perTarget.damageBehaviour = DamageBehaviour.SinlgeTarget;
            perTarget.distance        = math.sqrt(distanceSq);
            resolvedWriter.Enqueue(perTarget);
        }
    }
}
