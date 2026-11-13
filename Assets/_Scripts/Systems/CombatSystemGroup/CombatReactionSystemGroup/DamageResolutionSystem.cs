using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

// Expands raw AOE events into single-target events in DamageBus.resolved.
// A NativeQueue in a singleton bypasses ECS dependency tracking, so ProducerHandle is combined and
// completed by hand before raw is read.
// AOE hits everyone in radius except the source (faction gate is in DamageEventSystem).
// Cone/Line/Chain fall back to single-target.
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

        // Producers ScheduleParallel-write raw in other groups, so complete before reading it here.
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

        // Skip the target gather when no AOE event is present.
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

        // DamageEventSystem drains `resolved` on the main thread right after this.
        expandHandle.Complete();

        bus.raw.Clear();

        rawEvents.Dispose();
        targets.Dispose();

        state.Dependency = expandHandle;
    }
}

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
            // Everything non-AOE passes through on its pre-set targetEntity, normalised to SinlgeTarget.
            damageEvent.damageBehaviour = DamageBehaviour.SinlgeTarget;
            resolvedWriter.Enqueue(damageEvent);
            return;
        }

        // XZ radius only (2.5D ignores height).
        float rangeSq = damageEvent.range * damageEvent.range;
        for (int t = 0; t < targets.Length; t++)
        {
            Entity candidate = targets[t].entity;

            if (candidate == damageEvent.sourceEntity)
                continue;

            if (EntityUtils.IsDead(in deadLookup, candidate))
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
