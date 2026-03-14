
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(EditorAnimationSystem))]
public partial struct EditorApplyAnimatedPoseSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<AnimationEditorActive>();
    }

    public void OnUpdate(ref SystemState state)
    {
        // Apply image index to the material property
        foreach (var (animatedPose, imageIndex, imageIndexOverride) in 
                 SystemAPI.Query<RefRO<AnimationTargetPose>, RefRW<ImageIndex>, RefRW<ImageIndexOverride>>())
        {
            imageIndex.ValueRW.index = animatedPose.ValueRO.imageIndex;
            imageIndex.ValueRW.onUpdate = true;
            imageIndexOverride.ValueRW.Value = animatedPose.ValueRO.imageIndex;
        }
    }
}