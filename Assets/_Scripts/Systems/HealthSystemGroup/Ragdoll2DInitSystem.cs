using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Detects freshly dead units and enables/resets the ragdoll components.
/// Dead stays enabled until revived — Ragdoll2DReviveSystem handles cleanup.
/// Reads Health.kill* (captured by DamageEventSystem on the lethal DamageEvent): the full 3D
/// kill-source position drives both the fall side (fallSideSign, X only — the tilt is planar) and
/// the real 3D launch direction (horizontal away from the source + authored up-velocity).
///
/// Joints are discovered through the root's BodyPart buffer (RagdollJoint-flagged entries); each
/// joint's landing zones come from the PartLibrary blob via its PartDefId, and its baked
/// settleSpeed / segmentLength / weight (resolved at bake) are preserved across the state reset.
/// The flail is seeded with a launch-proportional angular kick so limbs trail from frame one.
/// </summary>
[UpdateInGroup(typeof(HealthSystemGroup))]
[UpdateAfter(typeof(DeathSystem))]
public partial struct Ragdoll2DInitSystem : ISystem
{
    // Mirrors RagdollConfigSO.defaultRestitution for the pre-scene-wiring fallback.
    private const float FALLBACK_RESTITUTION = 0.3f;
    // Fraction of the launch speed converted into the initial limb trail kick.
    private const float LAUNCH_KICK_SCALE    = 0.25f;

