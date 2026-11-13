using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

// Any caller can enable a SwapBrainRequest to convert a unit's brain in place (unitType, Faction, attack and motivation buffers).
// Rebuilt motivations get decayRate = 0 (the blob has no decay data), so a converted unit's needs stay satisfied.
[BurstCompile]
[UpdateInGroup(typeof(HealthSystemGroup))]
[UpdateAfter(typeof(ReviveRequestSystem))]
public partial struct SwapBrainSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<UnitDataLibrary>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        BlobAssetReference<UnitLibraryBlob> unitLibrary =
            SystemAPI.GetSingleton<UnitDataLibrary>().library;

        EntityCommandBuffer.ParallelWriter ecb =
            SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged)
                .AsParallelWriter();

        state.Dependency = new SwapBrainJob
        {
            unitLibrary = unitLibrary,
            ecb         = ecb,
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
// WithPresent(UtilityBrain): a revived minion keeps UtilityBrain disabled, but the swap must still run, so match it either way.
[WithPresent(typeof(UtilityBrain))]
public partial struct SwapBrainJob : IJobEntity
{
    [ReadOnly] public BlobAssetReference<UnitLibraryBlob> unitLibrary;
    public EntityCommandBuffer.ParallelWriter ecb;

    public void Execute(
        [ChunkIndexInQuery] int sortKey,
        Entity entity,
        in SwapBrainRequest swapBrainRequest,
        ref UtilityBrain utilityBrain,
        ref UnitData unitData,
        ref Faction faction,
        EnabledRefRW<ActionInterruptRequest> actionInterruptRequestEnabled,
        DynamicBuffer<AttackFaction> attackFactions,
        DynamicBuffer<AvailableAttack> availableAttacks,
        DynamicBuffer<Motivation> motivations)
    {
        UnitType newUnit = swapBrainRequest.newUnit;

        // One-shot consume regardless of outcome.
        ecb.SetComponentEnabled<SwapBrainRequest>(sortKey, entity, false);

        int idx = unitLibrary.Value.FindByUnitType(newUnit);
        if (idx < 0)
            return;

        ref UnitDataBlob entry = ref unitLibrary.Value.units[idx];

        utilityBrain.unitType = newUnit;
        unitData.unitType     = newUnit;
        faction.factionType   = entry.factionType;

        attackFactions.Clear();
        for (int i = 0; i < entry.attackFactions.Length; i++)
            attackFactions.Add(new AttackFaction { faction = entry.attackFactions[i] });

        availableAttacks.Clear();
        for (int i = 0; i < entry.attacks.Length; i++)
            availableAttacks.Add(new AvailableAttack
            {
                actionType = entry.attacks[i].action,
                damageSource = entry.attacks[i].attack,
            });

        motivations.Clear();
        for (int i = 0; i < entry.motivation.Length; i++)
            motivations.Add(UnitBakingUtil.DefaultBehaviour(entry.motivation[i]));

        // Per-entity seed; +1u keeps index 0 off the zero seed.
        Random random = Random.CreateFromIndex((uint)entity.Index + 1u);
        UnitBakingUtil.PopulateRandomBehaviours(
            motivations, ref entry.randomMotivations, entry.randomMotivationAmount, ref random);

        // Re-enter action selection with the new brain (idempotent with revive's own fire).
        actionInterruptRequestEnabled.ValueRW = true;
    }
}
