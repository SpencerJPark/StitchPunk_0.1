using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
public static class EntityUtils
{
    public static Random CreateRandom(int entityIndex, float time)
    {
        uint seed = (uint)(entityIndex + 1) * 747796405u + (uint)(time * 1000f);
        seed = math.max(seed, 1u);
        return new Random(seed);
    }
}