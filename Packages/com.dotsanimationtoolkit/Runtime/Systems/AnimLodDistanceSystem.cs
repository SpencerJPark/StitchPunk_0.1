// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// The optional distance-driven writer of <see cref="AnimLod.level"/>
    /// (architecture section 5.10).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Off by default and opt-in twice over.</strong> A world must set
    /// <see cref="AnimationToolkitConfig.distanceLodEnabled"/>, and an actor must carry
    /// <see cref="AnimLod"/>, which <c>ActorBaker</c> adds only when the authoring asks
    /// (amendment A23). Either switch left alone and this system writes nothing — a host that
    /// drives LOD from its own culling or crowd budget simply never turns it on, and the level it
    /// writes is never contested.
    /// </para>
    /// <para>
    /// <strong>Not gated on <see cref="AnimVisible"/>, unlike the rest of this group.</strong> The
    /// level is an input to the systems that are gated, and computing it for an off-screen actor
    /// costs one squared-distance compare. Gating it would leave a stale level on the frame an actor
    /// comes back into view — the one frame where §5.9's self-healing promise says everything must
    /// already be right.
    /// </para>
    /// <para>
    /// <strong>Distance is measured to the actor's origin, not to its bounds.</strong> A box test
    /// would be more accurate for a large rig and is deliberately not done: LOD thresholds are a
    /// tuning knob a host sets by eye, and a per-actor bounds fetch to refine a number that is
    /// already approximate would cost more than it is worth. §5.10's mesh-level LOD — where
    /// accuracy does matter — is delegated to Entities Graphics' own LOD path.
    /// </para>
    /// </remarks>
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

    /// <summary>
    /// Writes one actor's LOD level from its squared distance to the camera.
    /// </summary>
    [BurstCompile]
    internal partial struct AssignDistanceLodJob : IJobEntity
    {
        public float3 cameraPosition;
        public float4 lodDistancesSq;

        private void Execute(in LocalToWorld localToWorld, ref AnimLod animLod)
        {
            float distanceSq = math.lengthsq(localToWorld.Position - cameraPosition);
            animLod.level = AnimationLodPolicy.LevelForDistanceSq(distanceSq, in lodDistancesSq);
        }
    }
}
