// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Runs first in <see cref="AnimationToolkitBindingSystemGroup"/>: creates the
    /// <see cref="AnimationToolkitConfig"/> and <see cref="RagdollConfig"/> singletons with
    /// defaults when absent, then disables itself. Never overwrites an existing singleton, so a
    /// host's own authored config always wins.
    /// </summary>
    [UpdateInGroup(typeof(AnimationToolkitBindingSystemGroup), OrderFirst = true)]
    [BurstCompile]
    public partial struct ConfigBootstrapSystem : ISystem
    {
        // 20, 40 and 80 world units, squared — visibly conservative rather than tuned, so an
        // unconfigured opt-in does something reasonable; a project that cares about LOD sets its own.
        internal static readonly float4 DefaultLodDistancesSq = new float4(400f, 1600f, 6400f, 0f);

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            if (!SystemAPI.HasSingleton<AnimationToolkitConfig>())
            {
                // CreateSingleton, not CreateEntity + AddComponentData: builds the archetype in one
                // step and applies the debug name behind Entities' own debug-names guard, so this
                // costs nothing in a player build.
                state.EntityManager.CreateSingleton(
                    new AnimationToolkitConfig
                    {
                        defaultSampleRateHz = 0f,
                        distanceLodEnabled = false,
                        lodDistancesSq = DefaultLodDistancesSq
                    },
                    "AnimationToolkitConfig");
            }

            if (!SystemAPI.HasSingleton<RagdollConfig>())
            {
                // Defaults are tuned-but-conservative rather than do-nothing: unlike
                // AnimationToolkitConfig's zeros, a zero gravity or substep cap would make every
                // ragdoll in the project simply not fall.
                state.EntityManager.CreateSingleton(
                    new RagdollConfig
                    {
                        worldGravity = new float3(0f, -9.81f, 0f),
                        sleepLinearSpeed = 0.05f,
                        sleepAngularSpeed = 0.05f,
                        sleepDelaySeconds = 0.5f,
                        maxSubstepsPerFrame = 4,
                        fallbackGroundHeight = 0f,
                        contactProbeRadius = 0.02f
                    },
                    "RagdollConfig");
            }

            state.Enabled = false;
        }
    }
}
