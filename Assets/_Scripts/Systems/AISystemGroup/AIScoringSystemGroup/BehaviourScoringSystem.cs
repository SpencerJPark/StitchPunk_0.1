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
public partial struct BehaviourScoringSystem : ISystem
{
    private ComponentLookup<InteractionProvider> interactionProviderLookup;
    private ComponentLookup<LocalTransform>      transformLookup;
    private ComponentLookup<InteractionValue>    interactionValueLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<SpatialHashRegistry>();
        state.RequireForUpdate<ScoringLibrary>();

        interactionProviderLookup = state.GetComponentLookup<InteractionProvider>(true);
        transformLookup           = state.GetComponentLookup<LocalTransform>(true);
        interactionValueLookup    = state.GetComponentLookup<InteractionValue>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        interactionProviderLookup.Update(ref state);
        transformLookup.Update(ref state);
        interactionValueLookup.Update(ref state);

        SpatialHashRegistry spatialHash    = SystemAPI.GetSingleton<SpatialHashRegistry>();
        ScoringLibrary      scoringLibrary = SystemAPI.GetSingleton<ScoringLibrary>();

        state.Dependency = new MotivationScoringJob
        {
            interactionProviderLookup = interactionProviderLookup,
            transformLookup           = transformLookup,
            interactionValueLookup    = interactionValueLookup,
            interactionCells          = spatialHash.interactionCells,
            cellSize                  = SpatialHashSystem.CELL_SIZE,
            scoringLibrary            = scoringLibrary.library,
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(ActiveBrain))]
public partial struct MotivationScoringJob : IJobEntity
{
    [ReadOnly] public ComponentLookup<InteractionProvider>                      interactionProviderLookup;
    [ReadOnly] public ComponentLookup<LocalTransform>                           transformLookup;
    [ReadOnly] public ComponentLookup<InteractionValue>                         interactionValueLookup;
    [ReadOnly] public NativeParallelMultiHashMap<SpatialInteractionKey, Entity> interactionCells;
    [ReadOnly] public BlobAssetReference<AIScoringLibraryBlob>                  scoringLibrary;
    public float cellSize;

    public void Execute(
        ref DynamicBuffer<ActionOption> options,
        in  DynamicBuffer<Behaviour>   motivations,
        in  Awareness                   awareness,
        in  LocalTransform              transform,
        EnabledRefRO<NeedsAction>       needsAction)
    {
        if (!needsAction.ValueRO)
            return;

        float3 pos = transform.Position;
        NativeList<Entity> nearby = new NativeList<Entity>(8, Allocator.Temp);

        for (int m = 0; m < motivations.Length; m++)
        {
            Behaviour behaviour = motivations[m];

            switch (behaviour.scoringMode)
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
                        behaviour.behaviourType,
                        ref nearby);

                    for (int i = 0; i < nearby.Length; i++)
                    {
                        Entity candidate = nearby[i];

                        float interactionMultiplier = 1f;
                        if (interactionValueLookup.TryGetComponent(candidate, out InteractionValue iv))
                            interactionMultiplier = iv.multiplier;

                        float baseScore = AIUtils.ScoreInteraction(
                            candidate, pos, behaviour.value,
                            awareness.range, behaviour.behaviourType,
                            ref scoringLibrary, ref transformLookup);

                        float finalScore = baseScore * behaviour.contextMultiplier * interactionMultiplier;
                        AIUtils.AddActionOption(ref options, ref candidate, finalScore, behaviour.behaviourType);
                    }
                    break;

                case ScoringMode.Action:
                    float score = AIUtils.EvaluateScoringCurve(
                        ref scoringLibrary, behaviour.behaviourType, behaviour.value)
                        * behaviour.contextMultiplier;

                    if (score > 0f)
                    {
                        options.Add(new ActionOption
                        {
                            score         = score,
                            category      = behaviour.actionCategory,
                            behaviourType = behaviour.behaviourType,
                            targetEntity  = Entity.Null,
                        });
                    }
                    break;
            }
        }

        nearby.Dispose();
    }
}
