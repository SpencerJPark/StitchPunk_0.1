using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Detects when the player enters the range of an enabled NarrativeTrigger and fires
/// the corresponding narrative event by enabling OnNarrativeEvent on the singleton entity.
///
/// After firing, the trigger is disabled so it does not re-trigger while the event runs.
/// If the trigger has repeatable = true, NarrativeEventManager re-enables it when done.
///
/// Does nothing when an ActiveNarrativeEvent is already running (no event stacking).
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(NarrativeSystemGroup))]
public partial struct NarrativeProximitySystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Player>();
        state.RequireForUpdate<NarrativeEventTag>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        Entity narrativeEntity = SystemAPI.GetSingletonEntity<NarrativeEventTag>();

        // Block new triggers while an event is already running.
        if (SystemAPI.IsComponentEnabled<ActiveNarrativeEvent>(narrativeEntity)) return;

        // Block new triggers if one was already queued this frame.
        if (SystemAPI.IsComponentEnabled<OnNarrativeEvent>(narrativeEntity)) return;

        float3 playerPosition = float3.zero;
        bool playerFound = false;

        foreach (RefRO<LocalTransform> playerTransform in
            SystemAPI.Query<RefRO<LocalTransform>>().WithAll<Player>())
        {
            playerPosition = playerTransform.ValueRO.Position;
            playerFound = true;
            break;
        }

        if (!playerFound) return;

        foreach (var (trigger, triggerTransform, triggerEnabled) in
            SystemAPI.Query<
                RefRO<NarrativeTrigger>,
                RefRO<LocalTransform>,
                EnabledRefRW<NarrativeTrigger>>()
                    .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            if (!triggerEnabled.ValueRO) continue;

            float distance = math.distance(playerPosition, triggerTransform.ValueRO.Position);
            if (distance > trigger.ValueRO.range) continue;

            // Fire the event.
            SystemAPI.SetComponent(narrativeEntity, new OnNarrativeEvent
            {
                eventId = trigger.ValueRO.eventId,
            });
            SystemAPI.SetComponentEnabled<OnNarrativeEvent>(narrativeEntity, true);

            // Disable trigger to prevent re-firing until the event completes.
            triggerEnabled.ValueRW = false;

            // Only fire one trigger per frame.
            break;
        }
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state) { }
}
