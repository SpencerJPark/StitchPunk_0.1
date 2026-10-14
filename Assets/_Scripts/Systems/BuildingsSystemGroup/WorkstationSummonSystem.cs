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

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<UnitDataLibrary>();
        resourceStackLookup = state.GetBufferLookup<ResourceStack>(true);
        resourceChangeRequestLookup = state.GetBufferLookup<ResourceChangeRequest>();
        resourceShortfallLookup = state.GetComponentLookup<ResourceShortfall>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        resourceStackLookup.Update(ref state);
        resourceChangeRequestLookup.Update(ref state);
        resourceShortfallLookup.Update(ref state);

        BlobAssetReference<UnitLibraryBlob> unitLibrary = SystemAPI.GetSingleton<UnitDataLibrary>().library;
        double elapsedTime = SystemAPI.Time.ElapsedTime;
        float deltaTime = SystemAPI.Time.DeltaTime;

        foreach ((RefRO<Workstation> workstation,
                  RefRO<LocalTransform> localTransform,
                  DynamicBuffer<WorkstationSummonOption> options,
                  DynamicBuffer<WorkstationSummonRequest> requests,
                  DynamicBuffer<WorkstationSummonQueueEntry> queue,
                  DynamicBuffer<PendingMinionSpawn> pendingSpawns) in
            SystemAPI.Query<
                RefRO<Workstation>,
                RefRO<LocalTransform>,
                DynamicBuffer<WorkstationSummonOption>,
                DynamicBuffer<WorkstationSummonRequest>,
                DynamicBuffer<WorkstationSummonQueueEntry>,
                DynamicBuffer<PendingMinionSpawn>>())
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

            WorkstationSummonQueueEntry head = queue[0];
            head.remainingSeconds -= deltaTime;
            if (head.remainingSeconds > 0f)
            {
                DynamicBuffer<WorkstationSummonQueueEntry> writableQueue = queue;
                writableQueue[0] = head;
                continue;
            }

            float3 spawnPosition = localTransform.ValueRO.Position
                + math.rotate(localTransform.ValueRO.Rotation, workstation.ValueRO.summonSpawnLocalOffset);
            pendingSpawns.Add(new PendingMinionSpawn { unitType = head.unitType, position = spawnPosition });
            queue.RemoveAt(0);
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

        if (ownerPlayer == Entity.Null
            || !resourceStackLookup.HasBuffer(ownerPlayer)
            || !resourceChangeRequestLookup.HasBuffer(ownerPlayer)
            || !resourceShortfallLookup.HasComponent(ownerPlayer)) return;

        SummonCostBlob cost = unitLibrary.Value.units[unitIndex].summonCost;
        DynamicBuffer<ResourceStack> stack = resourceStackLookup[ownerPlayer];
        DynamicBuffer<ResourceChangeRequest> changeRequests = resourceChangeRequestLookup[ownerPlayer];
        bool paid = ResourceLedger.TrySpend(
            in stack, ref changeRequests, in cost, true, elapsedTime,
            ref resourceShortfallLookup.GetRefRW(ownerPlayer).ValueRW,
            resourceShortfallLookup.GetEnabledRefRW<ResourceShortfall>(ownerPlayer));
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
