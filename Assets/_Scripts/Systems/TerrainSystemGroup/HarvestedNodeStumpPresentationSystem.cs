using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// A cut tree swaps visuals: each child render part is shown or collapsed to near-zero scale depending on whether its node is a stump.
[BurstCompile]
[UpdateInGroup(typeof(TerrainSystemGroup))]
[UpdateAfter(typeof(HarvestedNodeRestoreSystem))]
public partial struct HarvestedNodeStumpPresentationSystem : ISystem
{
    private const float HiddenScale = 0.001f;

    private ComponentLookup<HarvestedNodeStump> harvestedNodeStumpLookup;

    private EntityQuery visualPartsMissingRestingMatrixQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<HarvestedNodeVisualPart>();
        harvestedNodeStumpLookup = state.GetComponentLookup<HarvestedNodeStump>(true);
        visualPartsMissingRestingMatrixQuery = SystemAPI.QueryBuilder()
            .WithAll<HarvestedNodeVisualPart, LocalToWorld>()
            .WithNone<HarvestedNodeVisualPartRestingMatrix>()
            .Build();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!visualPartsMissingRestingMatrixQuery.IsEmptyIgnoreFilter)
        {
            NativeArray<Entity> newParts = visualPartsMissingRestingMatrixQuery.ToEntityArray(Allocator.Temp);
            state.EntityManager.AddComponent<HarvestedNodeVisualPartRestingMatrix>(visualPartsMissingRestingMatrixQuery);
            for (int partIndex = 0; partIndex < newParts.Length; partIndex++)
            {
                Entity part = newParts[partIndex];
                state.EntityManager.SetComponentData(part, new HarvestedNodeVisualPartRestingMatrix
                {
                    localToWorld = state.EntityManager.GetComponentData<LocalToWorld>(part).Value
                });
            }
            newParts.Dispose();
        }

        harvestedNodeStumpLookup.Update(ref state);
        state.Dependency = new ApplyVisualPartVisibilityJob
        {
            harvestedNodeStumpLookup = harvestedNodeStumpLookup
        }.Schedule(state.Dependency);
    }

    [BurstCompile]
    private partial struct ApplyVisualPartVisibilityJob : IJobEntity
    {
        [ReadOnly] public ComponentLookup<HarvestedNodeStump> harvestedNodeStumpLookup;

        // These are static tile entities, so LocalToWorld is written directly; a dynamic part would have it overwritten by the transform system.
        private void Execute(ref LocalToWorld localToWorld, in HarvestedNodeVisualPartRestingMatrix restingMatrix, in HarvestedNodeVisualPart visualPart)
        {
            if (!harvestedNodeStumpLookup.HasComponent(visualPart.node))
            {
                return;
            }

            bool nodeIsStump = harvestedNodeStumpLookup.IsComponentEnabled(visualPart.node);
            bool visible = nodeIsStump == (visualPart.showWhenStump != 0);
            float4x4 target = visible
                ? restingMatrix.localToWorld
                : math.mul(restingMatrix.localToWorld, float4x4.Scale(HiddenScale));
            if (math.abs(localToWorld.Value.c0.x - target.c0.x) <= 1e-6f)
            {
                return;
            }

            localToWorld.Value = target;
        }
    }
}
