using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

[UpdateInGroup(typeof(DespawnSystemGroup))]
[UpdateAfter(typeof(UnitPoolReturnSystem))]
public partial struct DespawnSystem : ISystem
{
    // A per-UnitType blob replaces this when two unit types need different caps.
    private const int PoolCapPerType = 64;

    private EntityQuery despawnRequestQuery;
    private EntityQuery dormantPoolQuery;
    private ComponentLookup<PoolOwner> poolOwnerLookup;

    public void OnCreate(ref SystemState state)
    {
        despawnRequestQuery = new EntityQueryBuilder(Allocator.Temp).WithAll<Despawn>().Build(ref state);
        dormantPoolQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<PoolOwner, Disabled>()
            .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
            .Build(ref state);
        poolOwnerLookup = state.GetComponentLookup<PoolOwner>(true);
    }

    public void OnUpdate(ref SystemState state)
    {
        // WithAll<Despawn>() (no IncludeDisabled) honours the enabled bit, so this count is exactly
        // the Despawn-enabled set — the ParallelWriter lists below cannot grow, so it is their capacity.
        int despawnRequestCount = despawnRequestQuery.CalculateEntityCount();

        NativeList<PoolReturnCandidate> poolReturnCandidates = new NativeList<PoolReturnCandidate>(despawnRequestCount, Allocator.TempJob);
        NativeList<Entity> destroyCandidates = new NativeList<Entity>(despawnRequestCount, Allocator.TempJob);

        if (despawnRequestCount > 0)
        {
            poolOwnerLookup.Update(ref state);

            GatherDespawnRequestsJob gatherDespawnRequestsJob = new GatherDespawnRequestsJob
            {
                poolOwnerLookup = poolOwnerLookup,
                poolReturnCandidateWriter = poolReturnCandidates.AsParallelWriter(),
                destroyCandidateWriter = destroyCandidates.AsParallelWriter(),
            };
            state.Dependency = gatherDespawnRequestsJob.ScheduleParallel(despawnRequestQuery, state.Dependency);
            state.Dependency.Complete();
        }

        EntityCommandBuffer entityCommandBuffer = new EntityCommandBuffer(Allocator.Temp);
        // Enums do not implement IEquatable, so the map key is the underlying int value.
        NativeHashMap<int, int> keptDormantCountByUnitType = new NativeHashMap<int, int>(8, Allocator.Temp);

        NativeArray<Entity> dormantEntities = dormantPoolQuery.ToEntityArray(Allocator.Temp);
        NativeArray<PoolOwner> dormantPoolOwners = dormantPoolQuery.ToComponentDataArray<PoolOwner>(Allocator.Temp);

        // Units already dormant keep their pool slots first; this frame's returns fill what is left, the rest are destroyed.
        for (int dormantIndex = 0; dormantIndex < dormantEntities.Length; dormantIndex++)
        {
            int unitTypeKey = (int)dormantPoolOwners[dormantIndex].unitType;
            int keptCount = keptDormantCountByUnitType.TryGetValue(unitTypeKey, out int existingKeptCount) ? existingKeptCount : 0;

            if (keptCount < PoolCapPerType)
            {
                keptDormantCountByUnitType[unitTypeKey] = keptCount + 1;
            }
            else
            {
                entityCommandBuffer.DestroyEntity(dormantEntities[dormantIndex]);
            }
        }

        for (int destroyIndex = 0; destroyIndex < destroyCandidates.Length; destroyIndex++)
        {
            entityCommandBuffer.DestroyEntity(destroyCandidates[destroyIndex]);
        }

        for (int poolReturnIndex = 0; poolReturnIndex < poolReturnCandidates.Length; poolReturnIndex++)
        {
            PoolReturnCandidate poolReturnCandidate = poolReturnCandidates[poolReturnIndex];
            int unitTypeKey = (int)poolReturnCandidate.unitType;
            int keptCount = keptDormantCountByUnitType.TryGetValue(unitTypeKey, out int existingKeptCount) ? existingKeptCount : 0;

            if (keptCount < PoolCapPerType)
            {
                // Parent-only Disabled, matching UnitPoolReturnSystem.
                entityCommandBuffer.AddComponent<Disabled>(poolReturnCandidate.entity);
                entityCommandBuffer.SetComponentEnabled<Despawn>(poolReturnCandidate.entity, false);
                keptDormantCountByUnitType[unitTypeKey] = keptCount + 1;
            }
            else
            {
                entityCommandBuffer.DestroyEntity(poolReturnCandidate.entity);
            }
        }

        entityCommandBuffer.Playback(state.EntityManager);
        entityCommandBuffer.Dispose();

        dormantEntities.Dispose();
        dormantPoolOwners.Dispose();
        keptDormantCountByUnitType.Dispose();
        poolReturnCandidates.Dispose();
        destroyCandidates.Dispose();
    }

    public struct PoolReturnCandidate
    {
        public Entity entity;
        public UnitType unitType;
    }

    [BurstCompile]
    public partial struct GatherDespawnRequestsJob : IJobEntity
    {
        [ReadOnly] public ComponentLookup<PoolOwner> poolOwnerLookup;
        public NativeList<PoolReturnCandidate>.ParallelWriter poolReturnCandidateWriter;
        public NativeList<Entity>.ParallelWriter destroyCandidateWriter;

        private void Execute(Entity entity, in Despawn despawn)
        {
            if (despawn.mode != DespawnMode.ForceDestroy && poolOwnerLookup.TryGetComponent(entity, out PoolOwner poolOwner))
            {
                poolReturnCandidateWriter.AddNoResize(new PoolReturnCandidate
                {
                    entity = entity,
                    unitType = poolOwner.unitType,
                });
            }
            else
            {
                destroyCandidateWriter.AddNoResize(entity);
            }
        }
    }
}