    private ComponentLookup<Ragdoll2D>       ragdollLookup;
    private ComponentLookup<Ragdoll2DLaunch> launchLookup;
    private ComponentLookup<LocalTransform>  transformLookup;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PartLibrary>();
        ragdollLookup   = state.GetComponentLookup<Ragdoll2D>(false);
        launchLookup    = state.GetComponentLookup<Ragdoll2DLaunch>(false);
        transformLookup = state.GetComponentLookup<LocalTransform>(true);
    }

    public void OnUpdate(ref SystemState state)
    {
        PartLibrary library = SystemAPI.GetSingleton<PartLibrary>();
        if (!library.library.IsCreated)
            return;

        float defaultRestitution = SystemAPI.TryGetSingleton(out RagdollSimConfig simConfig)
            ? simConfig.defaultRestitution
            : FALLBACK_RESTITUTION;

        ragdollLookup.Update(ref state);
        launchLookup.Update(ref state);
        transformLookup.Update(ref state);

        foreach ((RefRO<Ragdoll2DConfig> config, DynamicBuffer<BodyPart> parts, RefRO<Health> health, Entity entity) in
            SystemAPI.Query<
                RefRO<Ragdoll2DConfig>,
                DynamicBuffer<BodyPart>,
                RefRO<Health>>()
                    .WithAll<Dead>()
                    .WithPresent<Ragdoll2DLaunch>()
                    .WithEntityAccess())
        {
            Entity visualRoot = config.ValueRO.visualRoot;
            if (!ragdollLookup.HasComponent(visualRoot)) continue;

            // Skip if already ragdolling (Dead stays enabled until revived)
            if (ragdollLookup.IsComponentEnabled(visualRoot)) continue;

            // Determine fall direction from the kill source captured on the lethal DamageEvent.
            //   source left of unit  → fall right → negative Z → fallSideSign = -1
            //   source right of unit → fall left  → positive Z → fallSideSign = +1
            float3 unitPosition = transformLookup.HasComponent(entity)
                ? transformLookup[entity].Position
                : float3.zero;
            float fallSideSign = health.ValueRO.killSourcePosition.x < unitPosition.x ? -1f : 1f;

            float ragdollForce   = math.max(0.1f, health.ValueRO.killRagdollForce);
            float flailIntensity = health.ValueRO.killFlailIntensity > 0f
                ? health.ValueRO.killFlailIntensity
                : 1f;

            // Reset and enable body tilt — fallSpeed scales how fast the body tips over; spin adds
            // an airborne tumble in the tilt direction (winds extra turns, settles to the nearest).
            ref Ragdoll2D ragdoll = ref ragdollLookup.GetRefRW(visualRoot).ValueRW;
            ragdoll.groundBuffer    = fallSideSign >= 0f
                ? config.ValueRO.groundBufferForward
                : config.ValueRO.groundBufferBackward;
            ragdoll.tiltOffset      = fallSideSign >= 0f
                ? config.ValueRO.tiltOffsetForward
                : config.ValueRO.tiltOffsetBackward;
            ragdoll.fallSpeed       = config.ValueRO.fallSpeed * ragdollForce;
            ragdoll.flailIntensity  = flailIntensity;
            ragdoll.spin            = health.ValueRO.killSpin * fallSideSign;
            ragdoll.bodyZAngle      = 0f;
            ragdoll.fallSideSign    = fallSideSign;
            ragdoll.initialRotation = transformLookup.HasComponent(visualRoot)
                ? transformLookup[visualRoot].Rotation
                : quaternion.identity;
            ragdollLookup.SetComponentEnabled(visualRoot, true);

            // 3D launch: horizontal away from the kill source, authored up-velocity. A zero-launch
            // kill still starts airborne with zero velocity — the first driver frame grounds it via
            // the raycast, which also handles dying over a ledge.
            float3 launchVelocity = float3.zero;
            if (launchLookup.HasComponent(entity))
            {
                float2 horizontalDelta = new float2(
                    unitPosition.x - health.ValueRO.killSourcePosition.x,
                    unitPosition.z - health.ValueRO.killSourcePosition.z);
                float horizontalDistance = math.length(horizontalDelta);
                float2 horizontalDirection = horizontalDistance > 1e-4f
                    ? horizontalDelta / horizontalDistance
                    : new float2(-fallSideSign, 0f); // degenerate (source above): fall-side X

                launchVelocity = new float3(
                    horizontalDirection.x * health.ValueRO.killLaunchForceX,
                    health.ValueRO.killLaunchForceY,
                    horizontalDirection.y * health.ValueRO.killLaunchForceX);

                launchLookup.GetRefRW(entity).ValueRW = new Ragdoll2DLaunch
                {
                    velocity    = launchVelocity,
                    restitution = health.ValueRO.killRestitution > 0f
                        ? health.ValueRO.killRestitution
                        : defaultRestitution,
                    airborne    = 1,
                    sleeping    = 0,
                };
                launchLookup.SetComponentEnabled(entity, true);
            }

            // Reset and enable each ragdoll joint — pick a random target angle from the blob zones
            // and seed the flail with a launch-proportional trail kick.
            Random random = new Random((uint)(entity.Index + 1));
            float launchSpeed = math.length(launchVelocity);

            for (int i = 0; i < parts.Length; i++)
            {
                if ((parts[i].flags & BodyPartFlags.RagdollJoint) == 0) continue;

                Entity jointEntity = parts[i].entity;
                if (jointEntity == Entity.Null) continue;
                if (!state.EntityManager.HasComponent<Ragdoll2DJoint>(jointEntity)) continue;

                LocalTransform jointTransform = transformLookup.HasComponent(jointEntity)
                    ? transformLookup[jointEntity]
                    : LocalTransform.Identity;

                float targetAngle = 0f;
                int defIndex = (int)parts[i].partDef;
                if (defIndex >= 0 && defIndex < library.library.Value.parts.Length)
                {
                    ref PartDef def = ref library.library.Value.parts[defIndex];
                    if (def.zones.Length > 0)
                    {
                        int zoneIndex = random.NextInt(0, def.zones.Length);
                        float2 zone = def.zones[zoneIndex];
                        targetAngle = random.NextFloat(zone.x, zone.y);
                    }
                }

                // Preserve the bake-resolved fields across the state reset.
                Ragdoll2DJoint bakedJoint = state.EntityManager.GetComponentData<Ragdoll2DJoint>(jointEntity);
                float segmentLength = math.max(bakedJoint.segmentLength, 0.05f);

                // Limbs trail opposite the launch, jittered so a horde doesn't flail in unison.
                float trailKick = -fallSideSign
                    * math.degrees(launchSpeed / segmentLength)
                    * LAUNCH_KICK_SCALE
                    * bakedJoint.weight * flailIntensity
                    * random.NextFloat(0.6f, 1.2f);

                state.EntityManager.SetComponentData(jointEntity, new Ragdoll2DJoint
                {
                    settleSpeed          = bakedJoint.settleSpeed,
                    segmentLength        = bakedJoint.segmentLength,
                    weight               = bakedJoint.weight,
                    targetAngle          = targetAngle,
                    currentZAngle        = 0f,
                    angularVelocity      = trailKick,
                    initialLocalRotation = jointTransform.Rotation,
                });
                state.EntityManager.SetComponentEnabled<Ragdoll2DJoint>(jointEntity, true);
            }
        }
    }
}
