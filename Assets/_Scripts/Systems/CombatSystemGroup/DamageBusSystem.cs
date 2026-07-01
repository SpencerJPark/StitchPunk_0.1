using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

// Managed owner of the recycled DamageBus queues — the EntityCommandBufferSystem pattern for a
// NativeQueue transport. Runs OrderFirst in GameManagerSystemGroup so the per-frame reset lands
// before ANY producer enqueues (thrown items in ItemSystemGroup run earlier in the frame than
// combat, so the bus must be reset ahead of the whole SimulationSystemGroup).
//
// A NativeQueue handed out through a singleton bypasses ECS automatic dependency tracking, so this
// system also carries the combined producer JobHandle: every producer that ScheduleParallel-writes
// the queue calls AddJobHandleForProducer(state.Dependency); DamageResolutionSystem combines
// ProducerHandle before its read. Managed (SystemBase) because it owns native containers + the
// handle; all hot work stays in the Burst producer/resolution/consumer jobs.
[UpdateInGroup(typeof(GameManagerSystemGroup), OrderFirst = true)]
public partial class DamageBusSystem : SystemBase
{
    private NativeQueue<DamageEvent> raw;
    private NativeQueue<DamageEvent> resolved;
    private JobHandle                producerHandle;

    // Combined dependency of every producer that wrote the raw queue this frame.
    public JobHandle ProducerHandle => producerHandle;

    // Producers call this after scheduling their enqueue job (ECB-owner pattern).
    public void AddJobHandleForProducer(JobHandle producerDependency)
    {
        producerHandle = JobHandle.CombineDependencies(producerHandle, producerDependency);
    }

    protected override void OnCreate()
    {
        RequireForUpdate<GameSceneTag>();

        raw      = new NativeQueue<DamageEvent>(Allocator.Persistent);
        resolved = new NativeQueue<DamageEvent>(Allocator.Persistent);

        Entity singleton = EntityManager.CreateEntity();
        EntityManager.SetName(singleton, "DamageBus");
        EntityManager.AddComponentData(singleton, new DamageBus
        {
            raw      = raw,
            resolved = resolved,
        });
    }

    protected override void OnUpdate()
    {
        // Last frame's producers/resolution/consumer are all long complete by now (the consumer
        // drains on the main thread mid-frame), but complete defensively before reusing the queues.
        producerHandle.Complete();

        raw.Clear();
        resolved.Clear();
        producerHandle = default;
    }

    protected override void OnDestroy()
    {
        producerHandle.Complete();

        if (raw.IsCreated)      raw.Dispose();
        if (resolved.IsCreated) resolved.Dispose();
    }
}
