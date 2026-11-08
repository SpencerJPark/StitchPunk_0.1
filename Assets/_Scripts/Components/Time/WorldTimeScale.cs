using Unity.Entities;
using Unity.Mathematics;

// Singleton driven by camera zoom (TT-D20). StrategicLodDriver mirrors `current` into UnityEngine.Time.timeScale, so the caravan, AI and the
// clock all run fast together (owner call 2026-11-07: JRPG overworld feel). Pause (timeScale 0) always wins.
public struct WorldTimeScale : IComponentData
{
    public float current;
    public float target;
    public float smoothingPerSecond;
}

// The simulation delta, which Time.timeScale has already multiplied by WorldTimeScale.current; WorldClockSystem advances by this when present.
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
