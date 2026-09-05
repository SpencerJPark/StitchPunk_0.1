using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(UtilityAwarenessSystemGroup))]
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
        state.RequireForUpdate<BrainLibrary>();

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
        BrainLibrary        brainLibrary    = SystemAPI.GetSingleton<BrainLibrary>();

        state.Dependency = new InteractionAwarenessJob
        {
            registry           = registry,
            transformLookup    = transformLookup,
            interactionLookup  = interactionLookup,
            interactionLibrary = interactionLib.library,
            aiConfig           = brainLibrary.blob,
            timestamp          = SystemAPI.Time.ElapsedTime,
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(UtilityBrain), typeof(ActionRequest))]
[WithDisabled(typeof(CutsceneActor))]
public partial struct InteractionAwarenessJob : IJobEntity
{
    [ReadOnly] public SpatialHashRegistry                         registry;
    [ReadOnly] public ComponentLookup<LocalTransform>             transformLookup;
    [ReadOnly] public ComponentLookup<Interaction>                interactionLookup;
    [ReadOnly] public BlobAssetReference<InteractionLibraryBlob>  interactionLibrary;
    [ReadOnly] public BlobAssetReference<BrainLibraryBlob>        aiConfig;
    public double timestamp;

    public void Execute(
        Entity entity,
        in UtilityBrain                          brain,
        ref DynamicBuffer<UtilityActions>        options,
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
            NeedType currentNeed = motivations[m].needType;
            if (currentNeed == NeedType.None) continue;

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
                                faction.factionType, brain.unitType, recentInteractions, ref options);
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
        NeedType                                 needType,
        FactionType                              npcFaction,
        UnitType                                 unitType,
        in DynamicBuffer<RecentInteraction>      recentInteractions,
        ref DynamicBuffer<UtilityActions>        options)
    {
        // Expiry-aware: an entry only blocks while its cooldown is still running.
        for (int i = 0; i < recentInteractions.Length; i++)
            if (recentInteractions[i].entity == target
                && recentInteractions[i].cooldownEndTime > (float)timestamp) return;

        if (!transformLookup.TryGetComponent(target, out LocalTransform targetTransform))
            return;

        float dist = math.distance(npcPos, targetTransform.Position);
        if (dist > maxRange)
            return;

        if (!interactionLookup.TryGetComponent(target, out Interaction interactData))
            return;

        ref InteractionBlob blob = ref interactionLibrary.Value.interactions[(int)interactData.action];

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

        int defIndex = BrainBlobUtils.GetActionDefIndex(ref aiConfig.Value, unitType, interactData.action);
        if (defIndex < 0)
            return;

        options.Add(new UtilityActions
        {
            actionType      = interactData.action,
            actionDefIndex  = defIndex,
            targetEntity    = target,
            needsValidation = true,
        });
    }
}
