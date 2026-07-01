using Unity.Collections;
using Unity.Entities;

// Recycled damage transport (v2). Replaces the per-hit DamageEvent-entity spawn/destroy of v1.
// Producers Enqueue raw DamageEvent values via raw.AsParallelWriter(); DamageResolutionSystem
// expands AOE into single-target events in `resolved`; DamageEventSystem drains `resolved`.
// Owned/created + disposed by DamageBusSystem (Allocator.Persistent) — never inspectable in the
// Entities window (it's a queue, not an entity). A NativeQueue passed via a singleton BYPASSES ECS
// automatic dependency tracking, so producer jobs MUST register with
// DamageBusSystem.AddJobHandleForProducer and DamageResolutionSystem MUST combine ProducerHandle
// before reading — exactly the EntityCommandBufferSystem pattern.
//
// (The plan's producer-written aoeCount was dropped: incrementing a NativeReference from the
// parallel producer jobs is a data race. DamageResolutionSystem instead detects AOE presence by
// scanning the drained raw array on the main thread — which it drains for the expand pass anyway.)
public struct DamageBus : IComponentData
{
    public NativeQueue<DamageEvent> raw;        // producers Enqueue here
    public NativeQueue<DamageEvent> resolved;   // AOE expand writes single-target here; consumer drains
}
