// Copyright (c) 2026 Stitch Punk. All rights reserved.

using Unity.Entities;
using Unity.Mathematics;

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
    /// <remarks>
    /// Marked serializable because architecture section 8 M2 exposes this struct directly as
    /// <c>ActorAuthoring.sampleOverride</c>, an inspector-facing field; Unity will not persist a
    /// custom struct without the attribute. It changes nothing about the component at runtime.
    /// </remarks>
    [System.Serializable]
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

    /// <summary>
    /// The actor's rest-pose bounds in <strong>actor space</strong> — the box the rig occupies with
    /// every target at its rest pose, before any clip displaces anything (architecture sections 5.2,
    /// 4.6, 5.8; amendment A13).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This exists because <see cref="ClipBlob.offsetBounds"/> is <em>offset</em> space, not actor
    /// space. Transform keys store offsets from a target's rest pose, and rest poses live on the
    /// prefab, which the authoring assembly that runs the bake cannot read — so every box the bake
    /// produces is centred on the origin. Treating that as actor space would give any rig whose
    /// parts sit away from the origin a box smaller than its own silhouette, and it would cull
    /// visibly: exactly the defect section 5.8 exists to close.
    /// </para>
    /// <para>
    /// Written by the entity baker, which does see the prefab hierarchy.
    /// <c>RenderBoundsUpdateSystem</c> (section 5.8) unions this with the
    /// <see cref="ClipBlob.offsetBounds"/> of every clip still referenced to obtain the actor-space
    /// bounds it writes into <c>RenderBounds</c>. No system may write
    /// <see cref="ClipBlob.offsetBounds"/> into <c>RenderBounds</c> directly.
    /// </para>
    /// </remarks>
    public struct ActorRestBounds : IComponentData
    {
        /// <summary>Rest-pose bounds in actor space: centre plus half-extents.</summary>
        public AABB value;
    }
}
