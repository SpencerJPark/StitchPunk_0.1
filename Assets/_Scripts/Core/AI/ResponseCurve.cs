using Unity.Burst;
using Unity.Mathematics;

[BurstCompile]
public static class ResponseCurve
{
    public static float Linear(float x)
    {
        return math.saturate(x);
    }

    public static float InverseLinear(float x)
    {
        return math.saturate(1f - x);
    }

    public static float Exponential(float x, float exponent)
    {
        return math.saturate(math.pow(x, exponent));
    }

    public static float InverseExponential(float x, float exponent)
    {
        return math.saturate(math.pow(1f - x, exponent));
    }

    public static float Logistic(float x, float steepness, float midpoint)
    {
        return math.saturate(1f / (1f + math.exp(-steepness * (x - midpoint))));
    }

    public static float Bell(float x, float midpoint, float width)
    {
        float dist = (x - midpoint) / width;
        return math.saturate(math.exp(-dist * dist));
    }

    public static float Threshold(float x, float threshold)
    {
        return x >= threshold ? 1f : 0f;
    }

    public static float SmoothStep(float x, float min, float max)
    {
        float t = math.saturate((x - min) / (max - min));
        return t * t * (3f - 2f * t);
    }
}