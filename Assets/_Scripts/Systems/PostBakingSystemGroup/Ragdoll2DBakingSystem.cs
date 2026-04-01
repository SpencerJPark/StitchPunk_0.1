using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
[UpdateInGroup(typeof(PostBakingSystemGroup))]
public partial struct Ragdoll2DBakingSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        var em = state.EntityManager;

        var visualRoots  = new NativeList<(Entity entity, Ragdoll2D data)>(Allocator.Temp);
        var jointParts   = new NativeList<(Entity entity, Ragdoll2DJoint data)>(Allocator.Temp);
        var launchRoots  = new NativeList<Entity>(Allocator.Temp);

        foreach (var (config, joints, rootEntity) in
            SystemAPI.Query<RefRO<Ragdoll2DConfig>, DynamicBuffer<Ragdoll2DJointRef>>()
                .WithEntityAccess())
        {
            if (!em.HasComponent<Ragdoll2DLaunch>(rootEntity))
                launchRoots.Add(rootEntity);

            Entity visualRoot = config.ValueRO.visualRoot;

            if (visualRoot != Entity.Null && !em.HasComponent<Ragdoll2D>(visualRoot))
            {
                visualRoots.Add((visualRoot, new Ragdoll2D
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
                if (em.HasComponent<Ragdoll2DJoint>(joint)) continue;

                jointParts.Add((joint, new Ragdoll2DJoint
                {
                    settleSpeed          = joints[i].settleSpeed,
                    targetAngle          = 0f,
                    currentZAngle        = 0f,
                    initialLocalRotation = quaternion.identity
                }));
            }
        }

        for (int i = 0; i < visualRoots.Length; i++)
        {
            em.AddComponentData(visualRoots[i].entity, visualRoots[i].data);
            em.SetComponentEnabled<Ragdoll2D>(visualRoots[i].entity, false);
        }

        for (int i = 0; i < jointParts.Length; i++)
        {
            em.AddComponentData(jointParts[i].entity, jointParts[i].data);
            em.SetComponentEnabled<Ragdoll2DJoint>(jointParts[i].entity, false);
        }

        for (int i = 0; i < launchRoots.Length; i++)
        {
            em.AddComponentData(launchRoots[i], new Ragdoll2DLaunch());
            em.SetComponentEnabled<Ragdoll2DLaunch>(launchRoots[i], false);
        }

        visualRoots.Dispose();
        jointParts.Dispose();
        launchRoots.Dispose();
    }
}
