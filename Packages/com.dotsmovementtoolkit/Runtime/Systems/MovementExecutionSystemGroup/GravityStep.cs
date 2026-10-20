using Unity.Mathematics;

namespace DotsMovementToolkit
{
public struct GravityStepResult
{
    public float positionY;
    public float verticalVelocity;
    public bool  isGrounded;
}

public static class GravityStep
{
    public const float PROBE_HEIGHT           = 0.5f;  // ground ray starts this far above the feet, so a unit pushed slightly into the floor is snapped back up
    public const float SNAP_TOLERANCE         = 0.1f;
    public const float RECOVERY_FALL_DISTANCE = 20f;   // falling this far below the last ground with nothing underneath returns the entity there

    // The ray must reach past this frame's fall step, or a hitch frame carries the unit below the floor unseen.
    public static float SweepDistance(float verticalVelocity, float fallSpeed, float deltaTime, float maxCheckDistance)
    {
        float predictedVelocity = verticalVelocity - fallSpeed * deltaTime;
        return math.max(maxCheckDistance, -predictedVelocity * deltaTime + SNAP_TOLERANCE);
    }

    public static GravityStepResult Resolve(float positionY, float verticalVelocity, float fallSpeed, float deltaTime, bool hasGround, float groundY)
    {
        float velocity = verticalVelocity - fallSpeed * deltaTime;
        float fallenY  = positionY + velocity * deltaTime;

        if (!hasGround)
        {
            return new GravityStepResult { positionY = fallenY, verticalVelocity = velocity, isGrounded = false };
        }

        bool airborne        = positionY - groundY > SNAP_TOLERANCE;
        bool reachesTheFloor = fallenY <= groundY;
        if (airborne && !reachesTheFloor)
        {
            return new GravityStepResult { positionY = fallenY, verticalVelocity = velocity, isGrounded = false };
        }

        return new GravityStepResult { positionY = groundY, verticalVelocity = 0f, isGrounded = true };
    }
}
}
