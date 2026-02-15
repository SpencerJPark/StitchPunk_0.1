using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(AIScoringSystemGroup))]
public partial struct BathroomScoringSystem : ISystem
{
    private ComponentLookup<BathroomInteraction> bathroomInteractionLookup;
    private ComponentLookup<InteractionProvider> interactionLookup;
    private ComponentLookup<LocalTransform> transformLookup;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BathroomInteraction>();
        state.RequireForUpdate<SpatialHashSingleton>();

        bathroomInteractionLookup = state.GetComponentLookup<BathroomInteraction>(true);
        interactionLookup = state.GetComponentLookup<InteractionProvider>(true);
        transformLookup = state.GetComponentLookup<LocalTransform>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        bathroomInteractionLookup.Update(ref state);
        interactionLookup.Update(ref state);
        transformLookup.Update(ref state);

        SpatialHashSingleton spatialHash = SystemAPI.GetSingleton<SpatialHashSingleton>();

        new BathroomScoringJob
        {
            bathroomInteractionLookup = bathroomInteractionLookup,
            interactionLookup = interactionLookup,
            transformLookup = transformLookup,
            waypointCells = spatialHash.waypointCells,
            cellSize = SpatialHashSystem.CELL_SIZE
        }.ScheduleParallel();
    }

    [BurstCompile]
    public partial struct BathroomScoringJob : IJobEntity
    {
        [ReadOnly] public ComponentLookup<BathroomInteraction> bathroomInteractionLookup;
        [ReadOnly] public ComponentLookup<InteractionProvider> interactionLookup;
        [ReadOnly] public ComponentLookup<LocalTransform> transformLookup;
        [ReadOnly] public NativeParallelMultiHashMap<int2, Entity> waypointCells;
        public float cellSize;

        public void Execute(
            ref DynamicBuffer<ActionOption> options,
            in Bladder bladder,
            in Awareness awareness,
            in LocalTransform transform,
            EnabledRefRO<NeedsAction> needsAction)
        {
            float baseScore = bladder.value;

            // High value means bladder is fine — no need to search for bathrooms
            // if (baseScore > 50f)
            //     return;

            // Invert: low bladder value = high action score
            float actionScore = -baseScore;

            float3 pos = transform.Position;

            NativeList<Entity> nearby = new NativeList<Entity>(8, Allocator.Temp);
            AIUtil.QueryNearbyInteractions(
                in waypointCells,
                in interactionLookup,
                in transformLookup,
                pos,
                awareness.range,
                cellSize,
                ref nearby);

            FindBestBathroom(in nearby, pos, actionScore, out Entity bestTarget, out float bestScore);

            nearby.Dispose();

            AIUtil.AddActionOption(ref options, ref bestTarget, bestScore);
        }

        private void FindBestBathroom(
            in NativeList<Entity> nearby,
            float3 pos,
            float baseScore,
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
                
                if (!interactionLookup.IsComponentEnabled(candidate))
                    continue;

                float score = ScoreCandidate(candidate, pos, baseScore);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestTarget = candidate;
                }
            }
        }

        private float ScoreCandidate(Entity candidate, float3 pos, float baseScore)
        {
            InteractionProvider interactionProvider = interactionLookup[candidate];
            float3 targetPos = transformLookup[candidate].Position;
            float distance = math.distance(pos, targetPos);

            float distanceBonus = math.remap(0f, interactionProvider.broadcastRadius, 10f, 0f, distance);
            return math.clamp(baseScore + distanceBonus, -100f, 100f);
        }
    }
}