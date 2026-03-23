using Unity.Burst;
using Unity.Collections;
using Unity.Entities;


/// <summary>
/// Runs first in AIExecutionSystemGroup every frame.
///
/// Any brain whose ActiveBrain component is disabled (dead, pooled, deactivated) is
/// considered "inactive". If that brain is still registered as an occupant of an
/// interaction, this system evicts it before the execution jobs run — preventing
/// execution systems from pathing, timing, or completing interactions on behalf of
/// a brain that should no longer be doing anything.
///
/// When the last occupant is evicted the interaction is fully restored to its
/// available state (InteractionProvider re-enabled, InteractionHandled and
/// InteractionTimer disabled) so other units can claim it.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(AIExecutionSystemGroup), OrderFirst = true)]
public partial struct InactiveBrainCleanupSystem : ISystem
{
    private ComponentLookup<ActiveBrain>    _activeBrainLookup;
    private ComponentLookup<SelectedAction> _selectedActionLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        _activeBrainLookup    = state.GetComponentLookup<ActiveBrain>(isReadOnly: true);
        _selectedActionLookup = state.GetComponentLookup<SelectedAction>(isReadOnly: false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        _activeBrainLookup.Update(ref state);
        _selectedActionLookup.Update(ref state);

        state.Dependency = new InactiveBrainCleanupJob
        {
            ActiveBrainLookup    = _activeBrainLookup,
            SelectedActionLookup = _selectedActionLookup,
        }.Schedule(state.Dependency);
    }
}

// Iterates currently-occupied interactions (InteractionProvider disabled).
// For each occupant brain that has ActiveBrain disabled:
//   - Removes it from the occupant buffer
//   - Clears its SelectedAction
// If all occupants are evicted the interaction is returned to available state.
[BurstCompile]
[WithDisabled(typeof(InteractionProvider))]
public partial struct InactiveBrainCleanupJob : IJobEntity
{
    [ReadOnly]
    public ComponentLookup<ActiveBrain> ActiveBrainLookup;

    // Write lookup — safe without NativeDisableParallelForRestriction because
    // the job is scheduled with .Schedule() (single-threaded).
    public ComponentLookup<SelectedAction> SelectedActionLookup;

    public void Execute(
        DynamicBuffer<InteractionOccupant> occupants,
        EnabledRefRW<InteractionProvider>  providerEnabled,
        EnabledRefRW<InteractionHandled>   handledEnabled,
        EnabledRefRW<InteractionTimer>     timerEnabled)
    {
        bool anyEvicted = false;

        // Iterate backwards so RemoveAt doesn't shift unvisited indices.
        for (int i = occupants.Length - 1; i >= 0; i--)
        {
            Entity brainEntity = occupants[i].entity;

            // Only evict if the brain exists and is explicitly inactive.
            if (!ActiveBrainLookup.HasComponent(brainEntity)) continue;
            if (ActiveBrainLookup.IsComponentEnabled(brainEntity)) continue;

            occupants.RemoveAt(i);
            anyEvicted = true;

            // Clear the brain's current assignment so it doesn't hold a stale reference.
            if (SelectedActionLookup.HasComponent(brainEntity))
            {
                SelectedActionLookup[brainEntity] = new SelectedAction
                {
                    current  = Entity.Null,
                    previous = Entity.Null,
                };
            }
        }

        // If every occupant was evicted, fully restore the interaction.
        if (anyEvicted && occupants.Length == 0)
        {
            providerEnabled.ValueRW = true;
            handledEnabled.ValueRW  = false;
            timerEnabled.ValueRW    = false;
        }
    }
}
