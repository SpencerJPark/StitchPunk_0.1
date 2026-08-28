// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>What a paste did, so the window can say so rather than appearing to do nothing.</summary>
    public struct ClipKeyPasteResult
    {
        /// <summary>Keys actually written into the destination.</summary>
        public int keyCount;

        /// <summary>Components created because the destination did not have them.</summary>
        public int addedComponentCount;

        /// <summary>Keys with nowhere to go — a component that could not be created.</summary>
        public int droppedKeyCount;

        /// <summary>Whether the paste declared a rig part, and so wrote the rig as well as the clip.</summary>
        public bool touchedRig;
    }

    /// <summary>
    /// Copy/paste buffer for timeline keys (architecture section 7.1, parity item "copy/paste").
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The buffer holds objects and their components, not track indices.</strong> It used to
    /// hold the index of the track each key came from and paste them straight back into the track at
    /// that index, which made copy/paste a way to duplicate keys in place and nothing else — paste
    /// onto a different object was not a thing the buffer could express, and pasting into a clip
    /// with a different track order landed the keys on whatever part happened to be sitting at that
    /// index. What is copied now is "the second flipbook of this object" and "this object's
    /// transform", and paste resolves those against whatever object is selected.
    /// </para>
    /// <para>
    /// <strong>A destination that lacks a component gets one.</strong> The components are what the
    /// keys need in order to exist, so requiring the author to add them first would be asking them
    /// to reconstruct, by hand, information the buffer is already carrying. Adding a Flipbook this
    /// way declares an unclaimed node a part exactly as the Add Component menu does, which is why a
    /// paste can write the rig — <see cref="ClipKeyPasteResult.touchedRig"/> says when it did, so
    /// the caller records the right undo.
    /// </para>
    /// <para>
    /// <strong>Poses convert between the two transform kinds.</strong> A part is keyed on a
    /// transform track and a bone by name, so copying a part's motion onto a bone has to cross that
    /// seam; <see cref="ClipKeyConversion"/> does, and the alternative — refusing the paste — would
    /// be the data model declining a request that makes perfect sense to the person making it.
    /// </para>
    /// <para>
    /// <strong>Times are stored relative to the earliest copied key</strong>, so paste lands the
    /// group at the playhead with its internal rhythm intact. Absolute times would make paste a
    /// no-op whenever the source and destination are the same clip, which is the common case.
    /// </para>
    /// </remarks>
    public static class ClipKeyClipboard
    {
        /// <summary>
        /// Which object a copied track was bound to, in the terms the asset binds by.
        /// </summary>
        /// <remarks>
        /// A part carries an id and a node carries a name — the same asymmetry every binding in the
        /// package has. Held so that a paste with nothing selected can put the keys back where they
        /// came from, and so that a copy spanning several objects keeps them apart.
        /// </remarks>
        private struct CopiedOwner : IEquatable<CopiedOwner>
        {
            public uint targetId;
            public string boneName;

            public bool Equals(CopiedOwner other)
            {
                return targetId == other.targetId
                    && string.Equals(boneName, other.boneName, StringComparison.Ordinal);
            }
        }

        /// <summary>One component's worth of copied keys, and the settings to recreate it with.</summary>
        /// <remarks>
        /// The settings travel because an auto-added track has to mean what the source meant. A
        /// sprite key's number is only interpretable beside its track's mode and base index — paste
        /// a relative key onto a track based at 0 when it was authored against 32 and it addresses
        /// a different character's artwork. They are applied to a track this paste created and never
        /// to one that was already there, which would silently retune animation somebody authored.
        /// </remarks>
        private sealed class CopiedTrack
        {
            public int objectIndex;
            public CopiedOwner owner;

            /// <summary>Transform, BoneTransform or Flipbook — the kinds that hold keys.</summary>
            public ClipComponentKind kind;

            /// <summary>Which flipbook of its object this was. Always 0 for the transform kinds.</summary>
            public int ordinal;

            public TrackBlendOp blendOp;
            public AnimatedChannels channels;
            public SpriteFrameMode spriteMode;
            public SpriteSliceSpace sliceSpace;
            public int baseIndex;

            public readonly List<TransformKey> transformKeys = new List<TransformKey>();
            public readonly List<BoneKey> boneKeys = new List<BoneKey>();
            public readonly List<SpriteKey> spriteKeys = new List<SpriteKey>();
        }

        private static readonly List<CopiedTrack> copiedTracks = new List<CopiedTrack>();
        private static readonly List<EventMarker> eventMarkers = new List<EventMarker>();
        private static readonly List<CopiedOwner> copiedOwners = new List<CopiedOwner>();
        private static readonly List<ClipComponentInstance> instanceScratch =
            new List<ClipComponentInstance>();

        /// <summary>The destinations of the paste in flight, updated as objects become parts.</summary>
        private static readonly List<ClipObjectRef> workingDestinations =
            new List<ClipObjectRef>();

        /// <summary>Whether anything is on the clipboard.</summary>
        public static bool HasContent
        {
            get { return copiedTracks.Count > 0 || eventMarkers.Count > 0; }
        }

        /// <summary>How many distinct objects the copied keys came from.</summary>
        /// <remarks>
        /// Paste reads this to decide how to spread the buffer over the selection: one source object
        /// goes onto every selected object, and several are matched up in order.
        /// </remarks>
        public static int ObjectCount
        {
            get { return copiedOwners.Count; }
        }

        /// <summary>Keys held, across every object and component.</summary>
        public static int KeyCount
        {
            get
            {
                int total = eventMarkers.Count;
                for (int trackIndex = 0; trackIndex < copiedTracks.Count; trackIndex++)
                {
                    CopiedTrack track = copiedTracks[trackIndex];
                    total += track.transformKeys.Count + track.boneKeys.Count
                        + track.spriteKeys.Count;
                }
                return total;
            }
        }

        public static void Clear()
        {
            copiedTracks.Clear();
            eventMarkers.Clear();
            copiedOwners.Clear();
        }

        /// <summary>Replaces the buffer with the given selection of <paramref name="clip"/>.</summary>
        public static void Copy(ClipAsset clip, IEnumerable<KeyAddress> addresses)
        {
            Clear();
            if (clip == null || addresses == null)
            {
                return;
            }

            float earliestTime = float.MaxValue;
            foreach (KeyAddress address in addresses)
            {
                float keyTime;
                if (CopyOne(clip, address, out keyTime))
                {
                    earliestTime = keyTime < earliestTime ? keyTime : earliestTime;
                }
            }

            if (!HasContent)
            {
                return;
            }
            Rebase(earliestTime);
        }

        /// <summary>
        /// Pastes the buffer onto <paramref name="destinations"/>, anchored at
        /// <paramref name="atTime"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>An empty destination list means "back where it came from".</strong> That is what
        /// makes duplicate-at-the-playhead work with nothing selected, and it is the honest reading
        /// of a paste with no target: the buffer already knows which objects it was taken from.
        /// </para>
        /// <para>
        /// <strong>One copied object goes onto all of them; several are matched in order.</strong>
        /// Spraying one part's motion across a row of selected parts is a thing people want, and
        /// pairing two copied objects with two selected ones is the only reading of that case that
        /// preserves what was copied. A mismatch beyond those two shapes pastes as far as the
        /// shorter list goes and reports the remainder as dropped rather than guessing.
        /// </para>
        /// <para>
        /// The caller owns the undo group and the dirty flags — on the rig as well as the clip when
        /// <see cref="ClipKeyPasteResult.touchedRig"/> comes back true. Pasted keys are appended and
        /// the caller re-sorts, matching how a drag defers sorting to the end of the gesture.
        /// </para>
        /// </remarks>
        public static ClipKeyPasteResult Paste(
            ClipAsset clip, RigAsset rig, IReadOnlyList<ClipObjectRef> destinations, float atTime)
        {
            ClipKeyPasteResult result = new ClipKeyPasteResult();
            bool pastesBackOntoSource = false;
            if (clip == null || !HasContent)
            {
                return result;
            }

            // Events belong to the clip rather than to any object, so they land whatever is
            // selected — including nothing.
            for (int markerIndex = 0; markerIndex < eventMarkers.Count; markerIndex++)
            {
                EventMarker marker = eventMarkers[markerIndex];
                marker.normalizedTime = atTime + marker.normalizedTime;
                if (clip.events == null)
                {
                    clip.events = new List<EventMarker>();
                }
                clip.events.Add(marker);
                result.keyCount++;
            }

            // Held in a list of its own, and written back to, because pasting a flipbook onto an
            // unclaimed node declares it a part. Re-reading the caller's list for the next track of
            // the same object would ask the rig to declare it a second time — harmless, since
            // adoption is idempotent, but it would report the rig touched on every track and would
            // depend on that idempotence rather than on knowing what just happened.
            workingDestinations.Clear();
            if (destinations != null)
            {
                for (int index = 0; index < destinations.Count; index++)
                {
                    workingDestinations.Add(destinations[index]);
                }
            }
            if (workingDestinations.Count == 0)
            {
                for (int ownerIndex = 0; ownerIndex < copiedOwners.Count; ownerIndex++)
                {
                    workingDestinations.Add(OwnerAsObjectRef(copiedOwners[ownerIndex]));
                }
                pastesBackOntoSource = true;
            }

            for (int trackIndex = 0; trackIndex < copiedTracks.Count; trackIndex++)
            {
                CopiedTrack track = copiedTracks[trackIndex];

                // One copied object broadcasts onto the whole selection; several pair up by
                // position. Pasting back onto the source is always pairwise — the buffer's own
                // objects are the destinations, so there is nothing to spread.
                bool broadcasts = !pastesBackOntoSource && copiedOwners.Count == 1;
                if (broadcasts)
                {
                    for (int index = 0; index < workingDestinations.Count; index++)
                    {
                        PasteTrackOnto(clip, rig, track, index, atTime, ref result);
                    }
                    continue;
                }

                if (track.objectIndex >= workingDestinations.Count)
                {
                    result.droppedKeyCount += KeyCountOf(track);
                    continue;
                }
                PasteTrackOnto(clip, rig, track, track.objectIndex, atTime, ref result);
            }

            workingDestinations.Clear();
            return result;
        }

        // -----------------------------------------------------------------------------------
        // Copy.
        // -----------------------------------------------------------------------------------

        /// <summary>Files one address into its object's component. False when it addresses nothing.</summary>
        private static bool CopyOne(ClipAsset clip, KeyAddress address, out float keyTime)
        {
            keyTime = 0f;
            switch (address.trackKind)
            {
                case TimelineTrackKind.Transform:
                {
                    TransformTrack track = GetAt(clip.transformTracks, address.trackIndex);
                    if (track == null || !HasKeyAt(track.keys, address.keyIndex))
                    {
                        return false;
                    }
                    TransformKey key = track.keys[address.keyIndex];
                    keyTime = key.normalizedTime;

                    CopiedOwner owner = new CopiedOwner
                    {
                        targetId = track.targetId,
                        boneName = string.Empty
                    };
                    CopiedTrack copied = ResolveCopiedTrack(
                        owner, ClipComponentKind.Transform, 0);
                    copied.blendOp = track.blendOp;
                    copied.channels = track.channels;
                    copied.transformKeys.Add(key);
                    return true;
                }
                case TimelineTrackKind.Bone:
                {
                    BoneTrack track = GetAt(clip.boneTracks, address.trackIndex);
                    if (track == null || !HasKeyAt(track.keys, address.keyIndex))
                    {
                        return false;
                    }
                    BoneKey key = track.keys[address.keyIndex];
                    keyTime = key.normalizedTime;

                    CopiedOwner owner = new CopiedOwner
                    {
                        targetId = 0u,
                        boneName = track.boneName ?? string.Empty
                    };
                    CopiedTrack copied = ResolveCopiedTrack(
                        owner, ClipComponentKind.BoneTransform, 0);
                    copied.boneKeys.Add(key);
                    return true;
                }
                case TimelineTrackKind.Sprite:
                {
                    SpriteTrack track = GetAt(clip.spriteTracks, address.trackIndex);
                    if (track == null || !HasKeyAt(track.keys, address.keyIndex))
                    {
                        return false;
                    }
                    SpriteKey key = track.keys[address.keyIndex];
                    keyTime = key.normalizedTime;

                    CopiedOwner owner = new CopiedOwner
                    {
                        targetId = track.targetId,
                        boneName = string.Empty
                    };

                    // The ordinal is the track's position among its own object's flipbooks, not in
                    // the clip's list. That is what survives a paste onto an object whose tracks
                    // were authored in a different order — "its second flipbook" means the same
                    // thing on both sides, and a list index does not.
                    CopiedTrack copied = ResolveCopiedTrack(
                        owner, ClipComponentKind.Flipbook,
                        FlipbookOrdinal(clip, track.targetId, address.trackIndex));
                    copied.spriteMode = track.mode;
                    copied.sliceSpace = track.sliceSpace;
                    copied.baseIndex = track.baseIndex;
                    copied.spriteKeys.Add(key);
                    return true;
                }
                default:
                {
                    int flatIndex = EventLaneAddressing.ResolveFlatIndex(
                        clip.events, address.trackIndex, address.keyIndex);
                    if (clip.events == null || flatIndex < 0)
                    {
                        return false;
                    }
                    EventMarker marker = clip.events[flatIndex];
                    keyTime = marker.normalizedTime;
                    eventMarkers.Add(marker);
                    return true;
                }
            }
        }

        /// <summary>How many of this object's flipbook tracks come before the one at an index.</summary>
        private static int FlipbookOrdinal(ClipAsset clip, uint targetId, int trackIndex)
        {
            int ordinal = 0;
            for (int index = 0; index < trackIndex && index < clip.spriteTracks.Count; index++)
            {
                SpriteTrack track = clip.spriteTracks[index];
                if (track != null && track.targetId == targetId)
                {
                    ordinal++;
                }
            }
            return ordinal;
        }

        /// <summary>The buffer entry for one object's one component, created on first sight.</summary>
        private static CopiedTrack ResolveCopiedTrack(
            CopiedOwner owner, ClipComponentKind kind, int ordinal)
        {
            for (int trackIndex = 0; trackIndex < copiedTracks.Count; trackIndex++)
            {
                CopiedTrack candidate = copiedTracks[trackIndex];
                if (candidate.owner.Equals(owner) && candidate.kind == kind
                    && candidate.ordinal == ordinal)
                {
                    return candidate;
                }
            }

            int objectIndex = copiedOwners.IndexOf(owner);
            if (objectIndex < 0)
            {
                objectIndex = copiedOwners.Count;
                copiedOwners.Add(owner);
            }

            CopiedTrack created = new CopiedTrack
            {
                objectIndex = objectIndex,
                owner = owner,
                kind = kind,
                ordinal = ordinal
            };
            copiedTracks.Add(created);
            return created;
        }

        /// <summary>Rebases every held time so the buffer stores offsets, not positions.</summary>
        private static void Rebase(float earliestTime)
        {
            for (int trackIndex = 0; trackIndex < copiedTracks.Count; trackIndex++)
            {
                CopiedTrack track = copiedTracks[trackIndex];
                for (int keyIndex = 0; keyIndex < track.transformKeys.Count; keyIndex++)
                {
                    TransformKey key = track.transformKeys[keyIndex];
                    key.normalizedTime -= earliestTime;
                    track.transformKeys[keyIndex] = key;
                }
                for (int keyIndex = 0; keyIndex < track.boneKeys.Count; keyIndex++)
                {
                    BoneKey key = track.boneKeys[keyIndex];
                    key.normalizedTime -= earliestTime;
                    track.boneKeys[keyIndex] = key;
                }
                for (int keyIndex = 0; keyIndex < track.spriteKeys.Count; keyIndex++)
                {
                    SpriteKey key = track.spriteKeys[keyIndex];
                    key.normalizedTime -= earliestTime;
                    track.spriteKeys[keyIndex] = key;
                }
            }
            for (int markerIndex = 0; markerIndex < eventMarkers.Count; markerIndex++)
            {
                EventMarker marker = eventMarkers[markerIndex];
                marker.normalizedTime -= earliestTime;
                eventMarkers[markerIndex] = marker;
            }
        }

        // -----------------------------------------------------------------------------------
        // Paste.
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Writes one copied component's keys onto one destination object, creating what it needs.
        /// </summary>
        private static void PasteTrackOnto(
            ClipAsset clip, RigAsset rig, CopiedTrack copied, int destinationIndex,
            float atTime, ref ClipKeyPasteResult result)
        {
            ClipObjectRef destination = workingDestinations[destinationIndex];
            if (!destination.IsValid)
            {
                result.droppedKeyCount += KeyCountOf(copied);
                return;
            }

            // The destination decides which transform kind the pose lands on, not the source. A
            // part is posed on a transform track and a node by name, and a paste that honoured the
            // source's choice would write a track the bake does not read for this object.
            ClipComponentKind wantedKind = copied.kind == ClipComponentKind.Flipbook
                ? ClipComponentKind.Flipbook
                : ClipComponentModel.TransformKindFor(destination);

            int trackIndex = EnsureComponent(
                clip, rig, ref destination, wantedKind, copied, ref result);
            workingDestinations[destinationIndex] = destination;
            if (trackIndex < 0)
            {
                result.droppedKeyCount += KeyCountOf(copied);
                return;
            }

            switch (wantedKind)
            {
                case ClipComponentKind.Transform:
                {
                    List<TransformKey> keys = clip.transformTracks[trackIndex].keys;
                    for (int keyIndex = 0; keyIndex < copied.transformKeys.Count; keyIndex++)
                    {
                        TransformKey key = copied.transformKeys[keyIndex];
                        key.normalizedTime += atTime;
                        keys.Add(key);
                        result.keyCount++;
                    }
                    for (int keyIndex = 0; keyIndex < copied.boneKeys.Count; keyIndex++)
                    {
                        TransformKey key =
                            ClipKeyConversion.ToTransformKey(copied.boneKeys[keyIndex]);
                        key.normalizedTime += atTime;
                        keys.Add(key);
                        result.keyCount++;
                    }
                    return;
                }
                case ClipComponentKind.BoneTransform:
                {
                    List<BoneKey> keys = clip.boneTracks[trackIndex].keys;
                    for (int keyIndex = 0; keyIndex < copied.boneKeys.Count; keyIndex++)
                    {
                        BoneKey key = copied.boneKeys[keyIndex];
                        key.normalizedTime += atTime;
                        keys.Add(key);
                        result.keyCount++;
                    }
                    for (int keyIndex = 0; keyIndex < copied.transformKeys.Count; keyIndex++)
                    {
                        BoneKey key = ClipKeyConversion.ToBoneKey(copied.transformKeys[keyIndex]);
                        key.normalizedTime += atTime;
                        keys.Add(key);
                        result.keyCount++;
                    }
                    return;
                }
                default:
                {
                    List<SpriteKey> keys = clip.spriteTracks[trackIndex].keys;
                    for (int keyIndex = 0; keyIndex < copied.spriteKeys.Count; keyIndex++)
                    {
                        SpriteKey key = copied.spriteKeys[keyIndex];
                        key.normalizedTime += atTime;
                        keys.Add(key);
                        result.keyCount++;
                    }
                    return;
                }
            }
        }

        /// <summary>
        /// The clip-list index of the destination's component, adding it when it has none.
        /// </summary>
        /// <remarks>
        /// A flipbook is matched by ordinal, so pasting "its second flipbook" onto an object with
        /// one adds a second rather than piling both sets of keys onto the first. Adding one can
        /// declare an unclaimed node a part, which is why the destination reference is taken by
        /// <c>ref</c> — every lookup after that has to know the object now has an id.
        /// </remarks>
        /// <returns>The index, or −1 when the component could not be made.</returns>
        private static int EnsureComponent(
            ClipAsset clip, RigAsset rig, ref ClipObjectRef destination, ClipComponentKind kind,
            CopiedTrack copied, ref ClipKeyPasteResult result)
        {
            int wantedOrdinal = kind == ClipComponentKind.Flipbook ? copied.ordinal : 0;

            for (int attempt = 0; attempt <= wantedOrdinal + 1; attempt++)
            {
                ClipComponentModel.CollectInstancesOfKind(
                    clip, rig, destination, kind, instanceScratch);
                if (wantedOrdinal < instanceScratch.Count
                    && instanceScratch[wantedOrdinal].HasTrack)
                {
                    return instanceScratch[wantedOrdinal].index;
                }

                string unavailableReason;
                if (!ClipComponentModel.CanAdd(clip, rig, destination, kind, out unavailableReason))
                {
                    return -1;
                }

                bool promotes = ClipComponentModel.RequiresRigTarget(kind)
                    && !destination.HasRigTarget;
                ClipComponentInstance added = ClipComponentModel.Add(
                    clip, rig, destination, kind, DescribeAddedName(destination, kind));
                if (!added.HasTrack)
                {
                    return -1;
                }
                result.addedComponentCount++;
                ApplySourceSettings(clip, kind, added.index, copied);

                if (promotes)
                {
                    result.touchedRig = true;
                    rig.EnsureStableIds();
                    RigTargetDefinition promoted =
                        ClipComponentModel.FindTargetForNode(rig, destination);
                    if (promoted != null)
                    {
                        destination = destination.WithRigTarget(promoted.Id.Value);
                    }
                }
            }
            return -1;
        }

        /// <summary>
        /// Gives a freshly created track the settings its source had.
        /// </summary>
        /// <remarks>
        /// Only ever called on a track this paste just made. Applying them to an existing track
        /// would retune animation somebody else authored — a sprite track's mode and base index
        /// change what every key already on it means.
        /// </remarks>
        private static void ApplySourceSettings(
            ClipAsset clip, ClipComponentKind kind, int trackIndex, CopiedTrack copied)
        {
            if (kind == ClipComponentKind.Flipbook)
            {
                SpriteTrack track = GetAt(clip.spriteTracks, trackIndex);
                if (track == null || copied.kind != ClipComponentKind.Flipbook)
                {
                    return;
                }
                track.mode = copied.spriteMode;
                track.sliceSpace = copied.sliceSpace;
                track.baseIndex = copied.baseIndex;
                return;
            }

            if (kind != ClipComponentKind.Transform)
            {
                return;
            }
            TransformTrack transformTrack = GetAt(clip.transformTracks, trackIndex);

            // Only from a transform source. A bone track has no blend op or channel mask to give,
            // and stamping the struct defaults over a new track would mask out every channel.
            if (transformTrack == null || copied.kind != ClipComponentKind.Transform)
            {
                return;
            }
            transformTrack.blendOp = copied.blendOp;
            transformTrack.channels = copied.channels;
        }

        /// <summary>The label a component created by a paste carries, when its kind has one.</summary>
        private static string DescribeAddedName(
            ClipObjectRef destination, ClipComponentKind kind)
        {
            if (ClipComponentModel.Scope(kind) != ClipComponentScope.Rig)
            {
                return string.Empty;
            }
            return string.IsNullOrEmpty(destination.boneName)
                ? "Pasted " + ClipComponentModel.DisplayName(kind)
                : destination.boneName;
        }

        /// <summary>
        /// The object a copied track came from, as something paste can bind to.
        /// </summary>
        /// <remarks>
        /// Deliberately thin: it has an id or a name and no hierarchy path, because the buffer never
        /// held one. That is enough to find a component that still exists — which is the only case
        /// this is used for, a paste back onto the source — and not enough to declare a new part,
        /// so a component deleted since the copy reports the keys dropped rather than reviving it
        /// somewhere the window cannot see.
        /// </remarks>
        private static ClipObjectRef OwnerAsObjectRef(CopiedOwner owner)
        {
            if (owner.targetId != 0u)
            {
                return ClipObjectRef.RigTarget(owner.targetId, 0u);
            }
            return ClipObjectRef.Bone(owner.boneName, 0u, 0u, string.Empty);
        }

        private static int KeyCountOf(CopiedTrack track)
        {
            return track.transformKeys.Count + track.boneKeys.Count + track.spriteKeys.Count;
        }

        private static TItem GetAt<TItem>(List<TItem> list, int index) where TItem : class
        {
            if (list == null || index < 0 || index >= list.Count)
            {
                return null;
            }
            return list[index];
        }

        private static bool HasKeyAt<TItem>(List<TItem> list, int index)
        {
            return list != null && index >= 0 && index < list.Count;
        }
    }
}
