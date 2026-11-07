using Unity.Entities;
using Unity.Mathematics;

// Singleton driven by camera zoom (TT-D20). Only the world clock follows it; physics and animation stay real-time.
public struct WorldTimeScale : IComponentData
{
    public float current;
    public float target;
    public float smoothingPerSecond;
}

// Real frame delta multiplied by WorldTimeScale.current; WorldClockSystem advances by this when present.
public struct WorldTimeScaleDelta : IComponentData
{
    public float scaledDeltaSeconds;
}

public static class WorldTimeScaleMath
{
    public const float SnapDistance = 0.001f;

    public static float EaseTimeScale(float current, float target, float smoothingPerSecond, float deltaSeconds)
    {
        if (smoothingPerSecond <= 0f)
            return target;

        float blend = 1f - math.exp(-smoothingPerSecond * math.max(deltaSeconds, 0f));
        float eased = current + (target - current) * blend;
        return math.abs(target - eased) < SnapDistance ? target : eased;
    }
}
