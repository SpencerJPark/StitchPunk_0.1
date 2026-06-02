using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Populates UtilityAction.contextData for every active action entity by reading from the owner unit
// and the action's target entity.
[BurstCompile]
[UpdateInGroup(typeof(UtilityDecisionSystemGroup))]
[UpdateAfter(typeof(ActionInstancingSystem))]
public partial struct ContextAssemblySystem : ISystem
{
    private ComponentLookup<Health>         _healthLookup;
    private BufferLookup<Motivation>        _motivationLookup;
    private ComponentLookup<LocalTransform> _transformLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        _healthLookup     = state.GetComponentLookup<Health>(true);
        _motivationLookup = state.GetBufferLookup<Motivation>(true);
        _transformLookup  = state.GetComponentLookup<LocalTransform>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        _healthLookup.Update(ref state);
        _motivationLookup.Update(ref state);
        _transformLookup.Update(ref state);

        state.Dependency = new ContextAssemblyJob
        {
            healthLookup     = _healthLookup,
            motivationLookup = _motivationLookup,
            transformLookup  = _transformLookup,
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(ActionActive))]
public partial struct ContextAssemblyJob : IJobEntity
{
    [ReadOnly] public ComponentLookup<Health>         healthLookup;
    [ReadOnly] public BufferLookup<Motivation>        motivationLookup;
    [ReadOnly] public ComponentLookup<LocalTransform> transformLookup;

    public void Execute(ref UtilityAction utilityAction)
    {
        Entity owner = utilityAction.ownerEntity;

        utilityAction.contextData.ownerHealthRatio =
            healthLookup.TryGetComponent(owner, out Health health) && health.healthAmountMax > 0
                ? (float)health.healthAmount / health.healthAmountMax
                : 1.0f;

        utilityAction.contextData.ownerNeedRatio = GetMaxNeed(owner, motivationLookup);

        // Calculate real distance to target when one is set.
        if (utilityAction.targetEntity != Unity.Entities.Entity.Null
            && transformLookup.TryGetComponent(owner, out LocalTransform ownerTransform)
            && transformLookup.TryGetComponent(utilityAction.targetEntity, out LocalTransform targetTransform))
        {
            utilityAction.contextData.distanceToTarget =
                math.distance(ownerTransform.Position, targetTransform.Position);
        }
        else
        {
            utilityAction.contextData.distanceToTarget = 0f;
        }
    }

    private static float GetMaxNeed(Entity owner, BufferLookup<Motivation> lookup)
    {
        if (!lookup.HasBuffer(owner)) return 0.5f;

        DynamicBuffer<Motivation> motivations = lookup[owner];
        float maxNormalized = 0f;
        for (int i = 0; i < motivations.Length; i++)
        {
            // Motivation values are in [-100, 100]; normalize to [0, 1].
            float normalized = (motivations[i].value + 100f) / 200f;
            if (normalized > maxNormalized) maxNormalized = normalized;
        }
        return maxNormalized;
    }
}
