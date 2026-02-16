using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
public static class AIUtil
{
    public static void AddActionOption(ref DynamicBuffer<ActionOption> options, ref Entity interactionEntity, float score)
    {
        if (interactionEntity != Entity.Null)
        {
            options.Add(new ActionOption
            {
                interactableEntity = interactionEntity,
                score = score
            });
        }
    }
    
    public static float EvaluateScoringCurve(
        ref BlobAssetReference<AIScoringLibraryBlob> library,
        MotivationType motivationType,
        float needValue)
    {
        ref AIScoringLibraryBlob blob = ref library.Value;

        for (int i = 0; i < blob.curves.Length; i++)
        {
            if (blob.curves[i].motivationType == motivationType)
                return blob.curves[i].curve.Evaluate(needValue);
        }

        return -needValue;
    }
    
    public static void QueryNearbyInteractions(
        in NativeParallelMultiHashMap<int2, Entity> waypointCells,
        in ComponentLookup<InteractionProvider> interactionLookup,
        in ComponentLookup<LocalTransform> transformLookup,
        float3 position,
        float range,
        float cellSize,
        ref NativeList<Entity> results)
    {
        float rangeSq = range * range;

        int2 minCell = new int2(
            (int)math.floor((position.x - range) / cellSize),
            (int)math.floor((position.z - range) / cellSize));
        int2 maxCell = new int2(
            (int)math.floor((position.x + range) / cellSize),
            (int)math.floor((position.z + range) / cellSize));

        for (int x = minCell.x; x <= maxCell.x; x++)
        {
            for (int y = minCell.y; y <= maxCell.y; y++)
            {
                if (!waypointCells.TryGetFirstValue(new int2(x, y), out Entity candidate, out var iterator))
                    continue;

                do
                {
                    if (!interactionLookup.TryGetComponent(candidate, out InteractionProvider interaction))
                        continue;

                    if (!transformLookup.TryGetComponent(candidate, out LocalTransform targetTransform))
                        continue;

                    float distSq = math.distancesq(position, targetTransform.Position);

                    if (distSq > rangeSq || distSq > interaction.broadcastRadius * interaction.broadcastRadius)
                        continue;

                    results.Add(candidate);

                } while (waypointCells.TryGetNextValue(out candidate, ref iterator));
            }
        }
    }
}