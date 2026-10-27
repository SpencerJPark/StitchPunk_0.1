using Unity.Mathematics;

// The caravan's handling, ported from the pre-DOTS VehicleController the owner tuned (Caravan_System.md CV-D17):
// the stick is a world direction to head toward and its tilt is the target speed; the heading eases round with a
// smoothed angle, and the body follows the heading at a capped turn rate. Pure so it can be tested without a World.
public static class CaravanDriveMath
{
    public static float IntegrateSpeed(float currentSpeed, float targetSpeed, in CaravanDriveTuning tuning, float deltaTime)
    {
        float rate = targetSpeed > currentSpeed ? tuning.forwardAcceleration : tuning.forwardDeceleration;
        return MoveTowards(currentSpeed, targetSpeed, rate * deltaTime);
    }

    // Turning is still possible from a standstill, just slower (idleTurnSpeedFactor), as in the original.
    public static float TurnSpeedLimit(float currentSpeed, in CaravanDriveTuning tuning)
    {
        float speedFraction = math.saturate(currentSpeed / math.max(tuning.moveSpeed, 0.0001f));
        return tuning.turnSpeed * math.lerp(tuning.idleTurnSpeedFactor, 1f, speedFraction);
    }

    // Unity's Mathf.SmoothDampAngle, ported so Burst can run it. Degrees.
    public static float SmoothDampAngle(float currentDegrees, float targetDegrees, ref float velocityDegreesPerSecond,
        float smoothTime, float maximumSpeed, float deltaTime)
    {
        float unwrappedTarget = currentDegrees + DeltaAngle(currentDegrees, targetDegrees);
        return SmoothDamp(currentDegrees, unwrappedTarget, ref velocityDegreesPerSecond, smoothTime, maximumSpeed, deltaTime);
    }

    public static float DeltaAngle(float fromDegrees, float toDegrees)
    {
        float difference = Repeat(toDegrees - fromDegrees, 360f);
        return difference > 180f ? difference - 360f : difference;
    }

    // Yaw in degrees of a flat direction, 0 = +Z, clockwise seen from above (Unity's Atan2(x, z)).
    public static float HeadingDegrees(float3 direction)
    {
        return math.degrees(math.atan2(direction.x, direction.z));
    }

    public static float3 HeadingDirection(float headingDegrees)
    {
        float headingRadians = math.radians(headingDegrees);
        return new float3(math.sin(headingRadians), 0f, math.cos(headingRadians));
    }

    public static float AdvanceWheelSpin(float wheelSpinRadians, float forwardSpeed, float wheelRadius, float deltaTime)
    {
        float spun = wheelSpinRadians + forwardSpeed * deltaTime / math.max(wheelRadius, 0.01f);
        float fullTurn = 2f * math.PI;
        return spun - fullTurn * math.floor(spun / fullTurn);
    }

    private static float SmoothDamp(float current, float target, ref float velocity, float smoothTime, float maximumSpeed, float deltaTime)
    {
        smoothTime = math.max(0.0001f, smoothTime);
        float omega = 2f / smoothTime;
        float omegaStep = omega * deltaTime;
        float decay = 1f / (1f + omegaStep + 0.48f * omegaStep * omegaStep + 0.235f * omegaStep * omegaStep * omegaStep);
        float originalTarget = target;
        float maximumChange = maximumSpeed * smoothTime;
        float change = math.clamp(current - target, -maximumChange, maximumChange);
        target = current - change;
        float springStep = (velocity + omega * change) * deltaTime;
        velocity = (velocity - omega * springStep) * decay;
        float output = target + (change + springStep) * decay;
        // Never overshoot the real target.
        if ((originalTarget - current > 0f) == (output > originalTarget))
        {
            output = originalTarget;
            velocity = deltaTime > 0f ? (output - originalTarget) / deltaTime : 0f;
        }
        return output;
    }

    private static float Repeat(float value, float length)
    {
        return math.clamp(value - math.floor(value / length) * length, 0f, length);
    }

    private static float MoveTowards(float current, float target, float maximumStep)
    {
        if (math.abs(target - current) <= maximumStep) return target;
        return current + math.sign(target - current) * maximumStep;
    }
}
