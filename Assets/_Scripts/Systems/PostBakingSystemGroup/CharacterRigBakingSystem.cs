using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

// Cross-entity baking for the character rig. Replaces CharacterBodyPartBakingSystem (BodyPart buffer
// assembly) and Ragdoll2DBakingSystem (ragdoll component stamping) in one pass. Runs after
// PartLibraryBakingSystem so ragdoll settle speeds can be resolved from the blob.
//
//   1. Buffer assembly (subscene / non-prefab): clear every BodyPart buffer, then for each BodyPartInfo
//      child map it to its root via BaseParent and append a BodyPart entry. Prefab-instantiated units
//      rebuild their buffer at runtime in BodyPartInitSystem (entity refs aren't reliably remapped).
//   2. Ragdoll stamp (INCLUDING prefabs): add Ragdoll2D (disabled) to each root's visual child and
//      Ragdoll2DJoint (disabled) to each RagdollJoint-flagged part, settle speed = per-placement
//      override (RagdollJointBakeData) when > 0, else the PartLibrary blob default. Component existence
//      must be baked onto prefab children so Instantiate carries it; enabled bits reset at spawn/death.
[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
[UpdateInGroup(typeof(PostBakingSystemGroup))]
[UpdateAfter(typeof(PartLibraryBakingSystem))]
public partial struct CharacterRigBakingSystem : ISystem
{
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
                partDef = info.ValueRO.partDef,
                flags   = info.ValueRO.flags,
            });
        }

        // --- Resolve the part library blob (for ragdoll settle-speed defaults) ---
        BlobAssetReference<PartLibraryBlob> partBlob = default;
        foreach (RefRO<PartLibrary> library in SystemAPI.Query<RefRO<PartLibrary>>())
        {
            if (library.ValueRO.library.IsCreated)
            {
                partBlob = library.ValueRO.library;
                break;
            }
        }

        // --- 2. Ragdoll stamp (INCLUDING prefabs) ---
        NativeList<Entity> visualRoots = new NativeList<Entity>(Allocator.Temp);
        NativeList<(Entity entity, float settleSpeed, float segmentLength, float weight)> jointParts =
            new NativeList<(Entity, float, float, float)>(Allocator.Temp);

        EntityQuery rootQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<Ragdoll2DConfig>()
            .WithOptions(EntityQueryOptions.IncludePrefab)
            .Build(ref state);
        NativeArray<Entity> rootEntities = rootQuery.ToEntityArray(Allocator.Temp);
        for (int rootIndex = 0; rootIndex < rootEntities.Length; rootIndex++)
        {
            Ragdoll2DConfig config = entityManager.GetComponentData<Ragdoll2DConfig>(rootEntities[rootIndex]);
            Entity visualRoot = config.visualRoot;
            if (visualRoot != Entity.Null && !entityManager.HasComponent<Ragdoll2D>(visualRoot))
                visualRoots.Add(visualRoot);
        }
        rootEntities.Dispose();

        EntityQuery jointQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<BodyPartInfo>()
            .WithOptions(EntityQueryOptions.IncludePrefab)
            .Build(ref state);
        NativeArray<Entity> partEntities = jointQuery.ToEntityArray(Allocator.Temp);
        for (int partIndex = 0; partIndex < partEntities.Length; partIndex++)
        {
            Entity partEntity = partEntities[partIndex];
            BodyPartInfo info = entityManager.GetComponentData<BodyPartInfo>(partEntity);
            if ((info.flags & BodyPartFlags.RagdollJoint) == 0) continue;
            if (entityManager.HasComponent<Ragdoll2DJoint>(partEntity)) continue;

            float settleSpeed   = ResolveSettleSpeed(entityManager, partEntity, info.partDef, partBlob);
            float segmentLength = 0.5f;
            float weight        = 1f;
            if (partBlob.IsCreated)
            {
                int defIndex = (int)info.partDef;
                if (defIndex >= 0 && defIndex < partBlob.Value.parts.Length)
                {
                    segmentLength = partBlob.Value.parts[defIndex].ragdollSegmentLength;
                    weight        = partBlob.Value.parts[defIndex].ragdollWeight;
                }
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

    private static float ResolveSettleSpeed(
        EntityManager entityManager,
        Entity partEntity,
        PartDefId partDef,
        BlobAssetReference<PartLibraryBlob> partBlob)
    {
        if (entityManager.HasComponent<RagdollJointBakeData>(partEntity))
        {
            float overrideSpeed = entityManager.GetComponentData<RagdollJointBakeData>(partEntity).settleSpeedOverride;
            if (overrideSpeed > 0f) return overrideSpeed;
        }

        if (partBlob.IsCreated)
        {
            int defIndex = (int)partDef;
            if (defIndex >= 0 && defIndex < partBlob.Value.parts.Length)
                return partBlob.Value.parts[defIndex].defaultSettleSpeed;
        }

        return 8f;
    }
}
