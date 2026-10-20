using DotsMovementToolkit;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(MovementSystemGroup))]
[UpdateAfter(typeof(MovementSteeringSystemGroup))]
[UpdateBefore(typeof(MovementExecutionSystemGroup))]
public partial struct DrunkStumbleSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        DrunkStumbleJob job = new DrunkStumbleJob
        {
            elapsedTime = (float)SystemAPI.Time.ElapsedTime
        };
        state.Dependency = job.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
public partial struct DrunkStumbleJob : IJobEntity
{
    const float TipsyThreshold = 0.3f;
    const float WobbleHertz = 0.7f;
    const float WobbleAmplitudePerLevel = 0.6f;
    const float SlowdownPerLevel = 0.35f;

    public float elapsedTime;

    public void Execute(
        Entity entity,
        ref Intoxication intoxication,
        ref Movement movement,
        in LocalTransform localTransform)
    {
        // Undo last frame's wobble first, then apply the new one: a follower may not rewrite targetPosition every frame.
        movement.targetPosition -= intoxication.appliedWobble;
        intoxication.appliedWobble = float3.zero;

        if (intoxication.level <= TipsyThreshold)
        {
            if (intoxication.baseMoveSpeed > 0f)
                movement.moveSpeed = intoxication.baseMoveSpeed;
            return;
        }

        if (intoxication.baseMoveSpeed > 0f)
            movement.moveSpeed = intoxication.baseMoveSpeed * (1f - SlowdownPerLevel * intoxication.level);

        if (!movement.isMoving)
            return;

        float3 toTarget = movement.targetPosition - localTransform.Position;
        toTarget.y = 0f;
        if (math.length(toTarget) < 0.05f)
            return;

        float3 direction = math.normalize(toTarget);
        float3 perpendicular = new float3(-direction.z, 0f, direction.x);
        float phase = elapsedTime * math.PI * 2f * WobbleHertz + entity.Index * 1.7f;
        float3 wobble = perpendicular * (math.sin(phase) * WobbleAmplitudePerLevel * intoxication.level);

        movement.targetPosition += wobble;
        intoxication.appliedWobble = wobble;
    }
}
