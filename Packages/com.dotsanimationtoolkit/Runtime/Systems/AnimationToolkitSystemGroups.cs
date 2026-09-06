// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Entities;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// The toolkit's single insertion point into a host frame; everything this package runs lives
    /// under it. No scene gating and no host tags — a host that wants the feature off uses
    /// <see cref="ToolkitWorldApi.SetEnabled"/> instead.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class AnimationToolkitSystemGroup : ComponentSystemGroup
    {
    }

    /// <summary>
    /// Spawn-time re-binding, before anything reads a binding. <c>OrderFirst</c> because an
    /// ECB-instantiated actor's <c>RigPartRef</c> buffer still points at the prefab's part entities
    /// until <c>RigBindingSystem</c> rebuilds it.
    /// </summary>
    [UpdateInGroup(typeof(AnimationToolkitSystemGroup), OrderFirst = true)]
    public partial class AnimationToolkitBindingSystemGroup : ComponentSystemGroup
    {
    }

    /// <summary>
    /// Playback state, time and events — the gameplay-visible half of the toolkit. Never gated on
    /// <see cref="AnimVisible"/>: timers advance and events fire for off-screen actors, so animation
    /// stays in sync with simulation regardless of what's on screen.
    /// </summary>
    [UpdateInGroup(typeof(AnimationToolkitSystemGroup))]
    [UpdateAfter(typeof(AnimationToolkitBindingSystemGroup))]
    [UpdateBefore(typeof(AnimationToolkitPresentationSystemGroup))]
    public partial class AnimationToolkitLogicSystemGroup : ComponentSystemGroup
    {
    }

    /// <summary>
    /// Sampling and the writes that feed rendering. Runs last; its systems skip actors whose
    /// <see cref="AnimVisible"/> is disabled, and re-enabling needs no dirty tracking since every
    /// enabled actor is fully re-sampled every frame.
    /// </summary>
    [UpdateInGroup(typeof(AnimationToolkitSystemGroup), OrderLast = true)]
    public partial class AnimationToolkitPresentationSystemGroup : ComponentSystemGroup
    {
    }

    /// <summary>
    /// The ragdoll's five systems: capture, the fallback world probe, the solver, the write-back,
    /// and release — <c>OrderFirst</c>/<c>OrderLast</c> capture/release, with explicit
    /// <c>UpdateAfter</c> chaining probe → solve → apply between them.
    /// </summary>
    // Must run after BillboardResolveSystem (a Planar2D ragdoll needs this frame's billboard
    // frame, not last frame's) and before SocketResolveSystem (otherwise an attached item lags
    // the hand by one frame).
    [UpdateInGroup(typeof(AnimationToolkitPresentationSystemGroup))]
    [UpdateAfter(typeof(BillboardResolveSystem))]
    [UpdateBefore(typeof(SocketResolveSystem))]
    public partial class AnimationToolkitRagdollSystemGroup : ComponentSystemGroup
    {
    }
}
