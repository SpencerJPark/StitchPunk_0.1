using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Detects freshly dead units and enables/resets the fake ragdoll components.
/// Dead stays enabled until revived — Ragdoll2DReviveSystem handles cleanup.
/// Reads Health.kill* (captured by DamageEventSystem on the lethal DamageEvent) to
/// determine which side the killing blow came from so the body falls away from the attacker.
///
/// Joints are now discovered through the root's BodyPart buffer (RagdollJoint-flagged entries); each
/// joint's landing zones come from the PartLibrary blob via its PartDefId, and its settle speed is the
/// baked value on the Ragdoll2DJoint component (resolved at bake from override / blob default).
/// </summary>
[UpdateInGroup(typeof(HealthSystemGroup))]
[UpdateAfter(typeof(DeathSystem))]
public partial struct Ragdoll2DInitSystem : ISystem
{
    private ComponentLookup<Ragdoll2D>       ragdollLookup;
    private ComponentLookup<Ragdoll2DLaunch> launchLookup;
    private ComponentLookup<LocalTransform>  transformLookup;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
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

        ragdollLookup.Update(ref state);
        launchLookup.Update(ref state);
        transformLookup.Update(ref state);

        foreach (var (config, parts, health, entity) in
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
            float fallSideSign = -1f;
            if (transformLookup.HasComponent(entity))
            {
                float unitX = transformLookup[entity].Position.x;
                fallSideSign = health.ValueRO.killSourceX < unitX ? -1f : 1f;
            }

            float ragdollForce = math.max(0.1f, health.ValueRO.killRagdollForce);

            // Reset and enable body tilt — fallSpeed scales how fast the body tips over.
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

            // Enable arc launch on the root entity (direct velocities, authored per-attack — no scaling).
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

            // Reset and enable each ragdoll joint — pick a random target angle from the blob zones.
            Random random = new Random((uint)(entity.Index + 1));

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

                float settleSpeed = state.EntityManager.GetComponentData<Ragdoll2DJoint>(jointEntity).settleSpeed;

                state.EntityManager.SetComponentData(jointEntity, new Ragdoll2DJoint
                {
                    settleSpeed          = settleSpeed,
                    targetAngle          = targetAngle,
                    currentZAngle        = 0f,
                    initialLocalRotation = jointTransform.Rotation
                });
                state.EntityManager.SetComponentEnabled<Ragdoll2DJoint>(jointEntity, true);
            }
        }
    }
}
