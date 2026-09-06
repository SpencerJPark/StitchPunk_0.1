using DotsAnimationToolkit;
using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;

// The game's answer to the toolkit's detach signal (A63): the toolkit hands over a world impulse and
// applies no physics of its own, because a sellable package cannot assume a physics stack. An item
// becomes a throw through the existing ThrownItemRequest pipeline; a unit stepping off a cart is
// simply left standing where the detach placed it (G2 §4 — launching a unit is the ragdoll path).
[BurstCompile]
[UpdateInGroup(typeof(CutsceneSystemGroup))]
[UpdateAfter(typeof(CutsceneStartSystem))]
public partial struct CutsceneDetachSystem : ISystem
{
    private ComponentLookup<ThrownItemRequest> _thrownItemRequestLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _thrownItemRequestLookup = state.GetComponentLookup<ThrownItemRequest>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        _thrownItemRequestLookup.Update(ref state);

        // Scheduled single-threaded: a frame detaches a handful of things at most, and each writes
        // its own entity through the lookup, so parallel workers would buy nothing but an attribute.
        state.Dependency = new ConsumeDetachSignalJob
        {
            thrownItemRequestLookup = _thrownItemRequestLookup,
        }.Schedule(state.Dependency);
    }
}

[BurstCompile]
public partial struct ConsumeDetachSignalJob : IJobEntity
{
    public ComponentLookup<ThrownItemRequest> thrownItemRequestLookup;

    // The signal is taken by ref, not in: an `in` parameter alongside EnabledRefRW of the same type
    // makes the generator emit an RO and an RW handle for it, which the job safety system rejects as
    // aliasing at run time — no compile error, just a throw the first time the job is scheduled.
    private void Execute(
        Entity detachedEntity,
        ref CutsceneDetachSignal cutsceneDetachSignal,
        in LocalTransform localTransform,
        EnabledRefRW<CutsceneDetachSignal> cutsceneDetachSignalEnabled)
    {
        // The same shape PlayerUnequipSystem writes, so a cutscene throw and a hand throw land in
        // ThrownItemSystem/ThrownItemHitSystem indistinguishable from each other.
        if (thrownItemRequestLookup.HasComponent(detachedEntity))
        {
            thrownItemRequestLookup[detachedEntity] = new ThrownItemRequest
            {
                velocity    = cutsceneDetachSignal.worldImpulse,
                thrower     = cutsceneDetachSignal.previousHost,
                throwOrigin = localTransform.Position,
            };
            thrownItemRequestLookup.SetComponentEnabled(detachedEntity, true);
        }

        cutsceneDetachSignalEnabled.ValueRW = false;
    }
}
