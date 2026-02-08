using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(AIAwarenessSystemGroup))]
[UpdateAfter(typeof(AwarenessClearSystem))]
public partial struct InteractableBroadcastSystem : ISystem
{
    private ComponentLookup<LocalTransform> transformLookup;
    private ComponentLookup<Needs> needsLookup;
    private ComponentLookup<CurrentInteraction> currentInteractionLookup;
    private ComponentLookup<BodyBrain> bodyBrainLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PhysicsWorldSingleton>();

        transformLookup = state.GetComponentLookup<LocalTransform>(true);
        needsLookup = state.GetComponentLookup<Needs>(true);
        currentInteractionLookup = state.GetComponentLookup<CurrentInteraction>(true);
        bodyBrainLookup = state.GetComponentLookup<BodyBrain>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        transformLookup.Update(ref state);
        needsLookup.Update(ref state);
        currentInteractionLookup.Update(ref state);
        bodyBrainLookup.Update(ref state);

        PhysicsWorldSingleton physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();

        // Job 1: Each interactable finds candidates and fills its offer buffer
        state.Dependency = new InteractableOfferJob
        {
            physicsWorld = physicsWorld,
            transformLookup = transformLookup,
            needsLookup = needsLookup,
            currentInteractionLookup = currentInteractionLookup,
            bodyBrainLookup = bodyBrainLookup
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
public partial struct InteractableOfferJob : IJobEntity
{
    [ReadOnly] public PhysicsWorldSingleton physicsWorld;
    [ReadOnly] public ComponentLookup<LocalTransform> transformLookup;
    [ReadOnly] public ComponentLookup<Needs> needsLookup;
    [ReadOnly] public ComponentLookup<CurrentInteraction> currentInteractionLookup;
    [ReadOnly] public ComponentLookup<BodyBrain> bodyBrainLookup;

    public void Execute(
        ref Interactable interactable,
        ref DynamicBuffer<OccupantEntity> occupants,
        ref DynamicBuffer<InteractableOffer> offers,
        in LocalTransform transform,
        Entity entity)
    {
        // Clear previous offers
        offers.Clear();

        // Clean up invalid occupants
        CleanupOccupants(ref occupants, entity);
        interactable.currentOccupants = occupants.Length;

        // Calculate available spots
        int availableSpots = interactable.maxOccupants - occupants.Length;
        if (availableSpots <= 0)
            return;

        // Find nearby NPCs
        float3 position = transform.Position;
        NativeList<DistanceHit> hits = new NativeList<DistanceHit>(Allocator.Temp);

        PointDistanceInput pointInput = new PointDistanceInput
        {
            Position = position,
            MaxDistance = interactable.broadcastRadius,
            Filter = new CollisionFilter
            {
                BelongsTo = ~0u,
                CollidesWith = 1u << 6,
                GroupIndex = 0
            }
        };

        physicsWorld.CalculateDistance(pointInput, ref hits);

        // Collect valid candidates
        NativeList<InteractableOffer> candidates = new NativeList<InteractableOffer>(Allocator.Temp);

        for (int i = 0; i < hits.Length; i++)
        {
            Entity hitEntity = hits[i].Entity;

            if (!bodyBrainLookup.TryGetComponent(hitEntity, out BodyBrain bodyBrain))
                continue;

            if (bodyBrain.brain == Entity.Null)
                continue;

            Entity brain = bodyBrain.brain;

            // Skip if already occupying
            bool isOccupant = false;
            for (int j = 0; j < occupants.Length; j++)
            {
                if (occupants[j].entity == brain)
                {
                    isOccupant = true;
                    break;
                }
            }
            if (isOccupant)
                continue;

            // Check needs
            if (!needsLookup.TryGetComponent(brain, out Needs needs))
                continue;

            // Skip if already interacting
            if (currentInteractionLookup.TryGetComponent(brain, out CurrentInteraction current))
            {
                if (current.target != Entity.Null)
                    continue;
            }

            // Check if NPC has matching need
            if (!HasMatchingNeed(interactable.type, needs))
                continue;

            candidates.Add(new InteractableOffer
            {
                brain = brain,
                distance = hits[i].Distance
            });
        }

        hits.Dispose();

        // Sort by distance and take top N offers
        SortByDistance(ref candidates);

        int offerCount = math.min(candidates.Length, interactable.maxOffers);
        for (int i = 0; i < offerCount; i++)
        {
            offers.Add(candidates[i]);
        }

        candidates.Dispose();
    }

    private void CleanupOccupants(ref DynamicBuffer<OccupantEntity> occupants, Entity interactableEntity)
    {
        for (int i = occupants.Length - 1; i >= 0; i--)
        {
            Entity occupant = occupants[i].entity;

            if (!currentInteractionLookup.HasComponent(occupant))
            {
                occupants.RemoveAt(i);
                continue;
            }

            CurrentInteraction interaction = currentInteractionLookup[occupant];
            if (interaction.target != interactableEntity)
            {
                occupants.RemoveAt(i);
            }
        }
    }

    private bool HasMatchingNeed(InteractableType type, Needs needs)
    {
        switch (type)
        {
            case InteractableType.Food:
                return needs.hunger > 0.3f;
            case InteractableType.Bed:
                return needs.energy < 0.4f;
            case InteractableType.Bathroom:
                return needs.bladder > 0.5f;
            case InteractableType.Seat:
                return needs.comfort < 0.5f;
            case InteractableType.Bar:
                return needs.entertainment < 0.5f || needs.social < 0.5f;
            case InteractableType.SmokingSpot:
                return needs.entertainment < 0.6f;
            case InteractableType.Entertainment:
                return needs.entertainment < 0.5f;
            case InteractableType.Workstation:
                return needs.energy > 0.3f && needs.hunger < 0.7f;
            default:
                return false;
        }
    }

    private void SortByDistance(ref NativeList<InteractableOffer> list)
    {
        for (int i = 0; i < list.Length - 1; i++)
        {
            for (int j = i + 1; j < list.Length; j++)
            {
                if (list[j].distance < list[i].distance)
                {
                    InteractableOffer temp = list[i];
                    list[i] = list[j];
                    list[j] = temp;
                }
            }
        }
    }
}