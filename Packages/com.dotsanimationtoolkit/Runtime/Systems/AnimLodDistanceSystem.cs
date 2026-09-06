// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Runs first in <see cref="AnimationToolkitPresentationSystemGroup"/>: writes
    /// <see cref="AnimLod.level"/> from each actor's squared distance to the camera. Opt-in twice
    /// over (<see cref="AnimationToolkitConfig.distanceLodEnabled"/> and the <see cref="AnimLod"/>
    /// component); not gated on <see cref="AnimVisible"/>, since the level feeds systems that are.
    /// </summary>
    [UpdateInGroup(typeof(AnimationToolkitPresentationSystemGroup), OrderFirst = true)]
    [BurstCompile]
    public partial struct AnimLodDistanceSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<AnimLod>();
            state.RequireForUpdate<AnimationToolkitCameraData>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // The config singleton is created by ConfigBootstrapSystem in another group. A world
            // that has not run it yet has not opted in either, so its absence and its default both
            // mean the same thing: do nothing.
            if (!SystemAPI.TryGetSingleton(out AnimationToolkitConfig toolkitConfig)
                || !toolkitConfig.distanceLodEnabled)
            {
                return;
            }

            AnimationToolkitCameraData cameraData = SystemAPI.GetSingleton<AnimationToolkitCameraData>();

            AssignDistanceLodJob assignJob = new AssignDistanceLodJob
            {
                cameraPosition = cameraData.position,
                lodDistancesSq = toolkitConfig.lodDistancesSq
            };
            state.Dependency = assignJob.ScheduleParallel(state.Dependency);
        }
    }

    [BurstCompile]
    internal partial struct AssignDistanceLodJob : IJobEntity
    {
        public float3 cameraPosition;
        public float4 lodDistancesSq;

        // Measured to the actor's origin, not its bounds: LOD thresholds are a tuning knob set by
        // eye, and a per-actor bounds fetch would cost more than the extra accuracy is worth.
        private void Execute(in LocalToWorld localToWorld, ref AnimLod animLod)
        {
            float distanceSq = math.lengthsq(localToWorld.Position - cameraPosition);
            animLod.level = AnimationLodResolver.ResolveLevelForDistanceSq(distanceSq, in lodDistancesSq);
        }
    }
}
