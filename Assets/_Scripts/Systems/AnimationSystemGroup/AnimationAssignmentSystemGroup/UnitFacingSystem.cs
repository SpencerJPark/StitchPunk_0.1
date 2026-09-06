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
            cutsceneFacingLookup = SystemAPI.GetComponentLookup<CutsceneFacing>(true),
            transformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true),
            partFacingLookup = SystemAPI.GetComponentLookup<PartFacing>(),
        }.ScheduleParallel();
    }
}

[BurstCompile]
// Cutscene actors are in the query, not excluded from it (G2 §3.4): while a cutscene puppets an
// actor its Movement.targetPosition is stale, so the facing either comes from the cutscene's own
// CutsceneFacing or is left exactly as it was when the cutscene started.
[WithPresent(typeof(CutsceneActor))]
public partial struct UnitFacingJob : IJobEntity
{
    [ReadOnly] public BlobAssetReference<UnitLibraryBlob> unitLibrary;
    [ReadOnly] public BlobAssetReference<PartLibraryBlob> partLibrary;
    [ReadOnly] public ComponentLookup<CombatTarget> combatTargetLookup;
    [ReadOnly] public ComponentLookup<CutsceneFacing> cutsceneFacingLookup;
    [ReadOnly] public ComponentLookup<LocalTransform> transformLookup;

    // Every unit's body parts are its own — no two units ever write the same part entity, so this
    // is safe across parallel workers despite the lookup spanning the whole world.
    [NativeDisableParallelForRestriction] public ComponentLookup<PartFacing> partFacingLookup;

    public void Execute(
        Entity unitEntity,
        ref UnitFacing unitFacing,
        EnabledRefRO<CutsceneActor> cutsceneActorEnabled,
        in UnitData unitData,
        in Movement movement,
        in UnitAction unitAction,
        in LocalTransform localTransform,
        in DynamicBuffer<BodyPart> bodyParts)
    {
        bool hasCutsceneFacing = cutsceneFacingLookup.HasComponent(unitEntity)
            && cutsceneFacingLookup.IsComponentEnabled(unitEntity);

        // A cutscene actor the cutscene has no facing answer for keeps the one it had: deriving it
        // from Movement.targetPosition would read a target nothing is walking toward any more.
        if (cutsceneActorEnabled.ValueRO && !hasCutsceneFacing)
            return;

        int unitIndex = unitLibrary.Value.FindByUnitType(unitData.unitType);
        if (unitIndex < 0)
            return;

        ref UnitDataBlob unitBlob = ref unitLibrary.Value.units[unitIndex];

        float2 aimDirectionXY = float2.zero;
        bool hasAimOverride = !hasCutsceneFacing
            && TryGetAimDirection(unitEntity, ref unitBlob, unitAction.current,
                localTransform.Position, out aimDirectionXY);

        float2 movementXY = ResolveMovementXY(
            hasCutsceneFacing,
            hasCutsceneFacing ? cutsceneFacingLookup[unitEntity].angleDegrees : 0f,
            hasAimOverride,
            in aimDirectionXY,
            movement.targetPosition - localTransform.Position);

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

    // The precedence a facing is decided by: a cutscene's own answer, then an attack's aim, then
    // where the unit is walking. Public (not private) so FacingSpaceTests can pin it directly.
    public static float2 ResolveMovementXY(
        bool hasCutsceneFacing,
        float cutsceneAngleDegrees,
        bool hasAimOverride,
        in float2 aimDirectionXY,
        float3 movementDelta)
    {
        if (hasCutsceneFacing)
            return CutsceneAngleToFacingSpace(cutsceneAngleDegrees);

        return hasAimOverride ? aimDirectionXY : WorldToFacingSpace(movementDelta);
    }

    // CutsceneFacing.angleDegrees is measured FROM +X TOWARD +Z — 0 east, 90 north — which is the
    // same space FacingResolver reads, so (cos, sin) lands in it directly. It is NOT a LocalTransform
    // Y euler, which measures from +Z: the two are a reflection about 45 degrees, and mixing them
    // turns an actor walking east into one facing north (toolkit A65's own bug, Gotchas.md).
    public static float2 CutsceneAngleToFacingSpace(float angleDegrees)
    {
        float angleRadians = math.radians(angleDegrees);
        return new float2(math.cos(angleRadians), math.sin(angleRadians));
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
