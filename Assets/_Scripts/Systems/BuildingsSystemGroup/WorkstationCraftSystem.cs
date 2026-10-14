using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[UpdateInGroup(typeof(BuildingsSystemGroup))]
public partial struct WorkstationCraftSystem : ISystem
{
    private BufferLookup<ResourceStack> resourceStackLookup;
    private BufferLookup<ResourceChangeRequest> resourceChangeRequestLookup;
    private ComponentLookup<ResourceShortfall> resourceShortfallLookup;

    public void OnCreate(ref SystemState state)
    {
        resourceStackLookup = state.GetBufferLookup<ResourceStack>(true);
        resourceChangeRequestLookup = state.GetBufferLookup<ResourceChangeRequest>();
        resourceShortfallLookup = state.GetComponentLookup<ResourceShortfall>();
    }

    public void OnUpdate(ref SystemState state)
    {
        NativeList<Entity> requestingWorkstations = new NativeList<Entity>(Allocator.Temp);
        foreach ((RefRO<CraftRevivalRoundRequest> _, Entity workstationEntity)
                 in SystemAPI.Query<RefRO<CraftRevivalRoundRequest>>().WithEntityAccess())
        {
            requestingWorkstations.Add(workstationEntity);
        }
        if (requestingWorkstations.Length == 0) return;

        resourceStackLookup.Update(ref state);
        resourceChangeRequestLookup.Update(ref state);
        resourceShortfallLookup.Update(ref state);
        double elapsedTime = SystemAPI.Time.ElapsedTime;

        for (int workstationIndex = 0; workstationIndex < requestingWorkstations.Length; workstationIndex++)
        {
            Entity workstationEntity = requestingWorkstations[workstationIndex];
            // Disabled first so an early skip below can never leave the request re-firing every frame.
            int requestedCount = math.max(1, SystemAPI.GetComponent<CraftRevivalRoundRequest>(workstationEntity).count);
            SystemAPI.SetComponentEnabled<CraftRevivalRoundRequest>(workstationEntity, false);

            Entity owner = SystemAPI.GetComponent<Workstation>(workstationEntity).ownerPlayer;
            if (owner == Entity.Null) continue;
            if (!resourceStackLookup.TryGetBuffer(owner, out DynamicBuffer<ResourceStack> resourceStack)) continue;
            if (!resourceChangeRequestLookup.TryGetBuffer(owner, out DynamicBuffer<ResourceChangeRequest> resourceChangeRequests)) continue;

            WorkstationRecipes recipes = SystemAPI.GetComponent<WorkstationRecipes>(workstationEntity);
            int scrapCost = recipes.revivalRoundScrapCost;
            int electricityCost = recipes.revivalRoundElectricityCost;
            int scrapStock = ResourceLedger.Get(in resourceStack, ResourceType.ScrapMetal);
            int electricityStock = ResourceLedger.Get(in resourceStack, ResourceType.Electricity);

            // TrySpend reads the live stack, which is unchanged until ResourceChangeSystem runs,
            // so the affordable count is worked out up front instead of spending one round at a time.
            int affordableCount = requestedCount;
            if (scrapCost > 0) affordableCount = math.min(affordableCount, scrapStock / scrapCost);
            if (electricityCost > 0) affordableCount = math.min(affordableCount, electricityStock / electricityCost);

            if (affordableCount > 0)
            {
                ResourceLedger.QueueAdd(ref resourceChangeRequests, ResourceType.ScrapMetal, -scrapCost * affordableCount);
                ResourceLedger.QueueAdd(ref resourceChangeRequests, ResourceType.Electricity, -electricityCost * affordableCount);
                ResourceLedger.QueueAdd(ref resourceChangeRequests, ResourceType.RevivalRound, affordableCount);
            }

            if (affordableCount >= requestedCount || !resourceShortfallLookup.HasComponent(owner)) continue;

            // Stamp what the next round would still be missing, measured against the stock left after the spends above.
            int scrapLeft = scrapStock - scrapCost * affordableCount;
            int electricityLeft = electricityStock - electricityCost * affordableCount;
            ResourceShortfall shortfall = default;
            shortfall.stampedTime = elapsedTime;
            if (scrapLeft < scrapCost)
            {
                shortfall.resourceType = ResourceType.ScrapMetal;
                shortfall.missing = scrapCost - scrapLeft;
            }
            else
            {
                shortfall.resourceType = ResourceType.Electricity;
                shortfall.missing = electricityCost - electricityLeft;
            }
            resourceShortfallLookup[owner] = shortfall;
            resourceShortfallLookup.SetComponentEnabled(owner, true);
        }
    }
}
