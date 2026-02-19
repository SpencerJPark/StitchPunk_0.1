using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(AIScoringSystemGroup))]
public partial struct EnergyScoringSystem : ISystem
{
    private const MotivationType MOTIVATION_TYPE = MotivationType.Energy;

    private ComponentLookup<InteractionProvider> interactionProviderLookup;
    private ComponentLookup<LocalTransform> transformLookup;
    private ComponentLookup<EnergyInteraction> energyInteractionLookup;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EnergyInteraction>();
        state.RequireForUpdate<SpatialHashSingleton>();
        state.RequireForUpdate<ScoringLibrary>();

        interactionProviderLookup = state.GetComponentLookup<InteractionProvider>(true);
        transformLookup = state.GetComponentLookup<LocalTransform>(true);
        energyInteractionLookup = state.GetComponentLookup<EnergyInteraction>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        interactionProviderLookup.Update(ref state);
        transformLookup.Update(ref state);
        energyInteractionLookup.Update(ref state);

        SpatialHashSingleton spatialHash = SystemAPI.GetSingleton<SpatialHashSingleton>();
        ScoringLibrary scoringLibrary = SystemAPI.GetSingleton<ScoringLibrary>();

        state.Dependency = new EnergyScoringJob
        {
            interactionProviderLookup = interactionProviderLookup,
            transformLookup = transformLookup,
            energyInteractionLookup = energyInteractionLookup,
            interactionCells = spatialHash.interactionCells,
            cellSize = SpatialHashSystem.CELL_SIZE,
            scoringLibrary = scoringLibrary.library
        }.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    public partial struct EnergyScoringJob : IJobEntity
    {
        [ReadOnly] public ComponentLookup<InteractionProvider> interactionProviderLookup;
        [ReadOnly] public ComponentLookup<LocalTransform> transformLookup;
        [ReadOnly] public ComponentLookup<EnergyInteraction> energyInteractionLookup;
        [ReadOnly] public NativeParallelMultiHashMap<SpatialInteractionKey, Entity> interactionCells;
        [ReadOnly] public BlobAssetReference<AIScoringLibraryBlob> scoringLibrary;
        public float cellSize;

        public void Execute(
            ref DynamicBuffer<ActionOption> options,
            in EnergyMotivation energyMotivation,
            in Awareness awareness,
            in LocalTransform transform,
            EnabledRefRO<NeedsAction> needsAction)
        {
            if (!needsAction.ValueRO)
                return;

            float3 pos = transform.Position;

            NativeList<Entity> nearby = new NativeList<Entity>(8, Allocator.Temp);

            // Only queries interactions that have EnergyInteraction component
            AIUtil.QueryNearbyInteractionsByType(
                in interactionCells,
                in interactionProviderLookup,
                in transformLookup,
                pos,
                awareness.range,
                cellSize,
                MOTIVATION_TYPE,
                ref nearby);

            for (int i = 0; i < nearby.Length; i++)
            {
                Entity candidate = nearby[i];

                // Get the interaction multiplier value
                EnergyInteraction interaction = energyInteractionLookup[candidate];
                float multiplier = interaction.value * 0.01f + 1f;

                float baseScore = AIUtil.ScoreInteraction(
                    candidate, pos, energyMotivation.value,
                    awareness.range, MOTIVATION_TYPE,
                    ref scoringLibrary, ref transformLookup);

                float finalScore = baseScore * multiplier;

                AIUtil.AddActionOption(ref options, ref candidate, finalScore);
            }

            nearby.Dispose();
        }
    }
}