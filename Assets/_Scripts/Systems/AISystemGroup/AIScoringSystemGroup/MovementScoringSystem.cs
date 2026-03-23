using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(AIScoringSystemGroup))]
public partial struct MovementScoringSystem : ISystem
{
    private const MotivationType MOTIVATION_TYPE = MotivationType.Movement;

    private ComponentLookup<InteractionProvider> interactionProviderLookup;
    private ComponentLookup<LocalTransform> transformLookup;
    private ComponentLookup<MovementInteraction> movementInteractionLookup;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<MovementInteraction>();
        state.RequireForUpdate<SpatialHashRegistry>();
        state.RequireForUpdate<ScoringLibrary>();

        interactionProviderLookup = state.GetComponentLookup<InteractionProvider>(true);
        transformLookup = state.GetComponentLookup<LocalTransform>(true);
        movementInteractionLookup = state.GetComponentLookup<MovementInteraction>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        interactionProviderLookup.Update(ref state);
        transformLookup.Update(ref state);
        movementInteractionLookup.Update(ref state);

        SpatialHashRegistry spatialHash = SystemAPI.GetSingleton<SpatialHashRegistry>();
        ScoringLibrary scoringLibrary = SystemAPI.GetSingleton<ScoringLibrary>();

        state.Dependency = new MovementScoringJob
        {
            interactionProviderLookup = interactionProviderLookup,
            transformLookup = transformLookup,
            movementInteractionLookup = movementInteractionLookup,
            interactionCells = spatialHash.interactionCells,
            cellSize = SpatialHashSystem.CELL_SIZE,
            scoringLibrary = scoringLibrary.library
        }.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    [WithAll(typeof(ActiveBrain))]
    public partial struct MovementScoringJob : IJobEntity
    {
        [ReadOnly] public ComponentLookup<InteractionProvider> interactionProviderLookup;
        [ReadOnly] public ComponentLookup<LocalTransform> transformLookup;
        [ReadOnly] public ComponentLookup<MovementInteraction> movementInteractionLookup;
        [ReadOnly] public NativeParallelMultiHashMap<SpatialInteractionKey, Entity> interactionCells;
        [ReadOnly] public BlobAssetReference<AIScoringLibraryBlob> scoringLibrary;
        public float cellSize;

        public void Execute(
            ref DynamicBuffer<ActionOption> options,
            in MovementMotivation movementMotivation,
            in Awareness awareness,
            in LocalTransform transform,
            EnabledRefRO<NeedsAction> needsAction)
        {
            if (!needsAction.ValueRO)
                return;

            float3 pos = transform.Position;

            NativeList<Entity> nearby = new NativeList<Entity>(8, Allocator.Temp);

            // Only queries interactions that have MovementInteraction component
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
                MovementInteraction interaction = movementInteractionLookup[candidate];
                float multiplier = interaction.value * 0.01f + 1f;

                float baseScore = AIUtils.ScoreInteraction(
                    candidate, pos, movementMotivation.value,
                    awareness.range, MOTIVATION_TYPE,
                    ref scoringLibrary, ref transformLookup);

                float finalScore = baseScore * multiplier;

                AIUtils.AddActionOption(ref options, ref candidate, finalScore);
            }

            nearby.Dispose();
        }
    }
}