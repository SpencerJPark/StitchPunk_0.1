// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// One "set the state, see the character move" entry the 2D Direction Sets panel can load in a
    /// single pick — a direction set, the rig to play it on, and how many directions the actor that
    /// uses it turns through. Deliberately flat and pre-labelled: what a set is for (an action, a
    /// stance, a locomotion state) is host vocabulary this package has no type for.
    /// </summary>
    public struct DirectionSetContextEntry
    {
        /// <summary>What the host calls this entry, e.g. "Zombie · Moving". Shown verbatim.</summary>
        public string label;

        /// <summary>The set this entry maps to. Null is legal and means "not wired up yet".</summary>
        public DirectionSetAsset set;

        /// <summary>The rig to preview it on, resolved by the host from whatever it knows.</summary>
        public RigAsset previewRig;

        /// <summary>
        /// The clip set holding this entry's clips. Picked alongside the rig, because the panel
        /// previews through the Clip Editor's own registry — an entry that loaded a rig but left the
        /// clip set behind would offer a queue whose clips nothing can pose.
        /// </summary>
        public ClipSetAsset previewClipSet;

        /// <summary>
        /// How many directions the actor turns through, not the set's own coverage — a
        /// Two-coverage set on a Six-turning actor previews the same fold the runtime applies.
        /// </summary>
        public AnimationDirections actorDirections;
    }

    /// <summary>
    /// The one-way seam that lets a host project offer its own units to the 2D Direction Sets
    /// panel. The package declares this interface and the panel consumes it; with no provider
    /// registered the panel simply hides its Unit Context dropdown.
    /// </summary>
    public interface IDirectionSetContextProvider
    {
        /// <summary>Every entry this host can offer, already labelled. Never null; empty is fine.</summary>
        IReadOnlyList<DirectionSetContextEntry> GetEntries();
    }
}
