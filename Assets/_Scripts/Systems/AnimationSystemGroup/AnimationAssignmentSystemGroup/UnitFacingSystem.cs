using DotsAnimationToolkit;
using DotsMovementToolkit;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Derives facing from movement (or the target direction while attacking) and writes every part's
// PartFacing. Runs before UnitAnimationAssignmentSystem so the same-frame clip pick sees it.
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
            actorFacingLookup = SystemAPI.GetComponentLookup<ActorFacing>(),
            actorProfileLookup = SystemAPI.GetComponentLookup<ActorProfile>(true),
        }.ScheduleParallel();
    }
}

[BurstCompile]
// Cutscene actors stay in the query: their Movement.targetPosition is stale, so facing comes from
// CutsceneFacing or is left as it was.
[WithPresent(typeof(CutsceneActor))]
public partial struct UnitFacingJob : IJobEntity
{
    [ReadOnly] public BlobAssetReference<UnitLibraryBlob> unitLibrary;
    [ReadOnly] public BlobAssetReference<PartLibraryBlob> partLibrary;
    [ReadOnly] public ComponentLookup<CombatTarget> combatTargetLookup;
    [ReadOnly] public ComponentLookup<CutsceneFacing> cutsceneFacingLookup;
    [ReadOnly] public ComponentLookup<LocalTransform> transformLookup;

    // Safe in parallel: no two units share a part entity.
    [NativeDisableParallelForRestriction] public ComponentLookup<PartFacing> partFacingLookup;

    // Safe in parallel: each unit writes only its own ActorFacing.
    [NativeDisableParallelForRestriction] public ComponentLookup<ActorFacing> actorFacingLookup;

    // Turn granularity comes from the baked profile; no profile falls back to Six.
    [ReadOnly] public ComponentLookup<ActorProfile> actorProfileLookup;

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

        // No cutscene facing: keep the old one, since Movement.targetPosition is stale.
        if (cutsceneActorEnabled.ValueRO && !hasCutsceneFacing)
            return;

        int unitIndex = unitLibrary.Value.FindByUnitType(unitData.unitType);
        if (unitIndex < 0)
            return;

        ref UnitDataBlob unitBlob = ref unitLibrary.Value.units[unitIndex];

        AnimationDirections turnDirections = AnimationDirections.Six;
        if (actorProfileLookup.HasComponent(unitEntity))
        {
            BlobAssetReference<ActorProfileBlob> profileBlob = actorProfileLookup[unitEntity].Value;
            if (profileBlob.IsCreated)
                turnDirections = profileBlob.Value.turnDirections;
        }

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
            in movementXY, turnDirections, unitFacing.current);

        // Compared to ActorFacing itself: it bakes as SouthEast while UnitFacing may differ.
        if (actorFacingLookup.HasComponent(unitEntity))
        {
            ActorFacing actorFacing = actorFacingLookup[unitEntity];
            if (actorFacing.facing != desiredFacing)
            {
                actorFacing.facing = desiredFacing;
                actorFacingLookup[unitEntity] = actorFacing;
            }
        }

        if (desiredFacing == unitFacing.current)
            return;

        unitFacing.current = desiredFacing;

        FacingResolver.ResolveClipFacing(
            desiredFacing, turnDirections, out Direction clipFacing, out bool mirrorX);

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

    // Precedence: cutscene, then attack aim, then walking. Public for FacingSpaceTests.
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

    // angleDegrees runs from +X toward +Z (0 east, 90 north), FacingResolver's space. It is NOT a
    // LocalTransform Y euler (from +Z); mixing them turns an east walker into a north facer.
    public static float2 CutsceneAngleToFacingSpace(float angleDegrees)
    {
        float angleRadians = math.radians(angleDegrees);
        return new float2(math.cos(angleRadians), math.sin(angleRadians));
    }

    // World xz maps straight onto facing space (+x east, +y away from camera); revisit if the
    // Cinemachine rig ever yaws. Public for FacingSpaceTests.
    public static float2 WorldToFacingSpace(float3 worldXZ)
    {
        return new float2(worldXZ.x, worldXZ.z);
    }

    // While attacking with a live CombatTarget, face the target instead of the movement direction.
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
