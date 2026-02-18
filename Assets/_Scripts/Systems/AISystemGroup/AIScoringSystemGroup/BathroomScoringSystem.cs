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

        new BathroomScoringJob
        {
            bathroomInteractionLookup = bathroomInteractionLookup,
            interactionProviderLookup = interactionProviderLookup,
            transformLookup = transformLookup,
            waypointCells = spatialHash.waypointCells,
            cellSize = SpatialHashSystem.CELL_SIZE,
            scoringLibrary = scoringLibrary.library
        }.ScheduleParallel();
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
                in waypointCells,
                in interactionProviderLookup,
                in transformLookup,
                pos,
                awareness.range,
                cellSize,
                ref nearby);

            FindBestBathroom(in nearby, pos, bladder.value, awareness.range, out Entity bestTarget, out float bestScore);

            nearby.Dispose();

            AIUtil.AddActionOption(ref options, ref bestTarget, bestScore);
        }

        private void FindBestBathroom(
            in NativeList<Entity> nearby,
            float3 pos,
            float needValue,
            float awarenessRange,
            out Entity bestTarget,
            out float bestScore)
        {
            bestScore = float.MinValue;
            bestTarget = Entity.Null;

            for (int i = 0; i < nearby.Length; i++)
            {
                Entity candidate = nearby[i];

                if (!bathroomInteractionLookup.HasComponent(candidate))
                    continue;

                if (!interactionProviderLookup.IsComponentEnabled(candidate))
                    continue;

                float score = ScoreCandidate(candidate, pos, needValue, awarenessRange);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestTarget = candidate;
                }
            }
        }

        private float ScoreCandidate(Entity candidate, float3 pos, float needValue, float awarenessRange)
        {
            float3 targetPos = transformLookup[candidate].Position;
            float distance = math.distance(pos, targetPos);

            float baseScore = AIUtil.EvaluateScoringCurve(
                ref scoringLibrary, BATHROOM_MOTIVATION, needValue);

            float distanceBonus = math.remap(0f, awarenessRange, 10f, 0f, distance);

            return math.clamp(baseScore + distanceBonus, -100f, 100f);
        }
    }
}