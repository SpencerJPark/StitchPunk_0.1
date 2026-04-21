using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Unified motivation scoring system. Iterates the Motivation buffer on each AI entity
/// and scores every entry using the same formula: EvaluateCurve(value) * contextMultiplier.
///
/// Two scoring modes:
///   Interaction — queries the spatial hash for nearby entities of this motivationType,
///                 writes an ActionOption per candidate (category = actionCategory, target = entity)
///   Action      — no spatial query; scores directly and writes one ActionOption
///                 (category = actionCategory, no target — execution systems use Target component)
///
/// Awareness systems (AIAwarenessSystemGroup) are responsible for updating motivation values
/// and contextMultipliers to reflect what the unit can currently act on. Scoring is pure.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(AIScoringSystemGroup))]
public partial struct HungerScoringSystem : ISystem
{

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<ScoringLibrary>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        interactionProviderLookup.Update(ref state);
        transformLookup.Update(ref state);
        satisfactionLookup.Update(ref state);

        SpatialHashRegistry spatialHash    = SystemAPI.GetSingleton<SpatialHashRegistry>();
        ScoringLibrary      scoringLibrary = SystemAPI.GetSingleton<ScoringLibrary>();

        state.Dependency = new MotivationScoringJob
        {
            interactionProviderLookup = interactionProviderLookup,
            transformLookup           = transformLookup,
            satisfactionLookup        = satisfactionLookup,
            interactionCells          = spatialHash.interactionCells,
            cellSize                  = SpatialHashSystem.CELL_SIZE,
            scoringLibrary            = scoringLibrary.library,
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(ActiveBrain))]
[WithAll(typeof(NeedsAction))]
public partial struct MotivationScoringJob : IJobEntity
{
    public void Execute(
        ref DynamicBuffer<ActionOption> options,
        in  DynamicBuffer<Motivation>   motivations,
        in  Awareness                   awareness,
        in  LocalTransform              transform)
    {
        if (!needsAction.ValueRO)
            return;

        float3 pos = transform.Position;
        NativeList<Entity> nearby = new NativeList<Entity>(8, Allocator.Temp);

        for (int m = 0; m < motivations.Length; m++)
        {
            Motivation motivation = motivations[m];

            switch (motivation.scoringMode)
            {
                case ScoringMode.Interaction:
                    nearby.Clear();
                    AIUtils.QueryNearbyInteractionsByType(
                        in interactionCells,
                        in interactionProviderLookup,
                        in transformLookup,
                        pos,
                        awareness.range,
                        cellSize,
                        motivation.motivationType,
                        ref nearby);

                    for (int i = 0; i < nearby.Length; i++)
                    {
                        Entity candidate = nearby[i];

                        // Pull the per-behaviour multiplier from the provider's satisfaction buffer.
                        // The linear scan is bounded by how many behaviours a single provider
                        // satisfies (typically 1–3), so it's cheap.
                        float interactionMultiplier = 1f;
                        if (satisfactionLookup.TryGetBuffer(candidate,
                            out DynamicBuffer<BehaviourSatisfaction> satisfaction))
                        {
                            for (int entryIndex = 0; entryIndex < satisfaction.Length; entryIndex++)
                            {
                                if (satisfaction[entryIndex].motivationType != motivation.motivationType)
                                    continue;
                                interactionMultiplier = satisfaction[entryIndex].multiplier;
                                break;
                            }
                        }

                        float baseScore = AIUtils.ScoreInteraction(
                            candidate, pos, motivation.value,
                            awareness.range, motivation.motivationType,
                            ref scoringLibrary, ref transformLookup);

                        float finalScore = baseScore * motivation.contextMultiplier * interactionMultiplier;
                        AIUtils.AddActionOption(ref options, ref candidate, finalScore, motivation.motivationType);
                    }
                    break;

                case ScoringMode.Action:
                    float score = AIUtils.EvaluateScoringCurve(
                        ref scoringLibrary, motivation.motivationType, motivation.value)
                        * motivation.contextMultiplier;

                    if (score > 0f)
                    {
                        options.Add(new ActionOption
                        {
                            utilityScore         = score,
                            category      = motivation.actionCategory,
                            motivationType = motivation.motivationType,
                            targetEntity  = Entity.Null,
                        });
                    }
                    break;
            }
        }

        nearby.Dispose();
    }
}
