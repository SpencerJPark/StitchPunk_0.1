using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Runs first in UtilityAwarenessSystemGroup each selection cycle.
/// Resets contextMultiplier to 1.0 (pre-pass systems set it fresh each frame)
/// and applies decay to motivation values.
/// Only processes entities that currently need an action — mid-execution units skip decay.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(UtilityMotivationSystemGroup))]
[UpdateAfter(typeof(MotivationChangeRequestSystem))]
public partial struct MotivationDecaySystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        state.Dependency = new MotivationDecayJob
        {
            deltaTime = deltaTime
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(UtilityBrain), typeof(ActionRequest))]
public partial struct MotivationDecayJob : IJobEntity
{
    public float deltaTime;

    public void Execute(ref DynamicBuffer<Motivation> motivations)
    {
        for (int i = 0; i < motivations.Length; i++)
        {
            Motivation motivation = motivations[i];
            motivation.value = math.clamp(motivation.value - motivation.decayRate * deltaTime, 0f, 100f);
            motivations[i] = motivation;
        }
    }
}
