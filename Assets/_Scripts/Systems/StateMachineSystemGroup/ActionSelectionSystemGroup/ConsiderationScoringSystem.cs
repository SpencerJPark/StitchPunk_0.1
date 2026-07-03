using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Scores each UtilityActions buffer entry on the owning unit using pre-sampled blob consideration curves.
// Reads health, motivation, distance, and personality inline — no separate ContextAssemblySystem needed.
// Uses Dave Mark's modification factor so adding more considerations doesn't collapse the total score.
[BurstCompile]
[UpdateInGroup(typeof(ActionSelectionSystemGroup))]
[UpdateBefore(typeof(WinnerSelectionSystem))]
public partial struct ConsiderationScoringSystem : ISystem
{
    private ComponentLookup<LocalTransform>      _targetTransformLookup;
    private BufferLookup<PersonalityAttributes>  _personalityLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<BrainLibrary>();
        _targetTransformLookup = state.GetComponentLookup<LocalTransform>(true);
        _personalityLookup     = state.GetBufferLookup<PersonalityAttributes>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        _targetTransformLookup.Update(ref state);
        _personalityLookup.Update(ref state);

        BrainLibrary brainLibrary = SystemAPI.GetSingleton<BrainLibrary>();

        state.Dependency = new ConsiderationScoringJob
        {
            aiConfig               = brainLibrary.blob,
            targetTransformLookup  = _targetTransformLookup,
            personalityLookup      = _personalityLookup,
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(UtilityBrain))]
public partial struct ConsiderationScoringJob : IJobEntity
{
    [ReadOnly] public BlobAssetReference<BrainLibraryBlob>   aiConfig;
    [ReadOnly] public ComponentLookup<LocalTransform>        targetTransformLookup;
    [ReadOnly] public BufferLookup<PersonalityAttributes>    personalityLookup;

    public void Execute(
        Entity                                   unit,
        in LocalTransform                        unitTransform,
        in Health                                health,
        in Awareness                             awareness,
        in DynamicBuffer<Motivation>             motivations,
        ref DynamicBuffer<UtilityActions>        actions)
    {
        float healthRatio = health.healthAmountMax > 0
            ? (float)health.healthAmount / health.healthAmountMax
            : 1f;

        for (int i = 0; i < actions.Length; i++)
        {
            UtilityActions entry = actions[i];

            if (entry.actionDefIndex < 0 || entry.actionDefIndex >= aiConfig.Value.actionDefs.Length)
                continue;

            ref ActionDefBlob actionDef = ref aiConfig.Value.actionDefs[entry.actionDefIndex];
            // Preserve awareness-set priority: normal emitters leave it 0 so actionDef wins;
            // awareness systems (e.g. SelfDefence) may set it higher and must not be stomped.
            entry.priority = math.max(actionDef.priority, entry.priority);

            int considerationCount = actionDef.considerations.Length;
            if (considerationCount == 0)
            {
                entry.totalUtility = 1f;
                actions[i] = entry;
                continue;
            }

            float targetDist = 0f;
            if (entry.targetEntity != Entity.Null
                && targetTransformLookup.TryGetComponent(entry.targetEntity, out LocalTransform tgt))
                targetDist = math.distance(unitTransform.Position, tgt.Position);

            // 0 = on top of the target, 1 = at the edge of this unit's awareness range.
            float normalizedDist = math.saturate(targetDist / math.max(awareness.range, 0.001f));

            float product = 1f;
            for (int c = 0; c < considerationCount; c++)
            {
                ref ConsiderationBlob consideration = ref actionDef.considerations[c];
                float inputValue = ReadInput(unit, ref consideration, healthRatio, normalizedDist, in motivations);
                float curveScore = consideration.Evaluate(inputValue);
                product *= curveScore * consideration.weight;
            }

            float modFactor = 1f - (1f / considerationCount);
            entry.totalUtility = product + (1f - product) * modFactor * product;
            actions[i] = entry;
        }
    }

    private float ReadInput(
        Entity                           unit,
        ref ConsiderationBlob            consideration,
        float                            healthRatio,
        float                            normalizedDist,
        in DynamicBuffer<Motivation>     motivations)
    {
        switch (consideration.input)
        {
            case ConsiderationType.Health:
                return healthRatio;

            case ConsiderationType.Motivation:
                return GetMotivationRatio(in motivations, consideration.needType);

            case ConsiderationType.DistanceToTarget:
                return normalizedDist;

            case ConsiderationType.Trait:
                return GetTraitValue(unit, consideration.traitType);

            default:
                return 0f;
        }
    }

    private static float GetMotivationRatio(in DynamicBuffer<Motivation> motivations, NeedType needType)
    {
        for (int i = 0; i < motivations.Length; i++)
        {
            if (motivations[i].needType == needType)
                return (motivations[i].value + 100f) / 200f;
        }
        return 0f;
    }

    private float GetTraitValue(Entity unit, PersonalityTypes traitType)
    {
        if (!personalityLookup.HasBuffer(unit)) return 0.5f;
        DynamicBuffer<PersonalityAttributes> traits = personalityLookup[unit];
        for (int i = 0; i < traits.Length; i++)
        {
            if (traits[i].personality == traitType)
                return math.saturate(traits[i].value);
        }
        return 0.5f;
    }
}
