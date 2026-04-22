using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(AIAwarenessSystemGroup))]
public partial struct EnvironmentalAwarenessSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var registry = SystemAPI.GetSingleton<SpatialHashRegistry>();
        
        // We need TransformLookup to calculate distance once we find a target entity
        var transformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true);
        // We need InteractionLookup to know what ActionType the object provides
        var interactionLookup = SystemAPI.GetComponentLookup<Interaction>(true);

        state.Dependency = new EnvironmentalAwarenessJob
        {
            registry = registry,
            transformLookup = transformLookup,
            interactionLookup = interactionLookup
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(ActiveBrain))]
public partial struct EnvironmentalAwarenessJob : IJobEntity
{
    [ReadOnly] public SpatialHashRegistry registry;
    [ReadOnly] public ComponentLookup<LocalTransform> transformLookup;
    [ReadOnly] public ComponentLookup<Interaction> interactionLookup;

    public void Execute(
        Entity entity, 
        ref DynamicBuffer<ActionOption> options, 
        in DynamicBuffer<Motivation> motivations, // Look at what we care about
        in LocalTransform transform, 
        in Awareness awareness)
    {
        options.Clear();

        float3 npcPos = transform.Position;
        int2 centerCell = SpatialHashSystem.GetCell(npcPos);
        int cellRange = (int)math.ceil(awareness.range / SpatialHashSystem.CELL_SIZE);

        // 1. Iterate through every motivation this NPC has
        for (int m = 0; m < motivations.Length; m++)
        {
            MotivationType currentNeed = motivations[m].motivationType;
            if (currentNeed == MotivationType.None) continue;

            // 2. Search surrounding cells for this specific need
            for (int x = -cellRange; x <= cellRange; x++)
            {
                for (int z = -cellRange; z <= cellRange; z++)
                {
                    int2 targetCell = centerCell + new int2(x, z);
                    
                    // Construct the key for this cell + this motivation
                    var searchKey = new SpatialInteractionKey(targetCell, currentNeed);

                    if (registry.interactionCells.TryGetFirstValue(searchKey, out Entity target, out var it))
                    {
                        do
                        {
                            AddActionIfValid(target, npcPos, awareness.range, ref options);
                        } while (registry.interactionCells.TryGetNextValue(out target, ref it));
                    }
                }
            }
        }
    }

    private void AddActionIfValid(Entity target, float3 npcPos, float maxRange, ref DynamicBuffer<ActionOption> options)
    {
        if (transformLookup.TryGetComponent(target, out var targetTransform))
        {
            float dist = math.distance(npcPos, targetTransform.Position);
            
            if (dist <= maxRange)
            {
                // Physical feasibility (0.0 to 1.0)
                float distScore = 1.0f - math.saturate(dist / maxRange);

                if (interactionLookup.TryGetComponent(target, out var interactData))
                {
                    options.Add(new ActionOption
                    {
                        actionType = interactData.actionType,
                        motivationType = interactData.motivationType,
                        targetEntity = target,
                        interaction = true,
                        utilityScore = distScore // Pure environmental score
                    });
                }
            }
        }
    }
}