// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Entities;
using Unity.Mathematics;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Actor-root buffer mapping each bound part entity to its dense target index. Instantiating
    /// via ECB does not remap entity references inside dynamic buffers, so <c>RigBindingSystem</c>
    /// must rebuild this after spawn; until then it is correct only for baked instances.
    /// </summary>
    [InternalBufferCapacity(16)]
    public struct RigPartRef : IBufferElementData
    {
        public Entity part;

        public int targetIndex; // position in ClipRegistryBlob.sortedTargetIds
    }

    /// <summary>
    /// Enableable spawn-remap tag on the actor root; baked enabled so ECB-instantiated copies
    /// start enabled. <c>RigBindingSystem</c> rebuilds <see cref="RigPartRef"/> and
    /// <see cref="RigPartBinding.actorRoot"/> from the LinkedEntityGroup, then disables this tag.
    /// </summary>
    public struct RigBindingUninitialized : IComponentData, IEnableableComponent
    {
    }

    /// <summary>
    /// The package-owned visibility boundary. Baked enabled — the package never sets it; a host
    /// culling bridge may. Presentation systems skip disabled actors; logic and timers ignore it,
    /// so off-screen actors keep exact time and events.
    /// </summary>
    public struct AnimVisible : IComponentData, IEnableableComponent
    {
    }

    /// <summary>
    /// Enableable clip-change signal for render bounds. Baked enabled (guarantees a first-frame
    /// write); enabled by command/time systems on a clip-set change, disabled only by
    /// <c>RenderBoundsUpdateSystem</c> after it writes the bounds union. Never a change-version filter.
    /// </summary>
    public struct BoundsDirty : IComponentData, IEnableableComponent
    {
    }

    /// <summary>
    /// Per-actor sampling rate settings. An actor samples only when
    /// <c>floor((elapsedTime + phase01 / rateHz) x rateHz)</c> advances; playback time itself is
    /// never quantized.
    /// </summary>
    // [Serializable]: exposed directly as ActorAuthoring.sampleOverride, an inspector field Unity
    // won't persist without it. Changes nothing about the component at runtime.
    [System.Serializable]
    public struct SampleSettings : IComponentData
    {
        public float rateHz; // 0 = every frame; falls back to AnimationToolkitConfig.defaultSampleRateHz

        public float phase01; // [0, 1); spreads crowd sampling across frames
    }

    /// <summary>
    /// Per-actor animation level of detail. Affects CPU presentation only, never timers or events.
    /// Written by the host or by the optional <c>AnimLodDistanceSystem</c>.
    /// </summary>
    public struct AnimLod : IComponentData
    {
        public byte level; // 0-3: full, half rate, quarter rate + snapped blends, frozen pose
    }

    /// <summary>
    /// What the presentation half remembers between frames about the pose it last produced.
    /// Written by <c>TransformSampleSystem</c>, read by nothing else.
    /// </summary>
    public struct AnimSampleState : IComponentData
    {
        public int sampledClipSignature; // order-sensitive fold of the sampled clips; compared only, never decoded
    }

    /// <summary>
    /// The actor's rest-pose bounds in actor space — the rig's box at rest, before any clip
    /// displaces anything. Written by the entity baker; <c>RenderBoundsUpdateSystem</c> unions this
    /// with the referenced clips' offset bounds to produce <c>RenderBounds</c>.
    /// </summary>
    public struct ActorRestBounds : IComponentData
    {
        public AABB value; // centre + half-extents
    }
}
