using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Swell + white flash while a unit winds up a ranged shot. Writes only the Visual child's Scale (billboard owns its Rotation) and BodyPartFlash.
[UpdateInGroup(typeof(AnimationSystemGroup))]
[UpdateAfter(typeof(VisualBillboardSystem))]
[BurstCompile]
public partial struct WindUpPresentationSystem : ISystem
{
    public const float WIND_UP_FLASH_MAX = 0.55f;
    public const float WIND_UP_SCALE_MAX = 1.10f;

    private ComponentLookup<BodyPartFlash> bodyPartFlashLookup;
    private ComponentLookup<LocalTransform> localTransformLookup;
    private ComponentLookup<Parent> parentLookup;
    private ComponentLookup<RangedAim> rangedAimLookup;

    public static float WindUpEasedProgress(in RangedAim rangedAim)
    {
        float progress = rangedAim.windUpTotal > 0f
            ? math.saturate(1f - rangedAim.windUpRemaining / rangedAim.windUpTotal)
            : 1f;
        return math.smoothstep(0f, 1f, progress);
    }

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        bodyPartFlashLookup = state.GetComponentLookup<BodyPartFlash>();
        localTransformLookup = state.GetComponentLookup<LocalTransform>();
        parentLookup = state.GetComponentLookup<Parent>(true);
        rangedAimLookup = state.GetComponentLookup<RangedAim>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float4 flashColor = new float4(1f, 1f, 1f, 1f);
        float flashMaximum = WIND_UP_FLASH_MAX;
        float scaleMaximum = WIND_UP_SCALE_MAX;
        if (SystemAPI.TryGetSingleton<WindUpPresentationConfig>(out WindUpPresentationConfig config))
        {
            flashColor = config.flashColor;
            flashMaximum = config.flashMaximum;
            scaleMaximum = config.scaleMaximum;
        }

        bodyPartFlashLookup.Update(ref state);
        localTransformLookup.Update(ref state);
        parentLookup.Update(ref state);
        rangedAimLookup.Update(ref state);

        WindUpFlashJob flashJob = new WindUpFlashJob
        {
            flashColor = flashColor,
            flashMaximum = flashMaximum,
            bodyPartFlashLookup = bodyPartFlashLookup
        };
        state.Dependency = flashJob.ScheduleParallel(state.Dependency);

        WindUpSwellJob swellJob = new WindUpSwellJob
        {
            scaleMaximum = scaleMaximum,
            localTransformLookup = localTransformLookup,
            parentLookup = parentLookup,
            rangedAimLookup = rangedAimLookup
        };
        state.Dependency = swellJob.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithPresent(typeof(RangedAim))]
public partial struct WindUpFlashJob : IJobEntity
{
    public float4 flashColor;
    public float flashMaximum;

    // Each unit root only writes its own body-part children.
    [NativeDisableParallelForRestriction] public ComponentLookup<BodyPartFlash> bodyPartFlashLookup;

    private void Execute(in DynamicBuffer<BodyPart> bodyParts, in RangedAim rangedAim, EnabledRefRO<RangedAim> rangedAimEnabled)
    {
        float alpha = rangedAimEnabled.ValueRO
            ? WindUpPresentationSystem.WindUpEasedProgress(in rangedAim) * flashMaximum
            : 0f;
        float4 target = new float4(flashColor.xyz, alpha);

        for (int partIndex = 0; partIndex < bodyParts.Length; partIndex++)
        {
            Entity partEntity = bodyParts[partIndex].entity;
            if (!bodyPartFlashLookup.HasComponent(partEntity))
            {
                continue;
            }
            if (math.all(bodyPartFlashLookup[partEntity].Value == target))
            {
                continue;
            }
            bodyPartFlashLookup[partEntity] = new BodyPartFlash { Value = target };
        }
    }
}

[BurstCompile]
[WithAll(typeof(VisualBillboard))]
public partial struct WindUpSwellJob : IJobEntity
{
    public float scaleMaximum;

    [NativeDisableParallelForRestriction] public ComponentLookup<LocalTransform> localTransformLookup;
    [ReadOnly] public ComponentLookup<Parent> parentLookup;
    [ReadOnly] public ComponentLookup<RangedAim> rangedAimLookup;

    private void Execute(Entity entity)
    {
        if (!parentLookup.HasComponent(entity) || !localTransformLookup.HasComponent(entity))
        {
            return;
        }
        Entity parent = parentLookup[entity].Value;

        float targetScale = 1f;
        if (rangedAimLookup.HasComponent(parent) && rangedAimLookup.IsComponentEnabled(parent))
        {
            RangedAim rangedAim = rangedAimLookup[parent];
            targetScale = math.lerp(1f, scaleMaximum, WindUpPresentationSystem.WindUpEasedProgress(in rangedAim));
        }

        if (localTransformLookup[entity].Scale == targetScale)
        {
            return;
        }
        RefRW<LocalTransform> visualTransform = localTransformLookup.GetRefRW(entity);
        visualTransform.ValueRW.Scale = targetScale;
    }
}
