// =====================================
// EDITOR ANIMATION SYSTEM (Live SO Sampling)
// =====================================

using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(ApplyAnimatedPoseSystem))]
public partial struct EditorAnimationSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<AnimationEditorActive>();
        state.RequireForUpdate<EditorAnimationTimeControl>();
    }

    public void OnUpdate(ref SystemState state)
    {
        try
        {
            Entity timeControlEntity =
                SystemAPI.GetSingletonEntity<EditorAnimationTimeControl>();

            if (!state.EntityManager.HasComponent<EditorAnimationLibraryManaged>(timeControlEntity))
                return;

            EditorAnimationLibraryManaged libraryManaged =
                state.EntityManager.GetComponentObject<EditorAnimationLibraryManaged>(timeControlEntity);

            if (libraryManaged == null || libraryManaged.library == null)
                return;

            EditorAnimationTimeControl timeControl =
                SystemAPI.GetSingleton<EditorAnimationTimeControl>();

            float deltaTime = SystemAPI.Time.DeltaTime;

            int frameRate = GameAssets.Instance != null
                ? GameAssets.Instance.animationFrameRate
                : 24;

            ComponentLookup<AnimationTargetPose> animatedPoseLookup =
                SystemAPI.GetComponentLookup<AnimationTargetPose>(false);

            ComponentLookup<AnimationTargetRestPose> restPoseLookup =
                SystemAPI.GetComponentLookup<AnimationTargetRestPose>(true);

            // ============================================================
            // UPDATE LAYER TIMES
            // ============================================================

            foreach (var layers
                     in SystemAPI.Query<DynamicBuffer<AnimationLayer>>())
            {
                for (int i = 0; i < layers.Length; i++)
                {
                    ref AnimationLayer layer = ref layers.ElementAt(i);

                    if (!layer.active)
                        continue;

                    AnimationClipSO clipSO =
                        libraryManaged.library.GetClip(layer.animation);

                    if (clipSO == null || clipSO.duration <= 0f)
                        continue;

                    if (!timeControl.isPaused)
                    {
                        layer.time += deltaTime * layer.speed * timeControl.playbackSpeed;

                        if (layer.time >= clipSO.duration)
                        {
                            if (layer.looping)
                            {
                                layer.time = math.fmod(layer.time, clipSO.duration);
                            }
                            else
                            {
                                layer.time = clipSO.duration;
                                layer.active = false;
                            }
                        }
                    }
                }
            }

            // ============================================================
            // SAMPLE & APPLY POSES
            // ============================================================

            foreach (var (layers, targets, entity)
                     in SystemAPI.Query<
                            DynamicBuffer<AnimationLayer>,
                            DynamicBuffer<AnimatorTarget>>()
                         .WithEntityAccess())
            {
                for (int i = 0; i < targets.Length; i++)
                {
                    Entity targetEntity = targets[i].entity;
                    AnimationTarget partType = targets[i].target;

                    if (!animatedPoseLookup.HasComponent(targetEntity) ||
                        !restPoseLookup.HasComponent(targetEntity))
                        continue;

                    AnimationTargetRestPose restPose =
                        restPoseLookup[targetEntity];

                    float3 finalPosition = restPose.localPosition;
                    float finalRotation = restPose.rotation;
                    float2 finalScale = restPose.scale;
                    int finalImageIndex = restPose.baseImageIndex;

                    AnimatedProperties appliedProperties = AnimatedProperties.None;

                    for (int layerIndex = layers.Length - 1; layerIndex >= 0; layerIndex--)
                    {
                        var layer = layers[layerIndex];

                        if (!layer.active)
                            continue;

                        AnimationClipSO clipSO =
                            libraryManaged.library.GetClip(layer.animation);

                        if (clipSO == null || clipSO.duration <= 0f)
                            continue;

                        float normalizedTime = layer.time / clipSO.duration;
                        float quantizedTime =
                            QuantizeTime(normalizedTime, clipSO.duration, frameRate);

                        SampleClipSO(
                            clipSO,
                            partType,
                            quantizedTime,
                            ref finalPosition,
                            ref finalRotation,
                            ref finalScale,
                            ref finalImageIndex,
                            ref appliedProperties);
                    }

                    animatedPoseLookup[targetEntity] = new AnimationTargetPose
                    {
                        localPosition = finalPosition,
                        rotation = finalRotation,
                        scale = finalScale,
                        imageIndex = finalImageIndex
                    };
                }
            }

            // ============================================================
            // UPDATE NORMALIZED TIME (UI)
            // ============================================================

            if (!timeControl.isPaused)
            {
                AnimationClipSO currentClipSO =
                    libraryManaged.library.GetClip(timeControl.currentAnimation);

                if (currentClipSO != null && currentClipSO.duration > 0f)
                {
                    float baseTime = 0f;

                    foreach (var layers
                             in SystemAPI.Query<DynamicBuffer<AnimationLayer>>())
                    {
                        for (int i = 0; i < layers.Length; i++)
                        {
                            if (layers[i].layer == AnimationLayerType.Base &&
                                layers[i].active)
                            {
                                baseTime = layers[i].time;
                                break;
                            }
                        }
                        break;
                    }

                    SystemAPI.SetSingleton(new EditorAnimationTimeControl
                    {
                        isPaused = timeControl.isPaused,
                        normalizedTime = baseTime / currentClipSO.duration,
                        playbackSpeed = timeControl.playbackSpeed,
                        forceLoop = timeControl.forceLoop,
                        currentAnimation = timeControl.currentAnimation
                    });
                }
            }
        }
        catch (System.Exception exception)
        {
            Debug.LogError(
                $"[EditorAnim] Exception: {exception.Message}\n{exception.StackTrace}");
        }
    }

    private float QuantizeTime(float normalizedTime, float duration, int frameRate)
    {
        if (frameRate <= 0 || duration <= 0f)
            return normalizedTime;

        float totalFrames = duration * frameRate;
        float currentFrame = math.floor(normalizedTime * totalFrames);
        return currentFrame / totalFrames;
    }

    private void SampleClipSO(
        AnimationClipSO clip,
        AnimationTarget target,
        float normalizedTime,
        ref float3 position,
        ref float rotation,
        ref float2 scale,
        ref int imageIndex,
        ref AnimatedProperties appliedProperties)
    {
        if (clip.partTracks == null)
            return;

        foreach (AnimationClipSO.PartTrack track in clip.partTracks)
        {
            if (track == null || track.animationTarget != target)
                continue;

            if (track.keyframes == null || track.keyframes.Count == 0)
                continue;

            AnimatedProperties propertiesToApply =
                track.animatedProperties & ~appliedProperties;

            if (propertiesToApply == AnimatedProperties.None)
                break;

            AnimationClipSO.Keyframe sampledKeyframe =
                SampleKeyframesSO(track, normalizedTime);

            ApplyTrackToPose(
                track,
                sampledKeyframe,
                propertiesToApply,
                ref position,
                ref rotation,
                ref scale,
                ref imageIndex);

            appliedProperties |= track.animatedProperties;
            break;
        }
    }

    private AnimationClipSO.Keyframe SampleKeyframesSO(
        AnimationClipSO.PartTrack track,
        float normalizedTime)
    {
        var keyframes = track.keyframes;

        int previousIndex = 0;
        int nextIndex = 0;

        for (int i = 0; i < keyframes.Count; i++)
        {
            if (keyframes[i].normalizedTime <= normalizedTime)
                previousIndex = i;

            if (keyframes[i].normalizedTime >= normalizedTime)
            {
                nextIndex = i;
                break;
            }

            nextIndex = i;
        }

        AnimationClipSO.Keyframe previous = keyframes[previousIndex];
        AnimationClipSO.Keyframe next = keyframes[nextIndex];

        InterpolationMode interpolationMode =
            previous.overrideInterpolation
                ? previous.interpolationOverride
                : track.interpolation;

        if (previousIndex == nextIndex ||
            interpolationMode == InterpolationMode.Step)
        {
            return previous;
        }

        float range = next.normalizedTime - previous.normalizedTime;
        float t = range > 0f
            ? (normalizedTime - previous.normalizedTime) / range
            : 0f;

        t = ApplyEasing(t, interpolationMode);

        return new AnimationClipSO.Keyframe
        {
            normalizedTime = normalizedTime,
            position = Vector3.Lerp(previous.position, next.position, t),
            rotation = Mathf.Lerp(previous.rotation, next.rotation, t),
            scale = Vector2.Lerp(previous.scale, next.scale, t),
            imageIndex = t < 0.5f ? previous.imageIndex : next.imageIndex,
            overrideInterpolation = previous.overrideInterpolation,
            interpolationOverride = previous.interpolationOverride
        };
    }

    private float ApplyEasing(float t, InterpolationMode mode)
    {
        switch (mode)
        {
            case InterpolationMode.EaseIn:
                return t * t;
            case InterpolationMode.EaseOut:
                return 1f - (1f - t) * (1f - t);
            case InterpolationMode.EaseInOut:
                return t < 0.5f
                    ? 2f * t * t
                    : 1f - 2f * (1f - t) * (1f - t);
            default:
                return t;
        }
    }

    private void ApplyTrackToPose(
        AnimationClipSO.PartTrack track,
        AnimationClipSO.Keyframe sampled,
        AnimatedProperties propertiesToApply,
        ref float3 position,
        ref float rotation,
        ref float2 scale,
        ref int imageIndex)
    {
        bool isAdditive = track.blendMode == BlendMode.Additive;

        if ((propertiesToApply & AnimatedProperties.PositionX) != 0)
            position.x = isAdditive ? position.x + sampled.position.x : sampled.position.x;

        if ((propertiesToApply & AnimatedProperties.PositionY) != 0)
            position.y = isAdditive ? position.y + sampled.position.y : sampled.position.y;

        if ((propertiesToApply & AnimatedProperties.PositionZ) != 0)
            position.z = isAdditive ? position.z + sampled.position.z : sampled.position.z;

        if ((propertiesToApply & AnimatedProperties.Rotation) != 0)
            rotation = isAdditive ? rotation + sampled.rotation : sampled.rotation;

        if ((propertiesToApply & AnimatedProperties.ScaleX) != 0)
            scale.x = isAdditive ? scale.x * sampled.scale.x : sampled.scale.x;

        if ((propertiesToApply & AnimatedProperties.ScaleY) != 0)
            scale.y = isAdditive ? scale.y * sampled.scale.y : sampled.scale.y;

        if ((propertiesToApply & AnimatedProperties.ImageIndex) != 0 &&
            sampled.imageIndex >= 0)
        {
            imageIndex = sampled.imageIndex;
        }
    }
}
