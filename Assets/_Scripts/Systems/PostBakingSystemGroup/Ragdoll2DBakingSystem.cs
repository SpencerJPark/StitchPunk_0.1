using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Adds Ragdoll2D (disabled) to the visual child entity and Ragdoll2DJoint (disabled)
/// to each joint pivot for every body that has Ragdoll2DConfig.
///
/// These components sit on child entities, so they cannot be added by a Baker
/// (bakers may only write to their own GO's entity). This baking-world system
/// handles the cross-entity distribution.
///
/// Ragdoll2DLaunch lives on the ROOT entity and is baked directly by
/// Ragdoll2DAuthoring.Baker — no baking system needed for it.
///
/// EntityQueryOptions.IncludePrefabs is required so prefab assets are processed
/// alongside scene-placed instances. Without it, instantiated units at runtime
/// would be missing Ragdoll2D / Ragdoll2DJoint on their child entities.
/// </summary>
[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
[UpdateInGroup(typeof(PostBakingSystemGroup))]
public partial struct Ragdoll2DBakingSystem : ISystem
{
    private EntityQuery _query;

    public void OnCreate(ref SystemState state)
    {
        _query = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<Ragdoll2DConfig, Ragdoll2DJointRef>()
            .WithOptions(EntityQueryOptions.IncludePrefab)
            .Build(ref state);
    }

    public void OnUpdate(ref SystemState state)
    {
        var em = state.EntityManager;

        var visualRoots = new NativeList<(Entity entity, Ragdoll2D data)>(Allocator.Temp);
        var jointParts  = new NativeList<(Entity entity, Ragdoll2DJoint data)>(Allocator.Temp);

        var entities = _query.ToEntityArray(Allocator.Temp);

        for (int e = 0; e < entities.Length; e++)
        {
            Entity rootEntity = entities[e];
            Ragdoll2DConfig config = em.GetComponentData<Ragdoll2DConfig>(rootEntity);
            DynamicBuffer<Ragdoll2DJointRef> joints = em.GetBuffer<Ragdoll2DJointRef>(rootEntity);

            Entity visualRoot = config.visualRoot;
            if (visualRoot != Entity.Null && !em.HasComponent<Ragdoll2D>(visualRoot))
            {
                visualRoots.Add((visualRoot, new Ragdoll2D
                {
                    fallSpeed       = config.fallSpeed,
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

        entities.Dispose();

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

        visualRoots.Dispose();
        jointParts.Dispose();
    }
}
