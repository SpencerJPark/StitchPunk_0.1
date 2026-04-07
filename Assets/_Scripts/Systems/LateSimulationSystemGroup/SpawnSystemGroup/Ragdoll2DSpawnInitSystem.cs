using Unity.Collections;
using Unity.Entities;

/// <summary>
/// Resets ragdoll enabled states on newly instantiated body entities.
///
/// ECB.Instantiate does not reliably copy IEnableableComponent enabled bits on
/// child entities. Ragdoll2D (visual child) and Ragdoll2DJoint (joint pivots) can
/// end up enabled at spawn even though they should start disabled. This causes
/// Ragdoll2DInitSystem to see them as already active and skip death-init — the
/// body then stays upright with fallSideSign 0 ("freezes") when killed.
///
/// Runs after UnitSpawnerSystem, reads Ragdoll2DConfig to reach the visual child
/// and the joints buffer to reach each pivot, then force-disables both.
/// </summary>
[UpdateInGroup(typeof(SpawnSystemGroup))]
[UpdateAfter(typeof(UnitSpawnerSystem))]
public partial struct Ragdoll2DSpawnInitSystem : ISystem
{
    private ComponentLookup<Ragdoll2D>      ragdollLookup;
    private ComponentLookup<Ragdoll2DJoint> jointLookup;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        ragdollLookup = state.GetComponentLookup<Ragdoll2D>(false);
        jointLookup   = state.GetComponentLookup<Ragdoll2DJoint>(false);
    }

    public void OnUpdate(ref SystemState state)
    {
        ragdollLookup.Update(ref state);
        jointLookup.Update(ref state);

        var ecb = new EntityCommandBuffer(Allocator.Temp);

        foreach (var (config, joints, entity) in
            SystemAPI.Query<
                RefRO<Ragdoll2DConfig>,
                DynamicBuffer<Ragdoll2DJointRef>>()
                    .WithAll<NeedsRagdollInit>()
                    .WithEntityAccess())
        {
            // Force-disable body tilt on the visual child entity.
            Entity visualRoot = config.ValueRO.visualRoot;
            if (ragdollLookup.HasComponent(visualRoot))
                ragdollLookup.SetComponentEnabled(visualRoot, false);

            // Force-disable every joint pivot.
            for (int i = 0; i < joints.Length; i++)
            {
                Entity joint = joints[i].joint;
                if (joint != Entity.Null && jointLookup.HasComponent(joint))
                    jointLookup.SetComponentEnabled(joint, false);
            }

            ecb.RemoveComponent<NeedsRagdollInit>(entity);
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }
}
