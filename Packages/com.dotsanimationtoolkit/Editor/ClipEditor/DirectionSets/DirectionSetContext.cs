// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// One "set the state, see the character move" entry the 2D Direction Sets panel can load in a
    /// single pick — a direction set, the rig to play it on, and how many directions the actor that
    /// uses it turns through.
    /// </summary>
    /// <remarks>
    /// Deliberately flat and pre-labelled. What a set is <em>for</em> — an action, a stance, a
    /// locomotion state — is host vocabulary this package has no type for, so the host flattens its
    /// own mappings into strings and hands over only what the panel can actually act on.
    /// </remarks>
    public struct DirectionSetContextEntry
    {
        /// <summary>What the host calls this entry, e.g. "Zombie · Moving". Shown verbatim.</summary>
        public string label;

        /// <summary>The set this entry maps to. Null is legal and means "not wired up yet".</summary>
        public DirectionSetAsset set;

        /// <summary>The rig to preview it on, resolved by the host from whatever it knows.</summary>
        public RigAsset previewRig;

        /// <summary>
        /// How many directions the actor turns through — <em>not</em> the set's own coverage. It
        /// drives the direction slider's quantize, so a Two-coverage set on a Six-turning actor
        /// previews the same fold the runtime applies rather than a tidier one.
        /// </summary>
        public AnimationDirections actorDirections;
    }

    /// <summary>
    /// The one-way seam that lets a host project offer its own units to the 2D Direction Sets panel.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Data-shaped, and the dependency points one way only.</strong> The package declares
    /// this interface and the panel consumes it; a host implements it from its own editor assembly
    /// and registers through <c>DirectionSetsPanel.SetContextProvider</c>. Nothing here names a host
    /// type, and with no provider registered the panel simply hides its Unit Context dropdown — the
    /// packaged-alone case, where a buyer wires sets up by hand.
    /// </para>
    /// <para>
    /// Queried each time the pane is opened rather than cached, so entries added since — a new unit,
    /// a newly assigned mapping — are there without a domain reload.
    /// </para>
    /// </remarks>
    public interface IDirectionSetContextProvider
    {
        /// <summary>Every entry this host can offer, already labelled. Never null; empty is fine.</summary>
        IReadOnlyList<DirectionSetContextEntry> GetEntries();
    }
}
