using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
[UpdateInGroup(typeof(PostBakingSystemGroup))]
public partial struct FakeRagdollBakingSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        var em = state.EntityManager;

        var visualRoots = new NativeList<(Entity entity, FakeRagdoll data)>(Allocator.Temp);
        var jointParts  = new NativeList<(Entity entity, FakeRagdollJoint data)>(Allocator.Temp);

        foreach (var (config, joints) in
            SystemAPI.Query<RefRO<FakeRagdollConfig>, DynamicBuffer<FakeRagdollJointRef>>())
        {
            Entity visualRoot = config.ValueRO.visualRoot;

            if (visualRoot != Entity.Null && !em.HasComponent<FakeRagdoll>(visualRoot))
            {
                visualRoots.Add((visualRoot, new FakeRagdoll
                {
                    fallSpeed       = config.ValueRO.fallSpeed,
                    bodyZAngle      = 0f,
                    initialRotation = quaternion.identity
                }));
            }

            for (int i = 0; i < joints.Length; i++)
            {
                Entity joint = joints[i].joint;
                if (joint == Entity.Null) continue;
                if (em.HasComponent<FakeRagdollJoint>(joint)) continue;

                jointParts.Add((joint, new FakeRagdollJoint
                {
                    groundBuffer         = config.ValueRO.groundBuffer,
                    zAngularVelocity     = 0f,
                    currentZAngle        = 0f,
                    initialLocalRotation = quaternion.identity
                }));
            }
        }

        for (int i = 0; i < visualRoots.Length; i++)
        {
            em.AddComponentData(visualRoots[i].entity, visualRoots[i].data);
            em.SetComponentEnabled<FakeRagdoll>(visualRoots[i].entity, false);
        }

        for (int i = 0; i < jointParts.Length; i++)
        {
            em.AddComponentData(jointParts[i].entity, jointParts[i].data);
            em.SetComponentEnabled<FakeRagdollJoint>(jointParts[i].entity, false);
        }

        visualRoots.Dispose();
        jointParts.Dispose();
    }
}
