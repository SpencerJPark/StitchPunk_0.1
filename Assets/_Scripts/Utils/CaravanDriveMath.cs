using Unity.Mathematics;

// The caravan's arcade handling (Caravan_System.md CV-D4), pure so it can be tested without a World.
public static class CaravanDriveMath
{
    private const float InputDeadZone = 0.05f;
    // Speed below which "pull back" is treated as reversing rather than braking.
    private const float StoppedSpeed = 0.1f;
    // Turning fades in over this much speed, so a stopped cart cannot spin on the spot.
    private const float FullSteeringSpeed = 1.5f;

    public static float IntegrateForwardSpeed(float forwardSpeed, float throttle, in CaravanDriveTuning tuning, float deltaTime)
    {
        if (throttle > InputDeadZone)
        {
            if (forwardSpeed < -StoppedSpeed)
                return MoveTowards(forwardSpeed, 0f, tuning.brakeDeceleration * deltaTime);
            return MoveTowards(forwardSpeed, tuning.maxForwardSpeed * throttle, tuning.acceleration * deltaTime);
        }

        if (throttle < -InputDeadZone)
        {
            if (forwardSpeed > StoppedSpeed)
                return MoveTowards(forwardSpeed, 0f, tuning.brakeDeceleration * deltaTime);
            return MoveTowards(forwardSpeed, tuning.maxReverseSpeed * throttle, tuning.acceleration * deltaTime);
        }

        return MoveTowards(forwardSpeed, 0f, tuning.coastDeceleration * deltaTime);
    }

    // Positive turn input = steer right. Reversing swings the nose the other way, like a real cart.
    public static float YawDegreesThisFrame(float forwardSpeed, float turnInput, in CaravanDriveTuning tuning, float deltaTime)
    {
        float absoluteSpeed = math.abs(forwardSpeed);
        float speedFraction = math.saturate(absoluteSpeed / math.max(tuning.maxForwardSpeed, 0.0001f));
        float turnRate = math.lerp(tuning.turnDegreesPerSecondAtLowSpeed, tuning.turnDegreesPerSecondAtFullSpeed, speedFraction);
        float steeringAuthority = math.saturate(absoluteSpeed / FullSteeringSpeed);
        return turnRate * math.clamp(turnInput, -1f, 1f) * math.sign(forwardSpeed) * steeringAuthority * deltaTime;
    }

    public static float AdvanceWheelSpin(float wheelSpinRadians, float forwardSpeed, float wheelRadius, float deltaTime)
    {
        float spun = wheelSpinRadians + forwardSpeed * deltaTime / math.max(wheelRadius, 0.01f);
        float fullTurn = 2f * math.PI;
        return spun - fullTurn * math.floor(spun / fullTurn);
    }

    private static float MoveTowards(float current, float target, float maximumStep)
    {
        if (math.abs(target - current) <= maximumStep) return target;
        return current + math.sign(target - current) * maximumStep;
    }
}
