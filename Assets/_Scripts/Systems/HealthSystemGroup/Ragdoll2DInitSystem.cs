using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Detects freshly dead units and enables/resets the fake ragdoll components.
/// Dead stays enabled until revived — Ragdoll2DReviveSystem handles cleanup.
/// Reads Health.kill* (captured by DamageEventSystem on the lethal DamageEvent) to
/// determine which side the killing blow came from so the body falls away from the attacker.
/// </summary>
[UpdateInGroup(typeof(HealthSystemGroup))]
[UpdateAfter(typeof(DeathSystem))]
public partial struct Ragdoll2DInitSystem : ISystem
{
    private ComponentLookup<Ragdoll2D>       ragdollLookup;
    private ComponentLookup<Ragdoll2DLaunch> launchLookup;
    private ComponentLookup<LocalTransform>    transformLookup;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        ragdollLookup   = state.GetComponentLookup<Ragdoll2D>(false);
        launchLookup    = state.GetComponentLookup<Ragdoll2DLaunch>(false);
        transformLookup = state.GetComponentLookup<LocalTransform>(true);
    }

    public void OnUpdate(ref SystemState state)
    {
        ragdollLookup.Update(ref state);
        launchLookup.Update(ref state);
        transformLookup.Update(ref state);

        foreach (var (config, joints, zones, health, entity) in
            SystemAPI.Query<
                RefRO<Ragdoll2DConfig>,
                DynamicBuffer<Ragdoll2DJointRef>,
                DynamicBuffer<Ragdoll2DJointZone>,
                RefRO<Health>>()
                    .WithAll<Dead>()
                    .WithPresent<Ragdoll2DLaunch>()
                    .WithEntityAccess())
        {
            Entity visualRoot = config.ValueRO.visualRoot;
            if (!ragdollLookup.HasComponent(visualRoot)) continue;

            // Skip if already ragdolling (Dead stays enabled until revived)
            if (ragdollLookup.IsComponentEnabled(visualRoot)) continue;

            // Determine fall direction from the kill source captured by DamageEventSystem
            // on the lethal DamageEvent, read here from Health.killSourceX.
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
            ref Ragdoll2D ragdoll = ref ragdollLookup.GetRefRW(visualRoot).ValueRW;
            ragdoll.groundBuffer    = fallSideSign >= 0f
                ? config.ValueRO.groundBufferForward
                : config.ValueRO.groundBufferBackward;
            ragdoll.tiltOffset      = fallSideSign >= 0f
                ? config.ValueRO.tiltOffsetForward
                : config.ValueRO.tiltOffsetBackward;
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
                launchLookup.GetRefRW(entity).ValueRW = new Ragdoll2DLaunch
                {
                    velocityX = -fallSideSign * health.ValueRO.killLaunchForceX,
                    velocityY = health.ValueRO.killLaunchForceY,
                    groundY   = groundY
                };
                launchLookup.SetComponentEnabled(entity, true);
            }

            // Reset and enable each joint — pick a random target angle from the authored landing zones
            var rng = new Unity.Mathematics.Random((uint)(entity.Index + 1));

            for (int i = 0; i < joints.Length; i++)
            {
                Entity jointEntity = joints[i].joint;
                if (jointEntity == Entity.Null) continue;
                if (!state.EntityManager.HasComponent<Ragdoll2DJoint>(jointEntity)) continue;

                LocalTransform jointTransform = transformLookup.HasComponent(jointEntity)
                    ? transformLookup[jointEntity]
                    : LocalTransform.Identity;

                // Pick a random zone then a random angle within it
                float targetAngle = 0f;
                int zoneCount = joints[i].zoneCount;
                if (zoneCount > 0)
                {
                    int zoneIdx = joints[i].zoneStart + rng.NextInt(0, zoneCount);
                    var zone = zones[zoneIdx];
                    targetAngle = rng.NextFloat(zone.min, zone.max);
                }

                state.EntityManager.SetComponentData(jointEntity, new Ragdoll2DJoint
                {
                    settleSpeed          = joints[i].settleSpeed,
                    targetAngle          = targetAngle,
                    currentZAngle        = 0f,
                    initialLocalRotation = jointTransform.Rotation
                });
                state.EntityManager.SetComponentEnabled<Ragdoll2DJoint>(jointEntity, true);
            }
        }
    }
}
