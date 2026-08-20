// Copyright (c) 2026 Stitch Punk. All rights reserved.

using Unity.Entities;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// The toolkit's single insertion point into a host frame (architecture section 5.1). Everything
    /// this package runs lives under it, so a host can reason about — and disable — the whole feature
    /// as one unit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>No scene gating and no host tags.</strong> The group runs whenever its queries match,
    /// and an empty world costs nothing. This is deliberate: requiring a tag component would make the
    /// package's systems refuse to run in any world that had not been taught about it, which is the
    /// coupling the source audit flagged in the host game (its systems require a `GameSceneTag`).
    /// A host that wants the feature off uses <see cref="ToolkitWorldControl.SetEnabled"/>.
    /// </para>
    /// <para>
    /// <strong>This type declares no <c>UpdateBefore</c>/<c>UpdateAfter</c> edges</strong>, only its
    /// parent group. Ordering edges here would be ordering opinions about assemblies this package has
    /// never seen, and two such opinions in one project deadlock the sort. A host orders *its own*
    /// groups against this type instead — the attribute lives on the host's type and references ours,
    /// so expressing an order costs the host nothing and costs this package no changes at all.
    /// </para>
    /// </remarks>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class AnimationToolkitSystemGroup : ComponentSystemGroup
    {
    }

    /// <summary>
    /// Spawn-time re-binding, before anything reads a binding (architecture section 5.1).
    /// </summary>
    /// <remarks>
    /// <c>OrderFirst</c> because an ECB-instantiated actor's <c>RigPartRef</c> buffer still points at
    /// the prefab's part entities until <c>RigBindingSystem</c> rebuilds it. Any system that read a
    /// binding before this group ran would read a stale entity reference — reliably, every spawn.
    /// </remarks>
    [UpdateInGroup(typeof(AnimationToolkitSystemGroup), OrderFirst = true)]
    public partial class AnimationToolkitBindingSystemGroup : ComponentSystemGroup
    {
    }

    /// <summary>
    /// Playback state, time and events — the half of the toolkit that is gameplay-visible
    /// (architecture section 5.1).
    /// </summary>
    /// <remarks>
    /// <strong>Never gated on <see cref="AnimVisible"/>.</strong> Timers advance and events fire for
    /// off-screen actors, so an actor that finishes a clip behind the camera still reports it and
    /// still holds exact time when it comes back into view. Gating logic on visibility is what makes
    /// animation-driven gameplay desync from the simulation; the split between this group and
    /// <see cref="AnimationToolkitPresentationSystemGroup"/> is the whole point of the design.
    /// </remarks>
    [UpdateInGroup(typeof(AnimationToolkitSystemGroup))]
    [UpdateAfter(typeof(AnimationToolkitBindingSystemGroup))]
    [UpdateBefore(typeof(AnimationToolkitPresentationSystemGroup))]
    public partial class AnimationToolkitLogicSystemGroup : ComponentSystemGroup
    {
    }

    /// <summary>
    /// Sampling and the writes that feed rendering (architecture section 5.1).
    /// </summary>
    /// <remarks>
    /// Runs last, and its systems skip actors whose <see cref="AnimVisible"/> is disabled. Re-enabling
    /// is self-healing and needs no dirty tracking: these systems run every frame for every enabled
    /// actor, so the first visible frame re-samples and re-writes every property from scratch.
    /// </remarks>
    [UpdateInGroup(typeof(AnimationToolkitSystemGroup), OrderLast = true)]
    public partial class AnimationToolkitPresentationSystemGroup : ComponentSystemGroup
    {
    }
}
