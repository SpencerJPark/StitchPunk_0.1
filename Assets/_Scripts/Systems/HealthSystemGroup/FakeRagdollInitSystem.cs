using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Detects freshly dead units and enables/resets the fake ragdoll components.
/// Dead stays enabled until revived — FakeRagdollReviveSystem handles cleanup.
/// Reads the Hurt buffer to determine which side the killing blow came from
/// so the body falls away from the attacker.
/// </summary>
[UpdateInGroup(typeof(HealthSystemGroup))]
[UpdateAfter(typeof(DeathSystem))]
public partial struct FakeRagdollInitSystem : ISystem
{
    private ComponentLookup<FakeRagdoll>       ragdollLookup;
    private ComponentLookup<FakeRagdollLaunch> launchLookup;
    private ComponentLookup<LocalTransform>    transformLookup;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        ragdollLookup   = state.GetComponentLookup<FakeRagdoll>(false);
        launchLookup    = state.GetComponentLookup<FakeRagdollLaunch>(false);
        transformLookup = state.GetComponentLookup<LocalTransform>(true);
    }

    public void OnUpdate(ref SystemState state)
    {
        ragdollLookup.Update(ref state);
        launchLookup.Update(ref state);
        transformLookup.Update(ref state);

        foreach (var (config, joints, health, entity) in
            SystemAPI.Query<
                RefRO<FakeRagdollConfig>,
                DynamicBuffer<FakeRagdollJointRef>,
                RefRO<Health>>()
                    .WithAll<Dead>()
                    .WithPresent<FakeRagdollLaunch>()
                    .WithEntityAccess())
        {
            Entity visualRoot = config.ValueRO.visualRoot;
            if (!ragdollLookup.HasComponent(visualRoot)) continue;

            // Skip if already ragdolling (Dead stays enabled until revived)
            if (ragdollLookup.IsComponentEnabled(visualRoot)) continue;

            // Determine fall direction from the kill source baked by DamageApplicationJob.
            // The Hurt buffer is already cleared by the time we run, so we read Health.killSourceX.
            // Positive Z rotation tilts the character top to the LEFT:
            //   source left of unit  → fall right → negative Z → fallSideSign = -1
            //   source right of unit → fall left  → positive Z → fallSideSign = +1
            float fallSideSign = -1f;
            if (transformLookup.HasComponent(entity))
            {
                float unitX = transformLookup[entity].Position.x;
                fallSideSign = health.ValueRO.killSourceX < unitX ? -1f : 1f;
            }

            float ragdollForce = math.max(0.1f, health.ValueRO.killRagdollForce);

            // Reset and enable body tilt — fallSpeed scales how fast the body tips over
            ref FakeRagdoll ragdoll = ref ragdollLookup.GetRefRW(visualRoot).ValueRW;
            ragdoll.groundBuffer    = config.ValueRO.groundBuffer;
            ragdoll.fallSpeed       = config.ValueRO.fallSpeed * ragdollForce;
            ragdoll.bodyZAngle      = 0f;
            ragdoll.fallSideSign    = fallSideSign;
            ragdoll.initialRotation = transformLookup.HasComponent(visualRoot)
                ? transformLookup[visualRoot].Rotation
                : quaternion.identity;
            ragdollLookup.SetComponentEnabled(visualRoot, true);

            // Enable arc launch on the root entity.
            // launchForceY/X are direct velocities (units/s) authored per-attack — no scaling.
            if (launchLookup.HasComponent(entity))
            {
                float groundY = transformLookup.HasComponent(entity) ? transformLookup[entity].Position.y : 0f;
                launchLookup.GetRefRW(entity).ValueRW = new FakeRagdollLaunch
                {
                    velocityX = -fallSideSign * health.ValueRO.killLaunchForceX,
                    velocityY = health.ValueRO.killLaunchForceY,
                    groundY   = groundY
                };
                launchLookup.SetComponentEnabled(entity, true);
            }

            // Reset and enable each joint — flail velocity scales with ragdollForce
            var rng = new Unity.Mathematics.Random((uint)(entity.Index + 1));

            for (int i = 0; i < joints.Length; i++)
            {
                Entity jointEntity = joints[i].joint;
                if (jointEntity == Entity.Null) continue;
                if (!state.EntityManager.HasComponent<FakeRagdollJoint>(jointEntity)) continue;

                LocalTransform jointTransform = transformLookup.HasComponent(jointEntity)
                    ? transformLookup[jointEntity]
                    : LocalTransform.Identity;

                float buf = joints[i].groundBuffer > 0f ? joints[i].groundBuffer : config.ValueRO.groundBuffer;
                state.EntityManager.SetComponentData(jointEntity, new FakeRagdollJoint
                {
                    groundBuffer         = buf,
                    zAngularVelocity     = rng.NextFloat(120f, 360f) * (rng.NextBool() ? 1f : -1f) * ragdollForce,
                    currentZAngle        = 0f,
                    initialLocalRotation = jointTransform.Rotation
                });
                state.EntityManager.SetComponentEnabled<FakeRagdollJoint>(jointEntity, true);
            }
        }
    }
}
