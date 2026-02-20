using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(AIScoringSystemGroup))]
public partial struct HungerScoringSystem : ISystem
{
    private const MotivationType MOTIVATION_TYPE = MotivationType.Hunger;

    private ComponentLookup<InteractionProvider> interactionProviderLookup;
    private ComponentLookup<LocalTransform> transformLookup;
    private ComponentLookup<HungerInteraction> hungerInteractionLookup;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<HungerInteraction>();
        state.RequireForUpdate<SpatialHashSingleton>();
        state.RequireForUpdate<ScoringLibrary>();

        interactionProviderLookup = state.GetComponentLookup<InteractionProvider>(true);
        transformLookup = state.GetComponentLookup<LocalTransform>(true);
        hungerInteractionLookup = state.GetComponentLookup<HungerInteraction>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        interactionProviderLookup.Update(ref state);
        transformLookup.Update(ref state);
        hungerInteractionLookup.Update(ref state);

        SpatialHashSingleton spatialHash = SystemAPI.GetSingleton<SpatialHashSingleton>();
        ScoringLibrary scoringLibrary = SystemAPI.GetSingleton<ScoringLibrary>();

        state.Dependency = new HungerScoringJob
        {
            interactionProviderLookup = interactionProviderLookup,
            transformLookup = transformLookup,
            hungerInteractionLookup = hungerInteractionLookup,
            interactionCells = spatialHash.interactionCells,
            cellSize = SpatialHashSystem.CELL_SIZE,
            scoringLibrary = scoringLibrary.library
        }.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    public partial struct HungerScoringJob : IJobEntity
    {
        [ReadOnly] public ComponentLookup<InteractionProvider> interactionProviderLookup;
        [ReadOnly] public ComponentLookup<LocalTransform> transformLookup;
        [ReadOnly] public ComponentLookup<HungerInteraction> hungerInteractionLookup;
        [ReadOnly] public NativeParallelMultiHashMap<SpatialInteractionKey, Entity> interactionCells;
        [ReadOnly] public BlobAssetReference<AIScoringLibraryBlob> scoringLibrary;
        public float cellSize;

        public void Execute(
            ref DynamicBuffer<ActionOption> options,
            in HungerMotivation hungerMotivation,
            in Awareness awareness,
            in LocalTransform transform,
            EnabledRefRO<NeedsAction> needsAction)
        {
            if (!needsAction.ValueRO)
                return;

            float3 pos = transform.Position;

            NativeList<Entity> nearby = new NativeList<Entity>(8, Allocator.Temp);

            // Only queries interactions that have HungerInteraction component
            AIUtils.QueryNearbyInteractionsByType(
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
                HungerInteraction interaction = hungerInteractionLookup[candidate];
                float multiplier = interaction.value * 0.01f + 1f;

                float baseScore = AIUtils.ScoreInteraction(
                    candidate, pos, hungerMotivation.value,
                    awareness.range, MOTIVATION_TYPE,
                    ref scoringLibrary, ref transformLookup);

                float finalScore = baseScore * multiplier;

                AIUtils.AddActionOption(ref options, ref candidate, finalScore);
            }

            nearby.Dispose();
        }
    }
}