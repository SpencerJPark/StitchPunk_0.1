using DotsAnimationToolkit;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// The fake-ragdoll twin of RagdollLaunchInitSystem: a freshly dead unit with no toolkit ragdoll bodies is flung by
// its killing blow (Health.kill*, the same numbers the toolkit launch reads) and stays down while Dead is enabled.
// A unit already knocked down (e.g. run over by the caravan) is left alone; it just never gets up.
[BurstCompile]
[UpdateInGroup(typeof(HealthSystemGroup))]
[UpdateAfter(typeof(DeathSystem))]
public partial struct FakeRagdollDeathSystem : ISystem
{
    private const float CORPSE_LIE_SECONDS = 1.5f;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<FakeRagdoll>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        EntityQuery freshlyDeadQuery = SystemAPI.QueryBuilder()
            .WithAll<Dead, FakeRagdollRig, Health, LocalTransform>()
            .WithDisabled<FakeRagdoll>()
            .WithNone<Player>()
            .Build();
        if (freshlyDeadQuery.IsEmpty) return;
        // Main-thread lookup writes below wait for nothing on their own (Gotchas.md).
        state.CompleteDependency();

        NativeArray<Entity> freshlyDeadUnits = freshlyDeadQuery.ToEntityArray(Allocator.Temp);
        for (int unitIndex = 0; unitIndex < freshlyDeadUnits.Length; unitIndex++)
        {
            Entity unitEntity = freshlyDeadUnits[unitIndex];
            if (SystemAPI.HasBuffer<RagdollBody>(unitEntity) && SystemAPI.GetBuffer<RagdollBody>(unitEntity).Length > 0) continue;

            Health health = SystemAPI.GetComponent<Health>(unitEntity);
            float3 unitPosition = SystemAPI.GetComponent<LocalTransform>(unitEntity).Position;
            float3 awayFromKiller = unitPosition - health.killSourcePosition;
            awayFromKiller.y = 0f;
            float3 launchDirection = math.normalizesafe(awayFromKiller, new float3(1f, 0f, 0f));
            float ragdollForce = math.max(0.1f, health.killRagdollForce);
            float3 launchVelocity = new float3(
                launchDirection.x * health.killLaunchForceX,
                health.killLaunchForceY,
                launchDirection.z * health.killLaunchForceX) * ragdollForce;

            FakeRagdoll fakeRagdoll = SystemAPI.GetComponent<FakeRagdoll>(unitEntity);
            FakeRagdollControl.Knock(ref fakeRagdoll, unitPosition, launchVelocity, CORPSE_LIE_SECONDS, false);
            // A killing blow with no launch at all still tips over the way it was hit.
            if (math.lengthsq(launchVelocity.xz) < 0.0001f) fakeRagdoll.fallDirection = launchDirection;
            SystemAPI.SetComponent(unitEntity, fakeRagdoll);
            SystemAPI.SetComponentEnabled<FakeRagdoll>(unitEntity, true);
        }
        freshlyDeadUnits.Dispose();
    }
}
