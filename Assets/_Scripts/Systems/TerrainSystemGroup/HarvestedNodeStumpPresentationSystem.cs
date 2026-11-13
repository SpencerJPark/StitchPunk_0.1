using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// A cut tree reads as a stump: the root's PostTransformMatrix flattens Y. Children inherit it because the parent's LocalToWorld (post transform included) feeds theirs.
[BurstCompile]
[UpdateInGroup(typeof(TerrainSystemGroup))]
[UpdateAfter(typeof(HarvestedNodeRestoreSystem))]
public partial struct HarvestedNodeStumpPresentationSystem : ISystem
{
    private const float StumpHeightScale = 0.15f;

    private EntityQuery nodesMissingPostTransformMatrixQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        nodesMissingPostTransformMatrixQuery = SystemAPI.QueryBuilder()
            .WithAll<HarvestableNodeKey>()
            .WithNone<PostTransformMatrix>()
            .Build();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!nodesMissingPostTransformMatrixQuery.IsEmptyIgnoreFilter)
        {
            // Added zeroed; the job below writes the real matrix this same update.
            state.EntityManager.AddComponent<PostTransformMatrix>(nodesMissingPostTransformMatrixQuery);
        }

        state.Dependency = new ApplyStumpScaleJob().Schedule(state.Dependency);
    }

    [BurstCompile]
    [WithPresent(typeof(HarvestedNodeStump))]
    private partial struct ApplyStumpScaleJob : IJobEntity
    {
        private void Execute(EnabledRefRO<HarvestedNodeStump> harvestedNodeStumpEnabled,
            ref PostTransformMatrix postTransformMatrix, in HarvestableNodeKey nodeKey)
        {
            float heightScale = harvestedNodeStumpEnabled.ValueRO ? StumpHeightScale : 1f;
            postTransformMatrix.Value = float4x4.Scale(1f, heightScale, 1f);
        }
    }
}
