using Unity.Burst;
using Unity.Entities;


[UpdateInGroup(typeof(AnimationSystemGroup))]
partial struct UpdateBaseAnimationLayerSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        new UpdateBaseAnimationLayerJob().ScheduleParallel();
    }
}

[BurstCompile]
public partial struct UpdateBaseAnimationLayerJob : IJobEntity
{
    public void Execute(ref DynamicBuffer<AnimationLayer> layers, in UnitAnimations unitAnimations, in UnitMover unitMover)
    {
        if (unitAnimations.overide != AnimationType.None)
        {
            return;
        }

        AnimationType targetAnimation = unitMover.isMoving ? unitAnimations.move : unitAnimations.idle;
        
        if (!AnimationUtil.IsCurrentLayer(ref layers, AnimationLayerType.Base, targetAnimation))
        {
            AnimationUtil.SetLayer(ref layers, AnimationLayerType.Base, targetAnimation);
        }
    }
}