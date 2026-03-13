using Unity.Entities;
using Unity.Burst;
using Unity.Collections;

[BurstCompile]
[UpdateInGroup(typeof(AnimationAssignmentSystemGroup))]
public partial struct UnitAnimationAssignmentSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<UnitLibrary>();
        state.RequireForUpdate<GameSceneTag>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        BlobAssetReference<UnitLibraryBlob> library = SystemAPI.GetSingleton<UnitLibrary>().library;

        new UnitAnimationAssignmentJob
        {
            library = library
        }.ScheduleParallel();
    }
}

[BurstCompile]
public partial struct UnitAnimationAssignmentJob : IJobEntity
{
    [ReadOnly] public BlobAssetReference<UnitLibraryBlob> library;

    public void Execute(
        ref DynamicBuffer<AnimationLayer> layers,
        in UnitData unitData,
        in UnitMover unitMover,
        in UnitAction unitAction)
    {
        if (unitData.libraryIndex < 0 || unitData.libraryIndex >= library.Value.units.Length)
            return;

        ref UnitDataBlob unitBlob = ref library.Value.units[unitData.libraryIndex];

        AnimationType targetAnimation = GetAnimationForAction(
            unitAction.current,
            ref unitBlob,
            unitMover.isMoving);

        if (!AnimationUtils.IsCurrentLayer(ref layers, AnimationLayerType.Base, targetAnimation))
        {
            AnimationUtils.SetLayer(ref layers, AnimationLayerType.Base, targetAnimation);
        }
    }

    private AnimationType GetAnimationForAction(
        ActionType action,
        ref UnitDataBlob unitBlob,
        bool isMoving)
    {
        ref BlobArray<ActionAnimationMappingBlob> mappings = ref unitBlob.actionAnimations;

        for (int i = 0; i < mappings.Length; i++)
        {
            if (mappings[i].action == action)
            {
                return mappings[i].animation;
            }
        }

        return isMoving ? unitBlob.movingAnimation : unitBlob.idleAnimation;
    }
}