// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Entities;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// Marks an actor entity whose <c>ActorBaker</c> pass bailed out after reporting why
    /// (architecture section 4.1, amendment A22). Its parts therefore have no registry to resolve
    /// against, and <c>RigBindingBakingSystem</c> stays silent about them because the actionable
    /// message has already been printed once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The tag exists so that suppression is driven by an explicit signal rather than by an inferred
    /// one. Before it, the binding pass simply said nothing whenever an actor entity carried no
    /// <c>ClipRegistry</c>, which was correct only because each of <c>ActorBaker</c>'s bail-out paths
    /// happened to log first. Nothing enforced that coupling: a fourth bail-out added without a log,
    /// or any future baking system that stripped <c>ClipRegistry</c>, would have left every part
    /// under that actor silently unbound with no diagnostic anywhere in the project — the
    /// "it does not animate and nothing says why" support ticket a sold package cannot afford.
    /// </para>
    /// <para>
    /// With the tag, silence is a claim someone made rather than a gap. An actor missing its
    /// registry <em>without</em> the tag is an unexplained failure, and the binding pass reports it.
    /// </para>
    /// <para>
    /// <see cref="BakingTypeAttribute"/> for the same reason as <see cref="RigPartBakeLink"/>:
    /// incremental baking re-runs only the bakers whose inputs changed, while the binding pass sees
    /// every actor on every pass, so the tag must outlive the pass that wrote it. Baking types never
    /// reach the built entity scene.
    /// </para>
    /// <para>
    /// <strong>Internal.</strong> It was briefly public on the grounds that Entities requires baking
    /// types to be reachable from the system that queries them. That is not a constraint here: the
    /// only writer (<c>ActorBaker</c>) and the only reader (<c>RigBindingBakingSystem</c>'s job, which
    /// is itself internal) live in this assembly, and the tests reach it through the contracted
    /// <c>InternalsVisibleTo</c>. A type carrying no consumer contract and never reaching a built
    /// entity scene should not widen a sold package's public surface.
    /// </para>
    /// </remarks>
    [BakingType]
    internal struct ActorBakeFailed : IComponentData
    {
    }
}
