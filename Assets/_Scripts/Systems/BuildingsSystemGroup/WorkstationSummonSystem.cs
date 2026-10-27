using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(BuildingsSystemGroup))]
public partial struct WorkstationSummonSystem : ISystem
{
    private BufferLookup<ResourceStack> resourceStackLookup;
    private BufferLookup<ResourceChangeRequest> resourceChangeRequestLookup;
    private ComponentLookup<ResourceShortfall> resourceShortfallLookup;
    private ComponentLookup<ResourceLedgerLink> resourceLedgerLinkLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<UnitDataLibrary>();
        resourceStackLookup = state.GetBufferLookup<ResourceStack>(true);
        resourceChangeRequestLookup = state.GetBufferLookup<ResourceChangeRequest>();
        resourceShortfallLookup = state.GetComponentLookup<ResourceShortfall>();
        resourceLedgerLinkLookup = state.GetComponentLookup<ResourceLedgerLink>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        resourceStackLookup.Update(ref state);
        resourceChangeRequestLookup.Update(ref state);
        resourceShortfallLookup.Update(ref state);
        resourceLedgerLinkLookup.Update(ref state);

        BlobAssetReference<UnitLibraryBlob> unitLibrary = SystemAPI.GetSingleton<UnitDataLibrary>().library;
        double elapsedTime = SystemAPI.Time.ElapsedTime;
        float deltaTime = SystemAPI.Time.DeltaTime;

        foreach ((RefRO<Workstation> workstation,
                  RefRO<LocalTransform> localTransform,
                  DynamicBuffer<WorkstationSummonOption> options,
                  DynamicBuffer<WorkstationSummonRequest> requests,
                  DynamicBuffer<WorkstationSummonQueueEntry> queue,
                  DynamicBuffer<PendingMinionSpawn> pendingSpawns,
                  Entity workstationEntity) in
            SystemAPI.Query<
                RefRO<Workstation>,
                RefRO<LocalTransform>,
                DynamicBuffer<WorkstationSummonOption>,
                DynamicBuffer<WorkstationSummonRequest>,
                DynamicBuffer<WorkstationSummonQueueEntry>,
                DynamicBuffer<PendingMinionSpawn>>()
                .WithEntityAccess())
        {
            // One request per frame: TrySpend reads the live stack, so two in one frame could both pass.
            if (requests.Length > 0)
            {
                UnitType requestedType = requests[0].unitType;
                requests.RemoveAt(0);
                TryQueueSummon(
                    workstation.ValueRO.ownerPlayer, requestedType, options, queue, unitLibrary, elapsedTime);
            }

            if (queue.Length == 0) continue;

            // A caravan builds only while parked, and one summon at a time leaves it (Caravan_System.md CV-Q3).
            bool isCaravan = SystemAPI.HasComponent<CaravanExitSequence>(workstationEntity);
            if (isCaravan
                && (!SystemAPI.IsComponentEnabled<CaravanParked>(workstationEntity)
                    || SystemAPI.IsComponentEnabled<CaravanExitSequence>(workstationEntity))) continue;

            WorkstationSummonQueueEntry head = queue[0];
            head.remainingSeconds -= deltaTime;
            if (head.remainingSeconds > 0f)
            {
                DynamicBuffer<WorkstationSummonQueueEntry> writableQueue = queue;
                writableQueue[0] = head;
                continue;
            }

            queue.RemoveAt(0);

            if (isCaravan)
            {
                // CaravanExitSequenceSystem releases the unit once the doors and smoke are ready for it.
                int finishedUnitIndex = unitLibrary.Value.FindByUnitType(head.unitType);
                CaravanExitStyle exitStyle = finishedUnitIndex >= 0
                    ? unitLibrary.Value.units[finishedUnitIndex].caravanExitStyle
                    : CaravanExitStyle.WalkOutAndHop;
                SystemAPI.SetComponent(workstationEntity, new CaravanExitSequence { style = exitStyle, unitType = head.unitType });
                SystemAPI.SetComponentEnabled<CaravanExitSequence>(workstationEntity, true);
                continue;
            }

            float3 spawnPosition = localTransform.ValueRO.Position
                + math.rotate(localTransform.ValueRO.Rotation, workstation.ValueRO.summonSpawnLocalOffset);
            pendingSpawns.Add(new PendingMinionSpawn { unitType = head.unitType, position = spawnPosition });
        }
    }

    private void TryQueueSummon(
        Entity ownerPlayer,
        UnitType requestedType,
        in DynamicBuffer<WorkstationSummonOption> options,
        DynamicBuffer<WorkstationSummonQueueEntry> queue,
        BlobAssetReference<UnitLibraryBlob> unitLibrary,
        double elapsedTime)
    {
        float buildSeconds = -1f;
        for (int optionIndex = 0; optionIndex < options.Length; optionIndex++)
        {
            if (options[optionIndex].unitType != requestedType) continue;
            buildSeconds = options[optionIndex].buildSeconds;
            break;
        }
        if (buildSeconds < 0f) return;

        int unitIndex = unitLibrary.Value.FindByUnitType(requestedType);
        if (unitIndex < 0) return;

        if (ownerPlayer == Entity.Null) return;
        Entity ledgerHolder = ResourceLedger.HolderOf(ownerPlayer, in resourceLedgerLinkLookup);
        if (!resourceStackLookup.HasBuffer(ledgerHolder)
            || !resourceChangeRequestLookup.HasBuffer(ledgerHolder)
            || !resourceShortfallLookup.HasComponent(ledgerHolder)) return;

        SummonCostBlob cost = unitLibrary.Value.units[unitIndex].summonCost;
        DynamicBuffer<ResourceStack> stack = resourceStackLookup[ledgerHolder];
        DynamicBuffer<ResourceChangeRequest> changeRequests = resourceChangeRequestLookup[ledgerHolder];
        bool paid = ResourceLedger.TrySpend(
            in stack, ref changeRequests, in cost, true, elapsedTime,
            ref resourceShortfallLookup.GetRefRW(ledgerHolder).ValueRW,
            resourceShortfallLookup.GetEnabledRefRW<ResourceShortfall>(ledgerHolder));
        if (!paid) return;

        queue.Add(new WorkstationSummonQueueEntry
        {
            unitType = requestedType,
            remainingSeconds = buildSeconds,
            totalSeconds = buildSeconds
        });
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state) { }
}
