using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(AIScoringSystemGroup))]
public partial struct BathroomScoringSystem : ISystem
{
    private const MotivationType BATHROOM_MOTIVATION = MotivationType.Bladder;

    private ComponentLookup<BathroomInteraction> bathroomInteractionLookup;
    private ComponentLookup<InteractionProvider> interactionProviderLookup;
    private ComponentLookup<LocalTransform> transformLookup;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BathroomInteraction>();
        state.RequireForUpdate<SpatialHashSingleton>();
        state.RequireForUpdate<ScoringLibrary>();

        bathroomInteractionLookup = state.GetComponentLookup<BathroomInteraction>(true);
        interactionProviderLookup = state.GetComponentLookup<InteractionProvider>(true);
        transformLookup = state.GetComponentLookup<LocalTransform>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        bathroomInteractionLookup.Update(ref state);
        interactionProviderLookup.Update(ref state);
        transformLookup.Update(ref state);

        SpatialHashSingleton spatialHash = SystemAPI.GetSingleton<SpatialHashSingleton>();
        ScoringLibrary scoringLibrary = SystemAPI.GetSingleton<ScoringLibrary>();

        state.Dependency = new BathroomScoringJob
        {
            bathroomInteractionLookup = bathroomInteractionLookup,
            interactionProviderLookup = interactionProviderLookup,
            transformLookup = transformLookup,
            waypointCells = spatialHash.waypointCells,
            cellSize = SpatialHashSystem.CELL_SIZE,
            scoringLibrary = scoringLibrary.library
        }.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    public partial struct BathroomScoringJob : IJobEntity
    {
        [ReadOnly] public ComponentLookup<BathroomInteraction> bathroomInteractionLookup;
        [ReadOnly] public ComponentLookup<InteractionProvider> interactionProviderLookup;
        [ReadOnly] public ComponentLookup<LocalTransform> transformLookup;
        [ReadOnly] public NativeParallelMultiHashMap<int2, Entity> waypointCells;
        [ReadOnly] public BlobAssetReference<AIScoringLibraryBlob> scoringLibrary;
        public float cellSize;

        public void Execute(
            ref DynamicBuffer<ActionOption> options,
            in Bladder bladder,
            in Awareness awareness,
            in LocalTransform transform,
            EnabledRefRO<NeedsAction> needsAction)
        {
            float3 pos = transform.Position;

            NativeList<Entity> nearby = new NativeList<Entity>(8, Allocator.Temp);
            AIUtil.QueryNearbyInteractions(
                in waypointCells, in interactionProviderLookup, in transformLookup,
                pos, awareness.range, cellSize, ref nearby);

            float bestScore = float.MinValue;
            Entity bestTarget = Entity.Null;

            for (int i = 0; i < nearby.Length; i++)
            {
                Entity candidate = nearby[i];

                if (!bathroomInteractionLookup.HasComponent(candidate))
                    continue;

                if (!interactionProviderLookup.IsComponentEnabled(candidate))
                    continue;

                float score = AIUtil.ScoreInteraction(candidate, pos, bladder.value,
                    awareness.range, BATHROOM_MOTIVATION, ref scoringLibrary, ref transformLookup);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestTarget = candidate;
                }
            }

            nearby.Dispose();

            AIUtil.AddActionOption(ref options, ref bestTarget, bestScore);
        }
    }
}