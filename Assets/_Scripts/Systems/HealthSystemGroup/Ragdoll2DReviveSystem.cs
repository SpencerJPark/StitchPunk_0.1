using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Cleans up the fake ragdoll when a unit is revived.
/// Runs after ReviveSystem (which disables Dead).
/// Detects "just revived but ragdoll still active" by querying
/// Ragdoll2DConfig with Dead disabled, then checking whether
/// Ragdoll2D is still enabled on the visual child.
/// </summary>
[UpdateInGroup(typeof(HealthSystemGroup))]
[UpdateAfter(typeof(ReviveRequestSystem))]
public partial struct Ragdoll2DReviveSystem : ISystem
{
    private ComponentLookup<Ragdoll2D>       ragdollLookup;
    private ComponentLookup<Ragdoll2DJoint>  jointLookup;
    private ComponentLookup<Ragdoll2DLaunch> launchLookup;
    private ComponentLookup<LocalTransform>    transformLookup;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        ragdollLookup   = state.GetComponentLookup<Ragdoll2D>(false);
        jointLookup     = state.GetComponentLookup<Ragdoll2DJoint>(false);
        launchLookup    = state.GetComponentLookup<Ragdoll2DLaunch>(false);
        transformLookup = state.GetComponentLookup<LocalTransform>(false);
    }

    public void OnUpdate(ref SystemState state)
    {
        ragdollLookup.Update(ref state);
        jointLookup.Update(ref state);
        launchLookup.Update(ref state);
        transformLookup.Update(ref state);

        foreach (var (config, parts, rootEntity) in
            SystemAPI.Query<
                RefRO<Ragdoll2DConfig>,
                DynamicBuffer<BodyPart>>()
                    .WithDisabled<Dead>()
                    .WithEntityAccess())
        {
            Entity visualRoot = config.ValueRO.visualRoot;
            if (!ragdollLookup.HasComponent(visualRoot)) continue;

            // Nothing to clean up if ragdoll was never activated
            if (!ragdollLookup.IsComponentEnabled(visualRoot)) continue;

            // Disable + zero the launch. The root stays where the corpse came to rest — the driver
            // already landed it on real ground (raycast), so no stored groundY restore is needed.
            if (launchLookup.HasComponent(rootEntity) && launchLookup.IsComponentEnabled(rootEntity))
            {
                launchLookup.GetRefRW(rootEntity).ValueRW = new Ragdoll2DLaunch
                {
                    velocity    = float3.zero,
                    restitution = 0f,
                    airborne    = 0,
                    sleeping    = 0,
                };
                launchLookup.SetComponentEnabled(rootEntity, false);
            }

            // Reset and disable body tilt
            Ragdoll2D ragdoll = ragdollLookup[visualRoot];
            ragdollLookup.SetComponentEnabled(visualRoot, false);

            if (transformLookup.HasComponent(visualRoot))
            {
                ref LocalTransform t = ref transformLookup.GetRefRW(visualRoot).ValueRW;
                t.Rotation = ragdoll.initialRotation;
            }

            // Reset and disable each ragdoll joint (RagdollJoint-flagged BodyPart entries)
            for (int i = 0; i < parts.Length; i++)
            {
                if ((parts[i].flags & BodyPartFlags.RagdollJoint) == 0) continue;

                Entity jointEntity = parts[i].entity;
                if (jointEntity == Entity.Null) continue;
                if (!jointLookup.HasComponent(jointEntity)) continue;
                if (!jointLookup.IsComponentEnabled(jointEntity)) continue;

                Ragdoll2DJoint joint = jointLookup[jointEntity];
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
