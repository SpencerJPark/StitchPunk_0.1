using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

// Runtime re-skin consumer. Reads ChangeDesignRequest (enabled-only), upserts each explicit
// (target, imageIndex) change into PersistedDesign so the new look persists, fans it to the child
// quads, then disables the request (one-shot). Lives in DesignSystemGroup (after HealthSystemGroup,
// before AnimationSystemGroup) so a conversion-fired re-skin lands before UpdateImageIndexSystem.
// Writes child entities via ComponentLookup on the main thread — never .Run().
[BurstCompile]
[UpdateInGroup(typeof(DesignSystemGroup))]
public partial struct DesignChangeSystem : ISystem
{
    private ComponentLookup<ImageIndex>             _imageIndexLookup;
    private ComponentLookup<AnimationTargetRestPose> _restPoseLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        _imageIndexLookup = state.GetComponentLookup<ImageIndex>(false);
        _restPoseLookup   = state.GetComponentLookup<AnimationTargetRestPose>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        _imageIndexLookup.Update(ref state);
        _restPoseLookup.Update(ref state);

        // Main-thread ComponentLookup writes below conflict with UpdateImageIndexJob (reads ImageIndex)
        // still in flight from a prior frame — complete the input dependency before writing.
        state.CompleteDependency();

        foreach (var (persistedDesign, changeRequest, targets, entity) in
            SystemAPI.Query<RefRW<PersistedDesign>, RefRO<ChangeDesignRequest>, DynamicBuffer<AnimatorTarget>>()
                .WithEntityAccess())
        {
            FixedList512Bytes<DesignSlot> changes = changeRequest.ValueRO.changes;
            for (int i = 0; i < changes.Length; i++)
            {
                int target     = changes[i].target;
                int imageIndex = changes[i].imageIndex;

                DesignApplyUtil.UpsertSlot(ref persistedDesign.ValueRW.slots, target, imageIndex);
                DesignApplyUtil.ApplySlot(
                    targets, target, imageIndex,
                    ref _imageIndexLookup, ref _restPoseLookup);
            }

            SystemAPI.SetComponentEnabled<ChangeDesignRequest>(entity, false);
        }
    }
}
