using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Flips the CameraVisible enableable tag on character rig roots + their BodyPart children from the
// CameraView singleton (camera ground center + view radius, written by AudioManager each LateUpdate,
// so it is one frame stale — the hysteresis padding absorbs that). World service: presentation
// systems downstream chunk-filter on the tag; UnitSpawnerSystem reads CameraView directly to place
// spawns out of view. PRESENTATION ONLY — simulation systems must never gate on CameraVisible.
[BurstCompile]
[UpdateInGroup(typeof(GameManagerSystemGroup))]
public partial struct CameraVisibilitySystem : ISystem
{
    // Hysteresis band beyond the (screen-circumscribing) view radius: entities become visible
    // inside radius + ENABLE, and only go invisible again outside radius + DISABLE. The gap
    // prevents edge-of-screen flicker and covers the one-frame-stale CameraView.
    private const float ENABLE_PADDING = 5f;
    private const float DISABLE_PADDING = 10f;

    private ComponentLookup<CameraVisible> partVisibleLookup;
    private ComponentLookup<Prefab> prefabLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<CameraView>();

        partVisibleLookup = state.GetComponentLookup<CameraVisible>(false);
        prefabLookup = state.GetComponentLookup<Prefab>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        CameraView cameraView = SystemAPI.GetSingleton<CameraView>();

        partVisibleLookup.Update(ref state);
        prefabLookup.Update(ref state);

        float enableRadius = cameraView.viewRadius + ENABLE_PADDING;
        float disableRadius = cameraView.viewRadius + DISABLE_PADDING;

        state.Dependency = new CameraVisibilityJob
        {
            partVisibleLookup = partVisibleLookup,
            prefabLookup = prefabLookup,
            viewCenter = cameraView.center,
            enableRadiusSq = enableRadius * enableRadius,
            disableRadiusSq = disableRadius * disableRadius,
        }.ScheduleParallel(state.Dependency);
    }
}

// Iterates rig roots (BodyPart buffer + CameraVisible). IgnoreComponentEnabledState so disabled
// (off-screen) roots are still iterated and can be re-enabled. Root → parts is a unique pairing
// (each part belongs to exactly one root), so parallel part writes are safe.
[BurstCompile]
[WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)]
public partial struct CameraVisibilityJob : IJobEntity
{
    [NativeDisableParallelForRestriction] public ComponentLookup<CameraVisible> partVisibleLookup;
    [ReadOnly] public ComponentLookup<Prefab> prefabLookup;

    public float3 viewCenter;
    public float enableRadiusSq;
    public float disableRadiusSq;

    public void Execute(
        in LocalTransform rootTransform,
        in DynamicBuffer<BodyPart> bodyParts,
        EnabledRefRW<CameraVisible> cameraVisibleEnabled)
    {
        float2 offsetFromCamera = rootTransform.Position.xz - viewCenter.xz;
        float distanceSq = math.lengthsq(offsetFromCamera);

        bool currentlyVisible = cameraVisibleEnabled.ValueRO;
        bool shouldBeVisible = currentlyVisible
            ? distanceSq <= disableRadiusSq
            : distanceSq <= enableRadiusSq;

        if (shouldBeVisible != currentlyVisible)
            cameraVisibleEnabled.ValueRW = shouldBeVisible;

        // Propagate to parts when the root transitioned, or when parts drifted out of sync — on the
        // spawn frame the BodyPart buffer still holds the PREFAB's part entities (refs are not
        // remapped until BodyPartInitSystem rebuilds it), so a transition can be missed there.
        bool needsPropagate = shouldBeVisible != currentlyVisible;

        if (!needsPropagate)
        {
            for (int partIndex = 0; partIndex < bodyParts.Length; partIndex++)
            {
                Entity partEntity = bodyParts[partIndex].entity;
                if (!partVisibleLookup.HasComponent(partEntity) || prefabLookup.HasComponent(partEntity))
                    continue;

                needsPropagate = partVisibleLookup.IsComponentEnabled(partEntity) != shouldBeVisible;
                break;
            }
        }

        if (!needsPropagate)
            return;

        for (int partIndex = 0; partIndex < bodyParts.Length; partIndex++)
        {
            Entity partEntity = bodyParts[partIndex].entity;

            // Prefab guard: stale spawn-frame refs point at the source prefab's parts — writing
            // through them would permanently corrupt the prefab's baked enable state.
            if (!partVisibleLookup.HasComponent(partEntity) || prefabLookup.HasComponent(partEntity))
                continue;

            partVisibleLookup.SetComponentEnabled(partEntity, shouldBeVisible);
        }
    }
}
