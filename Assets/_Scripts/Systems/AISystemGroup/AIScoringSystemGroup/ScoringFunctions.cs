using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;

// Define the signature that all behavior functions must follow
public delegate float ScoringAction(Entity entity, int index,  EntityCommandBuffer ecb);

public static class ScoringFunctions
{
    [BurstCompile]
    public static float ScorePunch(Entity entity, int index,  EntityCommandBuffer ecb)
    {
        entity 
    }
}