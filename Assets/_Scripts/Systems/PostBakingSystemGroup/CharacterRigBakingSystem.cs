using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

// Cross-entity baking for the character rig. Replaces CharacterBodyPartBakingSystem (BodyPart buffer
// assembly) and Ragdoll2DBakingSystem (ragdoll component stamping) in one pass.
//
//   1. Buffer assembly (subscene / non-prefab): clear every BodyPart buffer, then for each BodyPartInfo
//      child map it to its root via BaseParent and append a BodyPart entry. Prefab-instantiated units
//      rebuild their buffer at runtime in BodyPartInitSystem (entity refs aren't reliably remapped).
//   2. Ragdoll stamp (INCLUDING prefabs): add Ragdoll2D (disabled) to each root's visual child and
//      Ragdoll2DJoint (disabled) to each RagdollJoint-flagged part. Joint values come from the part's
//      RagdollJointBakeData (written by RagdollJointAuthoring from its RagdollJointSO — ragdoll is
//      fully separate from the design blob). Component existence must be baked onto prefab children
//      so Instantiate carries it; enabled bits reset at spawn/death.
[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
[UpdateInGroup(typeof(PostBakingSystemGroup))]
public partial struct CharacterRigBakingSystem : ISystem
{
    // Built once in OnCreate rather than per-bake in OnUpdate. Building a query is a lookup against
    // every archetype in the world, so doing it inside OnUpdate is what Unity warns about; a stored
    // query is matched incrementally as archetypes appear. Both need IncludePrefab, which is why they
    // are explicit queries rather than SystemAPI.Query.
    private EntityQuery ragdollRootQuery;
    private EntityQuery bodyPartQuery;

    public void OnCreate(ref SystemState state)
    {
        ragdollRootQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<Ragdoll2DConfig>()
            .WithOptions(EntityQueryOptions.IncludePrefab)
            .Build(ref state);

        bodyPartQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<BodyPartInfo>()
            .WithOptions(EntityQueryOptions.IncludePrefab)
            .Build(ref state);
    }

    public void OnUpdate(ref SystemState state)
    {
        EntityManager entityManager = state.EntityManager;

        // --- 1. BodyPart buffer assembly (non-prefab) ---
        foreach (DynamicBuffer<BodyPart> buffer in SystemAPI.Query<DynamicBuffer<BodyPart>>())
            buffer.Clear();

        foreach (var (info, baseParent, partEntity) in
            SystemAPI.Query<RefRO<BodyPartInfo>, RefRO<BaseParent>>().WithEntityAccess())
        {
            Entity rootEntity = baseParent.ValueRO.baseParentEntity;
            if (!SystemAPI.HasBuffer<BodyPart>(rootEntity)) continue;

            SystemAPI.GetBuffer<BodyPart>(rootEntity).Add(new BodyPart
            {
                entity  = partEntity,
                target  = info.ValueRO.target,
                unitPart = info.ValueRO.unitPart,
                flags   = info.ValueRO.flags,
            });
        }

        // --- 2. Ragdoll stamp (INCLUDING prefabs) ---
        NativeList<Entity> visualRoots = new NativeList<Entity>(Allocator.Temp);
        NativeList<(Entity entity, float settleSpeed, float segmentLength, float weight)> jointParts =
            new NativeList<(Entity, float, float, float)>(Allocator.Temp);

        NativeArray<Entity> rootEntities = ragdollRootQuery.ToEntityArray(Allocator.Temp);
        for (int rootIndex = 0; rootIndex < rootEntities.Length; rootIndex++)
        {
            Ragdoll2DConfig config = entityManager.GetComponentData<Ragdoll2DConfig>(rootEntities[rootIndex]);
            Entity visualRoot = config.visualRoot;
            if (visualRoot != Entity.Null && !entityManager.HasComponent<Ragdoll2D>(visualRoot))
                visualRoots.Add(visualRoot);
        }
        rootEntities.Dispose();

        NativeArray<Entity> partEntities = bodyPartQuery.ToEntityArray(Allocator.Temp);
        for (int partIndex = 0; partIndex < partEntities.Length; partIndex++)
        {
            Entity partEntity = partEntities[partIndex];
            BodyPartInfo info = entityManager.GetComponentData<BodyPartInfo>(partEntity);
            if ((info.flags & BodyPartFlags.RagdollJoint) == 0) continue;
            if (entityManager.HasComponent<Ragdoll2DJoint>(partEntity)) continue;

            // Joint physics resolved at authoring bake (RagdollJointAuthoring → RagdollJointBakeData);
            // built-in defaults only when the joint empty is missing its authoring.
            float settleSpeed   = 8f;
            float segmentLength = 0.5f;
            float weight        = 1f;
            if (entityManager.HasComponent<RagdollJointBakeData>(partEntity))
            {
                RagdollJointBakeData bakeData = entityManager.GetComponentData<RagdollJointBakeData>(partEntity);
                if (bakeData.settleSpeed > 0f)   settleSpeed   = bakeData.settleSpeed;
                if (bakeData.segmentLength > 0f) segmentLength = bakeData.segmentLength;
                if (bakeData.weight > 0f)        weight        = bakeData.weight;
            }
            jointParts.Add((partEntity, settleSpeed, segmentLength, weight));
        }
        partEntities.Dispose();

        for (int index = 0; index < visualRoots.Length; index++)
        {
            entityManager.AddComponentData(visualRoots[index], new Ragdoll2D
            {
                bodyZAngle      = 0f,
                initialRotation = quaternion.identity,
            });
            entityManager.SetComponentEnabled<Ragdoll2D>(visualRoots[index], false);
        }

        for (int index = 0; index < jointParts.Length; index++)
        {
            entityManager.AddComponentData(jointParts[index].entity, new Ragdoll2DJoint
            {
                settleSpeed          = jointParts[index].settleSpeed,
                segmentLength        = jointParts[index].segmentLength,
                weight               = jointParts[index].weight,
                targetAngle          = 0f,
                currentZAngle        = 0f,
                angularVelocity      = 0f,
                initialLocalRotation = quaternion.identity,
            });
            entityManager.SetComponentEnabled<Ragdoll2DJoint>(jointParts[index].entity, false);
        }

        visualRoots.Dispose();
        jointParts.Dispose();
    }
}
