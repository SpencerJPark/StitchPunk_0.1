using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
public partial struct FloatingWorldOriginSystem : ISystem
{
    private double nextCheckTime;

    private const double CHECK_INTERVAL = 10.0; // seconds
    private const float MAX_DISTANCE_FROM_ORIGIN = 500f;  // world units

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Player>();
        nextCheckTime = 0;
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        double time = SystemAPI.Time.ElapsedTime;
        if (time < nextCheckTime)
        {
            return;
        }

        nextCheckTime = time + CHECK_INTERVAL;

        // ───────────────────────────────────────────────
        // 1. Get the player entity (tag + only one)
        // ───────────────────────────────────────────────
        Entity playerEntity = SystemAPI.GetSingletonEntity<Player>();

        // ───────────────────────────────────────────────
        // 2. Get its LocalTransform
        // ───────────────────────────────────────────────
        LocalTransform playerTransform = SystemAPI.GetComponent<LocalTransform>(playerEntity);
        float3 playerPosition = playerTransform.Position;

        // ───────────────────────────────────────────────
        // 3. Check distance
        // ───────────────────────────────────────────────
        float distanceSquared = math.lengthsq(playerPosition);
        float maxDistanceSquared = MAX_DISTANCE_FROM_ORIGIN * MAX_DISTANCE_FROM_ORIGIN;

        if (distanceSquared < maxDistanceSquared)
        {
            return;
        }

        // ───────────────────────────────────────────────
        // 4. Compute offset (negative of player position)
        // ───────────────────────────────────────────────
        float3 offset = -playerPosition;

        // ───────────────────────────────────────────────
        // 5. Schedule job to shift all LocalTransforms
        // ───────────────────────────────────────────────
        ShiftWorldJob shiftJob = new ShiftWorldJob
        {
            offset = offset
        };

        state.Dependency = shiftJob.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
public partial struct ShiftWorldJob : IJobEntity
{
    public float3 offset;

    public void Execute(ref LocalTransform transform)
    {
        transform.Position += offset;
    }
}
