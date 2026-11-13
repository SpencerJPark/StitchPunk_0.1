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

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<HarvestedNodeVisualPart>();
        harvestedNodeStumpLookup = state.GetComponentLookup<HarvestedNodeStump>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
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

        private void Execute(ref PostTransformMatrix postTransformMatrix, in HarvestedNodeVisualPart visualPart)
        {
            if (!harvestedNodeStumpLookup.HasComponent(visualPart.node))
            {
                return;
            }

            bool nodeIsStump = harvestedNodeStumpLookup.IsComponentEnabled(visualPart.node);
            bool visible = nodeIsStump == (visualPart.showWhenStump != 0);
            float targetScale = visible ? 1f : HiddenScale;
            if (postTransformMatrix.Value.c0.x == targetScale)
            {
                return;
            }

            postTransformMatrix.Value = visible ? float4x4.identity : float4x4.Scale(HiddenScale);
        }
    }
}
