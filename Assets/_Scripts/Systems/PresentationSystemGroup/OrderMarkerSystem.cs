using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;

// Hides the destination marker for a horde group once the order is complete.
// An order is considered complete when no member of the horde still has PlayerControlled enabled —
// meaning every minion has either arrived and released control, or was interrupted.
//
// MinionCommandSystem is responsible for showing and repositioning the marker when a new order
// is issued. This system only handles the hide side.
[BurstCompile]
[UpdateInGroup(typeof(LateSimulationSystemGroup))]
public partial struct OrderMarkerSystem : ISystem
{
    private ComponentLookup<PlayerControlled> playerControlledLookup;
    private ComponentLookup<LocalTransform>   localTransformLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        playerControlledLookup = state.GetComponentLookup<PlayerControlled>(true);
        localTransformLookup   = state.GetComponentLookup<LocalTransform>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        playerControlledLookup.Update(ref state);
        localTransformLookup.Update(ref state);

        foreach (RefRO<Horde> horde in SystemAPI.Query<RefRO<Horde>>())
        {
            Entity markerEntity = horde.ValueRO.markerEntity;
            if (markerEntity == Entity.Null) continue;
            if (!localTransformLookup.HasComponent(markerEntity)) continue;

            // Only check hordes whose marker is currently visible (scale > 0).
            if (localTransformLookup[markerEntity].Scale <= 0f) continue;

            // Hide the marker once no member remains under player control.
            if (!AnyMemberIsPlayerControlled(ref state, horde.ValueRO))
            {
                LocalTransform markerTransform = localTransformLookup[markerEntity];
                markerTransform.Scale          = 0f;
                localTransformLookup[markerEntity] = markerTransform;
            }
        }
    }

    // Scans all minion bodies in this horde and returns true if any brain still has
    // PlayerControlled enabled (meaning the order is still active for that unit).
    private bool AnyMemberIsPlayerControlled(ref SystemState state, in Horde horde)
    {
        foreach ((RefRO<HordeMembership> membership, RefRO<BrainLink> brainLink) in
            SystemAPI.Query<RefRO<HordeMembership>, RefRO<BrainLink>>()
            .WithAll<Minion>())
        {
            if (membership.ValueRO.hordeId != horde.hordeId) continue;

            Entity brainEntity = brainLink.ValueRO.brain;
            if (brainEntity == Entity.Null) continue;
            if (!playerControlledLookup.HasComponent(brainEntity)) continue;
            if (playerControlledLookup.IsComponentEnabled(brainEntity)) return true;
        }
        return false;
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state) { }
}
