using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(AIScoringSystemGroup))]
public partial struct SocialScoringSystem : ISystem
{
    private const MotivationType MOTIVATION_TYPE = MotivationType.Social;

    private ComponentLookup<InteractionProvider> interactionProviderLookup;
    private ComponentLookup<LocalTransform> transformLookup;
    private ComponentLookup<SocialInteraction> socialInteractionLookup;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<SocialInteraction>();
        state.RequireForUpdate<SpatialHashRegistry>();
        state.RequireForUpdate<ScoringLibrary>();

        interactionProviderLookup = state.GetComponentLookup<InteractionProvider>(true);
        transformLookup = state.GetComponentLookup<LocalTransform>(true);
        socialInteractionLookup = state.GetComponentLookup<SocialInteraction>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        interactionProviderLookup.Update(ref state);
        transformLookup.Update(ref state);
        socialInteractionLookup.Update(ref state);

        SpatialHashRegistry spatialHash = SystemAPI.GetSingleton<SpatialHashRegistry>();
        ScoringLibrary scoringLibrary = SystemAPI.GetSingleton<ScoringLibrary>();

        state.Dependency = new SocialScoringJob
        {
            interactionProviderLookup = interactionProviderLookup,
            transformLookup = transformLookup,
            socialInteractionLookup = socialInteractionLookup,
            interactionCells = spatialHash.interactionCells,
            cellSize = SpatialHashSystem.CELL_SIZE,
            scoringLibrary = scoringLibrary.library
        }.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    [WithAll(typeof(ActiveBrain))]
    public partial struct SocialScoringJob : IJobEntity
    {
        [ReadOnly] public ComponentLookup<InteractionProvider> interactionProviderLookup;
        [ReadOnly] public ComponentLookup<LocalTransform> transformLookup;
        [ReadOnly] public ComponentLookup<SocialInteraction> socialInteractionLookup;
        [ReadOnly] public NativeParallelMultiHashMap<SpatialInteractionKey, Entity> interactionCells;
        [ReadOnly] public BlobAssetReference<AIScoringLibraryBlob> scoringLibrary;
        public float cellSize;

        public void Execute(
            ref DynamicBuffer<ActionOption> options,
            in SocialMotivation socialMotivation,
            in Awareness awareness,
            in LocalTransform transform,
            EnabledRefRO<NeedsAction> needsAction)
        {
            if (!needsAction.ValueRO)
                return;

            float3 pos = transform.Position;

            NativeList<Entity> nearby = new NativeList<Entity>(8, Allocator.Temp);

            // Only queries interactions that have SocialInteraction component
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
                SocialInteraction interaction = socialInteractionLookup[candidate];
                float multiplier = interaction.value * 0.01f + 1f;

                float baseScore = AIUtils.ScoreInteraction(
                    candidate, pos, socialMotivation.value,
                    awareness.range, MOTIVATION_TYPE,
                    ref scoringLibrary, ref transformLookup);

                float finalScore = baseScore * multiplier;

                AIUtils.AddActionOption(ref options, ref candidate, finalScore);
            }

            nearby.Dispose();
        }
    }
}