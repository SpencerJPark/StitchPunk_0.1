using Unity.Mathematics;

// Starts a knock-down. Every producer (caravan crash, a death fling) goes through here so the fields agree.
public static class FakeRagdollControl
{
    public static void Knock(ref FakeRagdoll fakeRagdoll, float3 rootPosition, float3 launchVelocity, float lieSeconds,
        bool drivesToolkitRagdoll)
    {
        float3 horizontalLaunch = new float3(launchVelocity.x, 0f, launchVelocity.z);
        fakeRagdoll.phase = FakeRagdollPhase.Airborne;
        fakeRagdoll.phaseSeconds = 0f;
        fakeRagdoll.position = rootPosition;
        fakeRagdoll.velocity = launchVelocity;
        fakeRagdoll.fallDirection = math.normalizesafe(horizontalLaunch, new float3(1f, 0f, 0f));
        fakeRagdoll.groundHeight = rootPosition.y;
        fakeRagdoll.lieSeconds = lieSeconds;
        fakeRagdoll.toppleDegrees = 0f;
        fakeRagdoll.drivesToolkitRagdoll = drivesToolkitRagdoll;
    }
}
