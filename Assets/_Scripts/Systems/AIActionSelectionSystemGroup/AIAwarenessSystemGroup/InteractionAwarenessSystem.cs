using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(AIAwarenessSystemGroup))]
public partial struct InteractionAwarenessSystem : ISystem
{
    private ComponentLookup<LocalTransform> transformLookup;
    private ComponentLookup<Interaction>    interactionLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<SpatialHashRegistry>();
        state.RequireForUpdate<InteractionLibrary>();

        transformLookup   = state.GetComponentLookup<LocalTransform>(true);
        interactionLookup = state.GetComponentLookup<Interaction>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        transformLookup.Update(ref state);
        interactionLookup.Update(ref state);

        SpatialHashRegistry registry        = SystemAPI.GetSingleton<SpatialHashRegistry>();
        InteractionLibrary  interactionLib  = SystemAPI.GetSingleton<InteractionLibrary>();

        state.Dependency = new InteractionAwarenessJob
        {
            registry           = registry,
            transformLookup    = transformLookup,
            interactionLookup  = interactionLookup,
            interactionLibrary = interactionLib.library,
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(AIBrain), typeof(ActionRequest))]
public partial struct InteractionAwarenessJob : IJobEntity
{
    [ReadOnly] public SpatialHashRegistry                         registry;
    [ReadOnly] public ComponentLookup<LocalTransform>             transformLookup;
    [ReadOnly] public ComponentLookup<Interaction>                interactionLookup;
    [ReadOnly] public BlobAssetReference<InteractionLibraryBlob>  interactionLibrary;

    public void Execute(
        Entity entity,
        ref DynamicBuffer<ActionOption>          options,
        in DynamicBuffer<Motivation>             motivations,
        in DynamicBuffer<RecentInteraction>      recentInteractions,
        in LocalTransform                        transform,
        in Awareness                             awareness,
        in Faction                               faction)
    {
        float3 npcPos     = transform.Position;
        int2   centerCell = InteractionSpatialHashSystem.GetCell(npcPos);
        int    cellRange  = (int)math.ceil(awareness.range / InteractionSpatialHashSystem.CELL_SIZE);

        for (int m = 0; m < motivations.Length; m++)
        {
            MotivationType currentNeed = motivations[m].motivationType;
            if (currentNeed == MotivationType.None) continue;

            for (int x = -cellRange; x <= cellRange; x++)
            {
                for (int z = -cellRange; z <= cellRange; z++)
                {
                    int2 targetCell = centerCell + new int2(x, z);
                    SpatialInteractionKey searchKey = new SpatialInteractionKey(targetCell, currentNeed);
                    bool hit = registry.interactionCells.TryGetFirstValue(searchKey, out Entity target, out NativeParallelMultiHashMapIterator<SpatialInteractionKey> it);

                    if (hit)
                    {
                        do
                        {
                            AddActionIfValid(target, npcPos, awareness.range, currentNeed,
                                faction.factionType, recentInteractions, ref options);
                        } while (registry.interactionCells.TryGetNextValue(out target, ref it));
                    }
                }
            }
        }
    }

    private void AddActionIfValid(
        Entity                                   target,
        float3                                   npcPos,
        float                                    maxRange,
        MotivationType                           motivationType,
        FactionType                              npcFaction,
        in DynamicBuffer<RecentInteraction>      recentInteractions,
        ref DynamicBuffer<ActionOption>          options)
    {
        for (int i = 0; i < recentInteractions.Length; i++)
            if (recentInteractions[i].entity == target) return;

        if (!transformLookup.TryGetComponent(target, out LocalTransform targetTransform))
            return;

        float dist = math.distance(npcPos, targetTransform.Position);
        if (dist > maxRange)
            return;

        if (!interactionLookup.TryGetComponent(target, out Interaction interactData))
            return;

        ref InteractionBlob blob = ref interactionLibrary.Value.interactions[(int)interactData.actionType];

        if (blob.allowedFactions.Length > 0)
        {
            bool permitted = false;
            for (int i = 0; i < blob.allowedFactions.Length; i++)
            {
                if (blob.allowedFactions[i] == npcFaction) { permitted = true; break; }
            }
            if (!permitted)
                return;
        }

        if (interactData.currentOccupants >= blob.maxOccupants)
            return;

        float distScore = 1.0f - math.saturate(dist / maxRange);

        options.Add(new ActionOption
        {
            actionType     = interactData.actionType,
            motivationType = motivationType,
            priority = blob.priority,
            targetEntity   = target,
            interaction    = true,
            utilityScore   = distScore
        });
    }
}
