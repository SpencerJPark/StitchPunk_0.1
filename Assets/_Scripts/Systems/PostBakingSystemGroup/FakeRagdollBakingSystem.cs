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

        var visualRoots  = new NativeList<(Entity entity, FakeRagdoll data)>(Allocator.Temp);
        var jointParts   = new NativeList<(Entity entity, FakeRagdollJoint data)>(Allocator.Temp);
        var launchRoots  = new NativeList<Entity>(Allocator.Temp);

        foreach (var (config, joints, rootEntity) in
            SystemAPI.Query<RefRO<FakeRagdollConfig>, DynamicBuffer<FakeRagdollJointRef>>()
                .WithEntityAccess())
        {
            if (!em.HasComponent<FakeRagdollLaunch>(rootEntity))
                launchRoots.Add(rootEntity);

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

                float buf = joints[i].groundBuffer > 0f ? joints[i].groundBuffer : config.ValueRO.groundBuffer;
                jointParts.Add((joint, new FakeRagdollJoint
                {
                    groundBuffer         = buf,
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

        for (int i = 0; i < launchRoots.Length; i++)
        {
            em.AddComponentData(launchRoots[i], new FakeRagdollLaunch());
            em.SetComponentEnabled<FakeRagdollLaunch>(launchRoots[i], false);
        }

        visualRoots.Dispose();
        jointParts.Dispose();
        launchRoots.Dispose();
    }
}
