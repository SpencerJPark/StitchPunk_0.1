using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
[UpdateInGroup(typeof(WorldTimeSystemGroup), OrderFirst = true)]
public partial struct WorldTimeScaleSystem : ISystem
{
    public static float EaseTimeScale(float current, float target, float smoothingPerSecond, float deltaSeconds)
    {
        return WorldTimeScaleMath.EaseTimeScale(current, target, smoothingPerSecond, deltaSeconds);
    }

    // Not Burst-compiled: CreateEntity takes a managed ComponentType[] (BC1028).
    public void OnCreate(ref SystemState state)
    {
        Entity timeScaleEntity = state.EntityManager.CreateEntity(
            ComponentType.ReadWrite<WorldTimeScale>(),
            ComponentType.ReadWrite<WorldTimeScaleDelta>());
        state.EntityManager.SetComponentData(timeScaleEntity, new WorldTimeScale
        {
            current = 1f,
            target = 1f,
            smoothingPerSecond = 4f,
        });
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaSeconds = SystemAPI.Time.DeltaTime;
        RefRW<WorldTimeScale> timeScale = SystemAPI.GetSingletonRW<WorldTimeScale>();
        // The simulation delta is already multiplied by Time.timeScale (= current), so the easing divides it back out; otherwise
        // the ramp would run 4x faster at 4x speed and slowing down would feel like an abrupt stop.
        float unscaledDeltaSeconds = deltaSeconds / math.max(timeScale.ValueRO.current, 0.0001f);
        timeScale.ValueRW.current = EaseTimeScale(
            timeScale.ValueRO.current, timeScale.ValueRO.target, timeScale.ValueRO.smoothingPerSecond, unscaledDeltaSeconds);

        RefRW<WorldTimeScaleDelta> scaledDelta = SystemAPI.GetSingletonRW<WorldTimeScaleDelta>();
        // Not multiplied again: StrategicLodDriver already put the scale into Time.timeScale.
        scaledDelta.ValueRW.scaledDeltaSeconds = deltaSeconds;
    }
}
