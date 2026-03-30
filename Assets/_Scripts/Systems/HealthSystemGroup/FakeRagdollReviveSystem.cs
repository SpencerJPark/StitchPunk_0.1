using Unity.Entities;
using Unity.Transforms;

/// <summary>
/// Cleans up the fake ragdoll when a unit is revived.
/// Runs after ReviveSystem (which disables Dead / enables Alive).
/// Detects "just revived but ragdoll still active" by querying
/// FakeRagdollConfig with Dead disabled, then checking whether
/// FakeRagdoll is still enabled on the visual child.
/// </summary>
[UpdateInGroup(typeof(HealthSystemGroup))]
[UpdateAfter(typeof(ReviveSystem))]
public partial struct FakeRagdollReviveSystem : ISystem
{
    private ComponentLookup<FakeRagdoll>      ragdollLookup;
    private ComponentLookup<FakeRagdollJoint> jointLookup;
    private ComponentLookup<LocalTransform>   transformLookup;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        ragdollLookup   = state.GetComponentLookup<FakeRagdoll>(false);
        jointLookup     = state.GetComponentLookup<FakeRagdollJoint>(false);
        transformLookup = state.GetComponentLookup<LocalTransform>(false);
    }

    public void OnUpdate(ref SystemState state)
    {
        ragdollLookup.Update(ref state);
        jointLookup.Update(ref state);
        transformLookup.Update(ref state);

        foreach (var (config, joints) in
            SystemAPI.Query<
                RefRO<FakeRagdollConfig>,
                DynamicBuffer<FakeRagdollJointRef>>()
                    .WithDisabled<Dead>())
        {
            Entity visualRoot = config.ValueRO.visualRoot;
            if (!ragdollLookup.HasComponent(visualRoot)) continue;

            // Nothing to clean up if ragdoll was never activated
            if (!ragdollLookup.IsComponentEnabled(visualRoot)) continue;

            // Reset and disable body tilt
            FakeRagdoll ragdoll = ragdollLookup[visualRoot];
            ragdollLookup.SetComponentEnabled(visualRoot, false);

            if (transformLookup.HasComponent(visualRoot))
            {
                ref LocalTransform t = ref transformLookup.GetRefRW(visualRoot).ValueRW;
                t.Rotation = ragdoll.initialRotation;
            }

            // Reset and disable each joint
            for (int i = 0; i < joints.Length; i++)
            {
                Entity jointEntity = joints[i].joint;
                if (jointEntity == Entity.Null) continue;
                if (!jointLookup.HasComponent(jointEntity)) continue;
                if (!jointLookup.IsComponentEnabled(jointEntity)) continue;

                FakeRagdollJoint joint = jointLookup[jointEntity];
                jointLookup.SetComponentEnabled(jointEntity, false);

                if (transformLookup.HasComponent(jointEntity))
                {
                    ref LocalTransform t = ref transformLookup.GetRefRW(jointEntity).ValueRW;
                    t.Rotation = joint.initialLocalRotation;
                }
            }
        }
    }
}
