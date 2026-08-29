using DotsAnimationToolkit;
using DotsMovementToolkit;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Derives facing from movement (or the to-target direction while attacking) and pushes it onto
// every part's PartFacing. Must resolve before UnitAnimationAssignmentSystem so the same-frame clip
// pick sees the new facing. See DirectionFacing_System.md §5.
[BurstCompile]
[UpdateInGroup(typeof(AnimationAssignmentSystemGroup), OrderFirst = true)]
[UpdateBefore(typeof(UnitAnimationAssignmentSystem))]
public partial struct UnitFacingSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<UnitDataLibrary>();
        state.RequireForUpdate<PartLibrary>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        BlobAssetReference<UnitLibraryBlob> unitLibrary = SystemAPI.GetSingleton<UnitDataLibrary>().library;
        BlobAssetReference<PartLibraryBlob> partLibrary = SystemAPI.GetSingleton<PartLibrary>().library;

        new UnitFacingJob
        {
            unitLibrary = unitLibrary,
            partLibrary = partLibrary,
            combatTargetLookup = SystemAPI.GetComponentLookup<CombatTarget>(true),
            transformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true),
            partFacingLookup = SystemAPI.GetComponentLookup<PartFacing>(),
        }.ScheduleParallel();
    }
}

[BurstCompile]
public partial struct UnitFacingJob : IJobEntity
{
    [ReadOnly] public BlobAssetReference<UnitLibraryBlob> unitLibrary;
    [ReadOnly] public BlobAssetReference<PartLibraryBlob> partLibrary;
    [ReadOnly] public ComponentLookup<CombatTarget> combatTargetLookup;
    [ReadOnly] public ComponentLookup<LocalTransform> transformLookup;

    // Every unit's body parts are its own — no two units ever write the same part entity, so this
    // is safe across parallel workers despite the lookup spanning the whole world.
    [NativeDisableParallelForRestriction] public ComponentLookup<PartFacing> partFacingLookup;

    public void Execute(
        Entity unitEntity,
        ref UnitFacing unitFacing,
        in UnitData unitData,
        in Movement movement,
        in UnitAction unitAction,
        in LocalTransform localTransform,
        in DynamicBuffer<BodyPart> bodyParts)
    {
        int unitIndex = unitLibrary.Value.FindByUnitType(unitData.unitType);
        if (unitIndex < 0)
            return;

        ref UnitDataBlob unitBlob = ref unitLibrary.Value.units[unitIndex];

        bool hasAimOverride = TryGetAimDirection(unitEntity, ref unitBlob, unitAction.current,
            localTransform.Position, out float2 aimDirectionXY);

        float2 movementXY = hasAimOverride
            ? aimDirectionXY
            : WorldToFacingSpace(movement.targetPosition - localTransform.Position);

        Direction desiredFacing = FacingResolver.FromMovement(
            in movementXY, unitBlob.animationDirections, unitFacing.current);

        if (desiredFacing == unitFacing.current)
            return;

        unitFacing.current = desiredFacing;

        FacingResolver.ResolveClipFacing(
            desiredFacing, unitBlob.animationDirections, out Direction clipFacing, out bool mirrorX);

        for (int partIndex = 0; partIndex < bodyParts.Length; partIndex++)
        {
            Entity partEntity = bodyParts[partIndex].entity;
            if (!partFacingLookup.HasComponent(partEntity))
                continue;

            int viewOffset = 0;
            UnitPartId unitPartId = bodyParts[partIndex].unitPart;
            if (unitPartId != UnitPartId.None && (int)unitPartId < partLibrary.Value.parts.Length)
            {
                viewOffset = partLibrary.Value.parts[(int)unitPartId].GetViewOffset(clipFacing);
            }

            partFacingLookup[partEntity] = new PartFacing { viewOffset = viewOffset, mirrorX = mirrorX };
        }
    }

    // World-fixed velocity.xz mapped straight onto facing space (+x east, +y away from camera),
    // stamped in DirectionFacing_System.md §2 — revisit only if the Cinemachine rig ever yaws.
    // Public (not private) so FacingSpaceTests can pin the mapping directly.
    public static float2 WorldToFacingSpace(float3 worldXZ)
    {
        return new float2(worldXZ.x, worldXZ.z);
    }

    // While unitAction.current is an attack and a live CombatTarget exists, the unit should face its
    // target rather than its movement — the same seam a future talking-partner facing will reuse.
    private bool TryGetAimDirection(
        Entity unitEntity,
        ref UnitDataBlob unitBlob,
        ActionType currentAction,
        float3 selfPosition,
        out float2 aimDirectionXY)
    {
        aimDirectionXY = default;

        if (!combatTargetLookup.HasComponent(unitEntity) || !combatTargetLookup.IsComponentEnabled(unitEntity))
            return false;

        if (AIUtils.GetAttackByAction(ref unitBlob, currentAction) == DamageSource.None)
            return false;

        Entity targetEntity = combatTargetLookup[unitEntity].entity;
        if (!transformLookup.HasComponent(targetEntity))
            return false;

        float3 toTarget = transformLookup[targetEntity].Position - selfPosition;
        aimDirectionXY = WorldToFacingSpace(toTarget);
        return true;
    }
}
