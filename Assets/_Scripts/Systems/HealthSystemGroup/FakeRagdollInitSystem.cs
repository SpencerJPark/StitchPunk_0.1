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
    private ComponentLookup<FakeRagdoll>    ragdollLookup;
    private ComponentLookup<LocalTransform> transformLookup;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        ragdollLookup   = state.GetComponentLookup<FakeRagdoll>(false);
        transformLookup = state.GetComponentLookup<LocalTransform>(true);
    }

    public void OnUpdate(ref SystemState state)
    {
        ragdollLookup.Update(ref state);
        transformLookup.Update(ref state);

        foreach (var (config, joints, hurtBuffer, entity) in
            SystemAPI.Query<
                RefRO<FakeRagdollConfig>,
                DynamicBuffer<FakeRagdollJointRef>,
                DynamicBuffer<Hurt>>()
                    .WithAll<Dead>()
                    .WithEntityAccess())
        {
            Entity visualRoot = config.ValueRO.visualRoot;
            if (!ragdollLookup.HasComponent(visualRoot)) continue;

            // Skip if already ragdolling (Dead stays enabled until revived)
            if (ragdollLookup.IsComponentEnabled(visualRoot)) continue;

            // Determine fall direction from the last hit's attacker position.
            // Unit falls away from attacker: attacker on left (+X) → fall right (positive tilt),
            // attacker on right (-X) → fall left (negative tilt). Default to +1 if unknown.
            float fallSideSign = 1f;
            if (hurtBuffer.Length > 0 && transformLookup.HasComponent(entity))
            {
                Entity attackerEntity = hurtBuffer[hurtBuffer.Length - 1].attackerEntity;
                if (attackerEntity != Entity.Null && transformLookup.HasComponent(attackerEntity))
                {
                    float3 unitPos     = transformLookup[entity].Position;
                    float3 attackerPos = transformLookup[attackerEntity].Position;
                    // Attacker left of unit (lower X) → unit falls right (+tilt), sign = +1
                    // Attacker right of unit (higher X) → unit falls left (-tilt), sign = -1
                    fallSideSign = attackerPos.x < unitPos.x ? 1f : -1f;
                }
            }

            // Reset and enable body tilt
            ref FakeRagdoll ragdoll = ref ragdollLookup.GetRefRW(visualRoot).ValueRW;
            ragdoll.bodyZAngle      = 0f;
            ragdoll.fallSideSign    = fallSideSign;
            ragdoll.initialRotation = transformLookup.HasComponent(visualRoot)
                ? transformLookup[visualRoot].Rotation
                : quaternion.identity;
            ragdollLookup.SetComponentEnabled(visualRoot, true);

            // Reset and enable each joint with a randomised flail velocity
            var rng = new Unity.Mathematics.Random((uint)(entity.Index + 1));

            for (int i = 0; i < joints.Length; i++)
            {
                Entity jointEntity = joints[i].joint;
                if (jointEntity == Entity.Null) continue;
                if (!state.EntityManager.HasComponent<FakeRagdollJoint>(jointEntity)) continue;

                LocalTransform jointTransform = transformLookup.HasComponent(jointEntity)
                    ? transformLookup[jointEntity]
                    : LocalTransform.Identity;

                state.EntityManager.SetComponentData(jointEntity, new FakeRagdollJoint
                {
                    groundBuffer         = config.ValueRO.groundBuffer,
                    zAngularVelocity     = rng.NextFloat(120f, 360f) * (rng.NextBool() ? 1f : -1f),
                    currentZAngle        = 0f,
                    initialLocalRotation = jointTransform.Rotation
                });
                state.EntityManager.SetComponentEnabled<FakeRagdollJoint>(jointEntity, true);
            }
        }
    }
}
