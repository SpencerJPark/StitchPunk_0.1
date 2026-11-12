using DotsAnimationToolkit;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Flips CameraVisible on rig roots and their BodyPart children from the CameraView singleton, which
// AudioManager writes each LateUpdate (one frame stale; the padding absorbs that).
// PRESENTATION ONLY: simulation systems must never gate on CameraVisible.
[BurstCompile]
[UpdateInGroup(typeof(GameManagerSystemGroup))]
public partial struct CameraVisibilitySystem : ISystem
{
    // Hysteresis: visible inside radius + ENABLE, invisible only outside radius + DISABLE (stops edge flicker).
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

        // Toolkit actors also carry AnimVisible; mirror the root's CameraVisible onto it so there is one authority.
        state.Dependency = new AnimVisibleMirrorJob().ScheduleParallel(state.Dependency);
    }
}

// Must run after CameraVisibilityJob so it reads this frame's result.
[BurstCompile]
[WithAll(typeof(AnimVisible))]
public partial struct AnimVisibleMirrorJob : IJobEntity
{
    public void Execute(EnabledRefRO<CameraVisible> cameraVisibleEnabled, EnabledRefRW<AnimVisible> animVisibleEnabled)
    {
        if (animVisibleEnabled.ValueRO != cameraVisibleEnabled.ValueRO)
            animVisibleEnabled.ValueRW = cameraVisibleEnabled.ValueRO;
    }
}

// IgnoreComponentEnabledState so off-screen roots are still iterated and can be re-enabled. Each part
// belongs to one root, so parallel part writes are safe.
// CameraVisible is deliberately not an Execute parameter: query access to a type also held by a
// writable ComponentLookup of the same type throws an aliasing error at schedule time, RO or RW.
[BurstCompile]
[WithAll(typeof(CameraVisible))]
[WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)]
public partial struct CameraVisibilityJob : IJobEntity
{
    [NativeDisableParallelForRestriction] public ComponentLookup<CameraVisible> partVisibleLookup;
    [ReadOnly] public ComponentLookup<Prefab> prefabLookup;

    public float3 viewCenter;
    public float enableRadiusSq;
    public float disableRadiusSq;

    public void Execute(
        Entity rootEntity,
        in LocalTransform rootTransform,
        in DynamicBuffer<BodyPart> bodyParts)
    {
        float2 offsetFromCamera = rootTransform.Position.xz - viewCenter.xz;
        float distanceSq = math.lengthsq(offsetFromCamera);

        bool currentlyVisible = partVisibleLookup.IsComponentEnabled(rootEntity);
        bool shouldBeVisible = currentlyVisible
            ? distanceSq <= disableRadiusSq
            : distanceSq <= enableRadiusSq;

        if (shouldBeVisible != currentlyVisible)
            partVisibleLookup.SetComponentEnabled(rootEntity, shouldBeVisible);

        // Also resync drifted parts: on the spawn frame the buffer still holds the PREFAB's part
        // entities (until BodyPartInitSystem rebuilds it), so a transition can be missed.
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

            // Prefab guard: stale spawn-frame refs would corrupt the prefab's baked enable state.
            if (!partVisibleLookup.HasComponent(partEntity) || prefabLookup.HasComponent(partEntity))
                continue;

            partVisibleLookup.SetComponentEnabled(partEntity, shouldBeVisible);
        }
    }
}
