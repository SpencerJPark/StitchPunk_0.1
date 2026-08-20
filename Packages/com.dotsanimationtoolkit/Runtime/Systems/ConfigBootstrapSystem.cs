// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Creates the <see cref="AnimationToolkitConfig"/> singleton with defaults when a world does not
    /// already have one, so the package works with zero setup (architecture sections 5.2, 5.12).
    /// </summary>
    /// <remarks>
    /// <para>
    /// All structural work happens in <see cref="OnCreate"/> and the system then disables itself, so
    /// this costs one entity creation per world and nothing per frame.
    /// </para>
    /// <para>
    /// <strong>It never overwrites an existing singleton.</strong> A host that bakes or authors its
    /// own config — or one that has already tuned the values at runtime — must not have them replaced
    /// by defaults because a system happened to initialise later. "Create if absent" is the whole
    /// contract; there is deliberately no update path here.
    /// </para>
    /// <para>
    /// Defaults are the do-nothing ones: <c>defaultSampleRateHz = 0</c> (sample every frame, no
    /// quantization) and <c>distanceLodEnabled = false</c>. A consumer who installs the package and
    /// plays should see full-quality animation, not a subtly degraded one they then have to discover
    /// how to turn off. <c>lodDistancesSq</c> carries ascending squared thresholds so that a host
    /// which flips <c>distanceLodEnabled</c> on without setting distances gets sane behaviour rather
    /// than every actor snapping to LOD 3 against an all-zero threshold set.
    /// </para>
    /// </remarks>
    [UpdateInGroup(typeof(AnimationToolkitBindingSystemGroup), OrderFirst = true)]
    [BurstCompile]
    public partial struct ConfigBootstrapSystem : ISystem
    {
        /// <summary>Default squared camera distances at which LOD 1, 2 and 3 begin; w is reserved.</summary>
        /// <remarks>
        /// 20, 40 and 80 world units, squared. Chosen to be visibly conservative rather than tuned:
        /// they exist so an unconfigured opt-in does something reasonable, and any project that cares
        /// about LOD will set its own.
        /// </remarks>
        internal static readonly float4 DefaultLodDistancesSq = new float4(400f, 1600f, 6400f, 0f);

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            if (!SystemAPI.HasSingleton<AnimationToolkitConfig>())
            {
                // CreateSingleton, not CreateEntity + AddComponentData: it builds the archetype in
                // one step and applies the debug name behind Entities' own DOTS_DISABLE_DEBUG_NAMES
                // guard, so this costs nothing in a player build. (EntityManager has no SetName —
                // that lives on the nested Debug struct.)
                state.EntityManager.CreateSingleton(
                    new AnimationToolkitConfig
                    {
                        defaultSampleRateHz = 0f,
                        distanceLodEnabled = false,
                        lodDistancesSq = DefaultLodDistancesSq
                    },
                    "AnimationToolkitConfig");
            }

            state.Enabled = false;
        }
    }
}
