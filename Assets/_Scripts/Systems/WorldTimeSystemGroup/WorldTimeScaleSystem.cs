using Unity.Burst;
using Unity.Entities;

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
        timeScale.ValueRW.current = EaseTimeScale(
            timeScale.ValueRO.current, timeScale.ValueRO.target, timeScale.ValueRO.smoothingPerSecond, deltaSeconds);

        RefRW<WorldTimeScaleDelta> scaledDelta = SystemAPI.GetSingletonRW<WorldTimeScaleDelta>();
        scaledDelta.ValueRW.scaledDeltaSeconds = deltaSeconds * timeScale.ValueRO.current;
    }
}
