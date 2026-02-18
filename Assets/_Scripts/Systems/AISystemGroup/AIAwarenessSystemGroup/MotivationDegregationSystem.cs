using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

[BurstCompile]
[UpdateInGroup(typeof(AIAwarenessSystemGroup))]
partial struct MotivationDegradationSystem : ISystem
{
    private const int NUMBER_OF_MOTIVATION_JOBS = 7;
    private NativeArray<JobHandle> jobHandleNativeArray;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        jobHandleNativeArray = new NativeArray<JobHandle>(NUMBER_OF_MOTIVATION_JOBS, Allocator.Persistent);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        if (jobHandleNativeArray.IsCreated)
            jobHandleNativeArray.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;

        jobHandleNativeArray[0] = new HungerDegradationJob
        {
            deltaTime = deltaTime
        }.ScheduleParallel(state.Dependency);

        jobHandleNativeArray[1] = new EnergyDegradationJob
        {
            deltaTime = deltaTime
        }.ScheduleParallel(state.Dependency);

        jobHandleNativeArray[2] = new FunDegradationJob
        {
            deltaTime = deltaTime
        }.ScheduleParallel(state.Dependency);

        jobHandleNativeArray[3] = new SocialDegradationJob
        {
            deltaTime = deltaTime
        }.ScheduleParallel(state.Dependency);

        jobHandleNativeArray[4] = new ComfortDegradationJob
        {
            deltaTime = deltaTime
        }.ScheduleParallel(state.Dependency);

        jobHandleNativeArray[5] = new BladderDegradationJob
        {
            deltaTime = deltaTime
        }.ScheduleParallel(state.Dependency);

        jobHandleNativeArray[6] = new MovementDegradationJob
        {
            deltaTime = deltaTime
        }.ScheduleParallel(state.Dependency);

        state.Dependency = JobHandle.CombineDependencies(jobHandleNativeArray);
    }
}

// -------------------------------------------------------
// HUNGER — degrades steadily, you get hungrier over time
// -------------------------------------------------------
[BurstCompile]
public partial struct HungerDegradationJob : IJobEntity
{
    public float deltaTime;

    // Units per second of degradation
    private const float RATE = 2f;

    public void Execute(ref Hunger hunger)
    {
        float current = hunger.value;
        current -= RATE * deltaTime;
        hunger.value = (int)Unity.Mathematics.math.clamp(current, -100f, 100f);
    }
}

// -------------------------------------------------------
// ENERGY — slow drain, you get tired over time
// -------------------------------------------------------
[BurstCompile]
public partial struct EnergyDegradationJob : IJobEntity
{
    public float deltaTime;

    private const float RATE = 1.5f;

    public void Execute(ref Energy energy)
    {
        float current = energy.value;
        current -= RATE * deltaTime;
        energy.value = (int)Unity.Mathematics.math.clamp(current, -100f, 100f);
    }
}

// -------------------------------------------------------
// FUN — boredom creeps in
// -------------------------------------------------------
[BurstCompile]
public partial struct FunDegradationJob : IJobEntity
{
    public float deltaTime;

    private const float RATE = 1f;

    public void Execute(ref Fun fun)
    {
        float current = fun.value;
        current -= RATE * deltaTime;
        fun.value = (int)Unity.Mathematics.math.clamp(current, -100f, 100f);
    }
}

// -------------------------------------------------------
// SOCIAL — loneliness builds
// -------------------------------------------------------
[BurstCompile]
public partial struct SocialDegradationJob : IJobEntity
{
    public float deltaTime;

    private const float RATE = 0.8f;

    public void Execute(ref Social social)
    {
        float current = social.value;
        current -= RATE * deltaTime;
        social.value = (int)Unity.Mathematics.math.clamp(current, -100f, 100f);
    }
}

// -------------------------------------------------------
// COMFORT — discomfort grows
// -------------------------------------------------------
[BurstCompile]
public partial struct ComfortDegradationJob : IJobEntity
{
    public float deltaTime;

    private const float RATE = 0.5f;

    public void Execute(ref Comfort comfort)
    {
        float current = comfort.value;
        current -= RATE * deltaTime;
        comfort.value = (int)Unity.Mathematics.math.clamp(current, -100f, 100f);
    }
}

// -------------------------------------------------------
// BLADDER — fills up over time
// -------------------------------------------------------
[BurstCompile]
public partial struct BladderDegradationJob : IJobEntity
{
    public float deltaTime;

    private const float RATE = 0.5f;

    public void Execute(ref Bladder bladder)
    {
        float current = bladder.value;
        current -= RATE * deltaTime;
        bladder.value = (int)Unity.Mathematics.math.clamp(current, -100f, 100f);
    }
}

// -------------------------------------------------------
// MOVEMENT — restlessness builds from being sedentary
// -------------------------------------------------------
[BurstCompile]
public partial struct MovementDegradationJob : IJobEntity
{
    public float deltaTime;

    private const float RATE = 3f;

    public void Execute(ref Movement movement)
    {
        float current = movement.value;
        current -= RATE * deltaTime;
        movement.value = (int)Unity.Mathematics.math.clamp(current, -100f, 100f);
    }
}