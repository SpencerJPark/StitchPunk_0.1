// Copyright (c) 2026 Stitch Punk. All rights reserved.

using Unity.Entities;

namespace StitchPunk.AnimationToolkit
{
    /// <summary>
    /// Actor-root buffer mapping each bound part entity to its dense target index
    /// (architecture section 5.2). Rebuilt by <c>RigBindingSystem</c> after ECB instantiation
    /// (section 5.3) because instantiate does not remap entity references inside dynamic buffers.
    /// </summary>
    [InternalBufferCapacity(16)]
    public struct RigPartRef : IBufferElementData
    {
        /// <summary>The bound part entity.</summary>
        public Entity part;

        /// <summary>The part's dense target index (position in <see cref="ClipRegistryBlob.sortedTargetIds"/>).</summary>
        public int targetIndex;
    }

    /// <summary>
    /// Enableable spawn-remap tag on the actor root (architecture sections 5.2, 5.3). Baked
    /// ENABLED so ECB-instantiated copies start enabled; <c>RigBindingSystem</c> rebuilds
    /// <see cref="RigPartRef"/> and <see cref="RigPartBinding.actorRoot"/> from the
    /// LinkedEntityGroup, then disables the tag.
    /// </summary>
    public struct RigBindingUninitialized : IComponentData, IEnableableComponent
    {
    }

    /// <summary>
    /// The package-owned visibility boundary (architecture sections 5.2, 5.9). Baked ENABLED —
    /// everything animates by default. The package never sets it; any provider may (a host culling
    /// bridge, or nothing at all). Presentation systems skip disabled actors; logic systems and
    /// timers never look at it, so off-screen actors keep exact time and events.
    /// </summary>
    public struct AnimVisible : IComponentData, IEnableableComponent
    {
    }

    /// <summary>
    /// Enableable clip-change signal for render bounds (architecture sections 5.2, 5.8). Baked
    /// ENABLED (guarantees a first-frame bounds write); enabled by the command/time systems when
    /// any layer's clip set changes; disabled again by <c>RenderBoundsUpdateSystem</c> after it
    /// writes the bounds union — the sole reset path. Never a change-version filter.
    /// </summary>
    public struct BoundsDirty : IComponentData, IEnableableComponent
    {
    }

    /// <summary>
    /// Per-actor sampling rate settings (architecture sections 5.2, 5.6). An actor samples only on
    /// frames where <c>floor((elapsedTime + phase01 / rateHz) × rateHz)</c> advances; playback time
    /// itself is never quantized — only sampling frequency.
    /// </summary>
    public struct SampleSettings : IComponentData
    {
        /// <summary>Sample rate in Hz; 0 = sample every frame (falls back to <see cref="AnimationToolkitConfig.defaultSampleRateHz"/>).</summary>
        public float rateHz;

        /// <summary>Per-entity phase offset in [0, 1) spreading crowd sampling across frames.</summary>
        public float phase01;
    }

    /// <summary>
    /// Per-actor animation level of detail (architecture sections 5.2, 5.10). Affects CPU
    /// presentation only — never timers, never events. Written by the host or by the optional
    /// <c>AnimLodDistanceSystem</c>.
    /// </summary>
    public struct AnimLod : IComponentData
    {
        /// <summary>LOD level 0–3: full, half rate, quarter rate + snapped blends, frozen pose.</summary>
        public byte level;
    }
}
