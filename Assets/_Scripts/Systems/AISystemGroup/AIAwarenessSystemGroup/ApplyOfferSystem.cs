using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

[BurstCompile]
[UpdateInGroup(typeof(AIAwarenessSystemGroup))]
[UpdateAfter(typeof(InteractableBroadcastSystem))]
public partial struct ApplyOffersSystem : ISystem
{
    private ComponentLookup<Awareness> awarenessLookup;
    private BufferLookup<PendingOffer> pendingOfferLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        awarenessLookup = state.GetComponentLookup<Awareness>(false);
        pendingOfferLookup = state.GetBufferLookup<PendingOffer>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        awarenessLookup.Update(ref state);
        pendingOfferLookup.Update(ref state);

        // First clear all pending offers
        state.Dependency = new ClearPendingOffersJob().ScheduleParallel(state.Dependency);

        // Then apply new offers from interactables
        state.Dependency = new ApplyOffersJob
        {
            awarenessLookup = awarenessLookup,
            pendingOfferLookup = pendingOfferLookup
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
public partial struct ClearPendingOffersJob : IJobEntity
{
    public void Execute(ref DynamicBuffer<PendingOffer> pendingOffers, ref Awareness awareness)
    {
        pendingOffers.Clear();
        
        // Reset awareness
        awareness.nearestFood = Entity.Null;
        awareness.nearestBed = Entity.Null;
        awareness.nearestWork = Entity.Null;
        awareness.nearestEntertainment = Entity.Null;
        awareness.nearestSmokeSpot = Entity.Null;
        awareness.nearestBar = Entity.Null;
        awareness.nearestBathroom = Entity.Null;
        awareness.nearestSeat = Entity.Null;

        awareness.nearestFoodDistance = float.MaxValue;
        awareness.nearestBedDistance = float.MaxValue;
        awareness.nearestWorkDistance = float.MaxValue;
        awareness.nearestEntertainmentDistance = float.MaxValue;
        awareness.nearestSmokeSpotDistance = float.MaxValue;
        awareness.nearestBarDistance = float.MaxValue;
        awareness.nearestBathroomDistance = float.MaxValue;
        awareness.nearestSeatDistance = float.MaxValue;

        awareness.hasFood = false;
        awareness.hasBed = false;
        awareness.hasWork = false;
        awareness.hasEntertainment = false;
        awareness.hasSmokeSpot = false;
        awareness.hasBar = false;
        awareness.hasBathroom = false;
        awareness.hasSeat = false;
    }
}

[BurstCompile]
public partial struct ApplyOffersJob : IJobEntity
{
    [NativeDisableParallelForRestriction] public ComponentLookup<Awareness> awarenessLookup;
    [NativeDisableParallelForRestriction] public BufferLookup<PendingOffer> pendingOfferLookup;

    public void Execute(
        in Interactable interactable,
        in DynamicBuffer<InteractableOffer> offers,
        Entity entity)
    {
        for (int i = 0; i < offers.Length; i++)
        {
            Entity brain = offers[i].brain;
            float distance = offers[i].distance;

            // Add to pending offers
            if (pendingOfferLookup.TryGetBuffer(brain, out DynamicBuffer<PendingOffer> pendingOffers))
            {
                pendingOffers.Add(new PendingOffer
                {
                    interactable = entity,
                    type = interactable.type,
                    distance = distance
                });
            }

            // Update awareness with closest per type
            if (awarenessLookup.TryGetComponent(brain, out Awareness awareness))
            {
                UpdateAwareness(ref awareness, interactable.type, entity, distance);
                awarenessLookup[brain] = awareness;
            }
        }
    }

    private void UpdateAwareness(ref Awareness awareness, InteractableType type, Entity entity, float distance)
    {
        switch (type)
        {
            case InteractableType.Food:
                if (!awareness.hasFood || distance < awareness.nearestFoodDistance)
                {
                    awareness.nearestFood = entity;
                    awareness.nearestFoodDistance = distance;
                    awareness.hasFood = true;
                }
                break;
            case InteractableType.Bed:
                if (!awareness.hasBed || distance < awareness.nearestBedDistance)
                {
                    awareness.nearestBed = entity;
                    awareness.nearestBedDistance = distance;
                    awareness.hasBed = true;
                }
                break;
            case InteractableType.Bathroom:
                if (!awareness.hasBathroom || distance < awareness.nearestBathroomDistance)
                {
                    awareness.nearestBathroom = entity;
                    awareness.nearestBathroomDistance = distance;
                    awareness.hasBathroom = true;
                }
                break;
            case InteractableType.Seat:
                if (!awareness.hasSeat || distance < awareness.nearestSeatDistance)
                {
                    awareness.nearestSeat = entity;
                    awareness.nearestSeatDistance = distance;
                    awareness.hasSeat = true;
                }
                break;
            case InteractableType.Bar:
                if (!awareness.hasBar || distance < awareness.nearestBarDistance)
                {
                    awareness.nearestBar = entity;
                    awareness.nearestBarDistance = distance;
                    awareness.hasBar = true;
                }
                break;
            case InteractableType.SmokingSpot:
                if (!awareness.hasSmokeSpot || distance < awareness.nearestSmokeSpotDistance)
                {
                    awareness.nearestSmokeSpot = entity;
                    awareness.nearestSmokeSpotDistance = distance;
                    awareness.hasSmokeSpot = true;
                }
                break;
            case InteractableType.Entertainment:
                if (!awareness.hasEntertainment || distance < awareness.nearestEntertainmentDistance)
                {
                    awareness.nearestEntertainment = entity;
                    awareness.nearestEntertainmentDistance = distance;
                    awareness.hasEntertainment = true;
                }
                break;
            case InteractableType.Workstation:
                if (!awareness.hasWork || distance < awareness.nearestWorkDistance)
                {
                    awareness.nearestWork = entity;
                    awareness.nearestWorkDistance = distance;
                    awareness.hasWork = true;
                }
                break;
        }
    }
}