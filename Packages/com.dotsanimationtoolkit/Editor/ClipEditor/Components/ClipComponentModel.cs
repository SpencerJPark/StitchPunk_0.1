// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// What components an object has, what it could have, and what adding or removing one does to
    /// the asset. Pure over the assets, with no window state and no undo — the caller records undo
    /// on the right object and marks it dirty. Presence is derived, never stored.
    /// </summary>
    public static class ClipComponentModel
    {
        /// <summary>Kinds in the order the inspector stacks them, which is the order added here.</summary>
        private static readonly ClipComponentKind[] stackOrder =
        {
            ClipComponentKind.Transform,
            ClipComponentKind.BoneTransform,
            ClipComponentKind.Flipbook,
            ClipComponentKind.Billboard,
            ClipComponentKind.Socket,
            ClipComponentKind.Ragdoll
        };

        /// <summary>The add-ons, in stack order — everything the Add Component menu offers.</summary>
        private static readonly ClipComponentKind[] addableKinds =
        {
            ClipComponentKind.Flipbook,
            ClipComponentKind.Billboard,
            ClipComponentKind.Socket,
            ClipComponentKind.Ragdoll
        };

        private static readonly List<ClipComponentInstance> presenceScratch =
            new List<ClipComponentInstance>();

        /// <summary>Every kind, in stack order, intrinsic ones included.</summary>
        public static IReadOnlyList<ClipComponentKind> AllKinds
        {
            get { return stackOrder; }
        }

        // The kinds Add Component offers, which is every kind that is not intrinsic — the transform
        // kinds are on every object already, so there is nothing to add.
        public static IReadOnlyList<ClipComponentKind> AddableKinds
        {
            get { return addableKinds; }
        }

        public static string DisplayName(ClipComponentKind kind)
        {
            switch (kind)
            {
                case ClipComponentKind.Transform: return "Transform";
                case ClipComponentKind.BoneTransform: return "Bone Transform";
                case ClipComponentKind.Flipbook: return "Flipbook";
                case ClipComponentKind.Billboard: return "Billboard";
                case ClipComponentKind.Ragdoll: return "Ragdoll";
                default: return "Socket";
            }
        }

        /// <summary>What a kind does, in the words the picker shows when a row is hovered.</summary>
        public static string Describe(ClipComponentKind kind)
        {
            switch (kind)
            {
                case ClipComponentKind.Transform:
                    return "Where this part is, turned and scaled, at each key. Every object has "
                        + "one; keying it here writes the part's transform track.";

                case ClipComponentKind.BoneTransform:
                    return "Where this node sits in its parent's space at each key. Every object "
                        + "has one; on a skinned bone it moves the mesh.";

                case ClipComponentKind.Flipbook:
                    return "Swaps which sprite frame this part shows, keyed as a frame index. A "
                        + "part can carry several — a mouth and a pair of eyes drawn from the same "
                        + "texture array, each with its own base frame.";

                case ClipComponentKind.Billboard:
                    return "Turns this node to face the viewer, and its children with it. Stored on "
                        + "the rig, so it holds in every clip; the keys here animate how much.";

                case ClipComponentKind.Ragdoll:
                    return "A box collider this node falls with once the rig's ragdoll drops it — "
                        + "sized and placed in the viewport, and welded to whatever body is nearest "
                        + "above it. Works on a guiding part or a skinned bone alike, and holds for "
                        + "every clip since it is stored on the rig.";

                default:
                    return "An attachment point that follows this object — a hand's grip, a muzzle, "
                        + "a hat's peg. Stored on the rig, so anything spawned onto it finds it in "
                        + "every clip.";
            }
        }

        // Which asset a kind is stored on, and therefore how far an edit to it reaches. Billboard is
        // rig-scoped because the component is the billboard root; Ragdoll is rig-scoped because its
        // fields are not animatable at all, so there is no clip-side half to distinguish.
        public static ClipComponentScope Scope(ClipComponentKind kind)
        {
            return kind == ClipComponentKind.Socket || kind == ClipComponentKind.Billboard
                || kind == ClipComponentKind.Ragdoll
                ? ClipComponentScope.Rig
                : ClipComponentScope.Clip;
        }

        /// <summary>Whether a kind is on every object it applies to, rather than being added to it.</summary>
        public static bool IsIntrinsic(ClipComponentKind kind)
        {
            return kind == ClipComponentKind.Transform || kind == ClipComponentKind.BoneTransform;
        }

        // Which of the two transform kinds an object carries, decided by whether the rig declares a
        // part for it: a part is posed through a transform track bound to its id, everything else
        // through a bone track bound to its name, matching how the bake reads them.
        public static ClipComponentKind TransformKindFor(ClipObjectRef objectRef)
        {
            return objectRef.HasRigTarget
                ? ClipComponentKind.Transform
                : ClipComponentKind.BoneTransform;
        }

        // Whether a kind binds by rig-target id, and so needs the object to be a declared part.
        // Adding such a component on an undeclared node mints the part instead of refusing — see PromoteToRigTarget.
        public static bool RequiresRigTarget(ClipComponentKind kind)
        {
            return kind == ClipComponentKind.Transform || kind == ClipComponentKind.Flipbook;
        }

        /// <summary>Whether an object can carry more than one of a kind. True for Flipbook and Socket only.</summary>
        public static bool AllowsMultiple(ClipComponentKind kind)
        {
            return kind == ClipComponentKind.Flipbook || kind == ClipComponentKind.Socket;
        }

        // Whether a kind is offerable on an object at all, and why not when it is not — a statement
        // about kinds, not about the current state (no clip open, etc.), which CanAdd answers
        // instead. Only the transform kinds can answer no, each about the other one.
        public static bool AppliesTo(
            ClipComponentKind kind, ClipObjectRef objectRef, out string unavailableReason)
        {
            unavailableReason = string.Empty;
            if (!IsIntrinsic(kind))
            {
                return true;
            }

            ClipComponentKind transformKind = TransformKindFor(objectRef);
            if (kind == transformKind)
            {
                return true;
            }

            unavailableReason = kind == ClipComponentKind.Transform
                ? "The rig declares no part for this node, so its poses are keyed on a bone track. "
                    + "Adding a Flipbook makes it a part."
                : "This object is a part the rig declares, so its poses are keyed on its transform "
                    + "track rather than by bone name.";
            return false;
        }

        // The components this object has, in stack order, its transform always first. Cleared and
        // refilled rather than returning a new list, since the inspector rebuilds this on every edit.
        public static void CollectInstances(
            ClipAsset clip, RigAsset rig, ClipObjectRef objectRef,
            List<ClipComponentInstance> instances)
        {
            if (instances == null)
            {
                return;
            }
            instances.Clear();
            if (!objectRef.IsValid)
            {
                return;
            }

            ClipComponentKind primaryTransform = TransformKindFor(objectRef);

            for (int orderIndex = 0; orderIndex < stackOrder.Length; orderIndex++)
            {
                ClipComponentKind kind = stackOrder[orderIndex];
                int countBefore = instances.Count;
                CollectInstancesOfKindInto(clip, rig, objectRef, kind, instances);

                if (!IsIntrinsic(kind))
                {
                    continue;
                }

                // The object's own transform kind is present whether or not its track is yet —
                // "not keyed" is a real state, not a reason to leave it out of the stack.
                if (kind == primaryTransform && instances.Count == countBefore)
                {
                    instances.Add(
                        new ClipComponentInstance(kind, ClipComponentInstance.NoTrackIndex));
                }
            }
        }

        /// <summary>Whether a component is the object's own transform, as opposed to one left behind.</summary>
        public static bool IsPrimaryTransform(ClipComponentKind kind, ClipObjectRef objectRef)
        {
            return IsIntrinsic(kind) && kind == TransformKindFor(objectRef);
        }

        // Whether the object already carries at least one of a kind. Answered through the shared
        // scratch list rather than a fresh one, since the editor is single-threaded.
        public static bool HasAny(
            ClipAsset clip, RigAsset rig, ClipObjectRef objectRef, ClipComponentKind kind)
        {
            presenceScratch.Clear();
            CollectInstancesOfKindInto(clip, rig, objectRef, kind, presenceScratch);
            return presenceScratch.Count > 0;
        }

        // Whether Add Component should offer a kind, given what the object already has. Everything
        // refused here is refused by circumstance rather than by kind, unlike AppliesTo.
        public static bool CanAdd(
            ClipAsset clip, RigAsset rig, ClipObjectRef objectRef, ClipComponentKind kind,
            out string unavailableReason)
        {
            if (!AppliesTo(kind, objectRef, out unavailableReason))
            {
                return false;
            }
            if (Scope(kind) == ClipComponentScope.Clip && clip == null)
            {
                unavailableReason = "Select a clip first — this component is stored on the clip.";
                return false;
            }
            if (Scope(kind) == ClipComponentScope.Rig && rig == null)
            {
                unavailableReason = "This clip set's Rig field is empty, and this component is "
                    + "stored on the rig. Pick a RigAsset above the hierarchy, or build "
                    + "one in the Rigs tab, to give this component somewhere to live.";
                return false;
            }

            // A part-bound component on an unclaimed node mints the part, and the part is a row on
            // the rig. So the rig has to be there even for a kind the clip stores.
            if (RequiresRigTarget(kind) && !objectRef.HasRigTarget && rig == null)
            {
                unavailableReason = "This clip set's Rig field is empty, so there is nothing to "
                    + "declare this node a part on. Assign a RigAsset there in the Inspector.";
                return false;
            }
            if (RequiresRigTarget(kind) && !objectRef.HasRigTarget
                && objectRef.kind != ClipObjectKind.Bone)
            {
                unavailableReason = "This row is not a node the rig can declare a part for.";
                return false;
            }

            if (!AllowsMultiple(kind) && HasAny(clip, rig, objectRef, kind))
            {
                unavailableReason = DisplayName(kind) + " is already on this object.";
                return false;
            }
            return true;
        }

        // How many keys a component holds — what makes removing it destructive. A socket answers 0
        // because it has no keys at all, not because it is empty; its removal is confirmed on other grounds.
        public static int KeyCount(
            ClipAsset clip, ClipObjectRef objectRef, ClipComponentInstance instance)
        {
            switch (instance.kind)
            {
                case ClipComponentKind.Transform:
                {
                    TransformTrack track = GetAt(clip == null ? null : clip.transformTracks, instance.index);
                    return track == null || track.keys == null ? 0 : track.keys.Count;
                }
                case ClipComponentKind.BoneTransform:
                {
                    BoneTrack track = GetAt(clip == null ? null : clip.boneTracks, instance.index);
                    return track == null || track.keys == null ? 0 : track.keys.Count;
                }
                case ClipComponentKind.Flipbook:
                {
                    SpriteTrack track = GetAt(clip == null ? null : clip.spriteTracks, instance.index);
                    return track == null || track.keys == null ? 0 : track.keys.Count;
                }
                case ClipComponentKind.Billboard:
                {
                    // Summed over the root's tracks rather than read from one: the instance
                    // addresses the rig's root list, and the keys are in the clip.
                    if (clip == null || clip.billboardTracks == null
                        || objectRef.billboardRootId == 0u)
                    {
                        return 0;
                    }
                    int billboardKeyCount = 0;
                    for (int trackIndex = 0; trackIndex < clip.billboardTracks.Count; trackIndex++)
                    {
                        BillboardTrack track = clip.billboardTracks[trackIndex];
                        if (track == null || track.keys == null
                            || track.rootStableId != objectRef.billboardRootId)
                        {
                            continue;
                        }
                        billboardKeyCount += track.keys.Count;
                    }
                    return billboardKeyCount;
                }
                default:
                    return 0;
            }
        }

        /// <summary>
        /// Declares a rig target for a previewed node, so part-bound components have an id to bind
        /// to. An existing target is adopted before a new one is minted, and any bone track the node
        /// already had is carried across to the new transform track rather than dropped.
        /// </summary>
        /// <returns>The definition the node is now bound to, or null when it could not be made.</returns>
        public static RigTargetDefinition PromoteToRigTarget(
            ClipAsset clip, RigAsset rig, ClipObjectRef objectRef)
        {
            if (rig == null || objectRef.kind != ClipObjectKind.Bone || !objectRef.IsValid)
            {
                return null;
            }
            if (rig.targets == null)
            {
                rig.targets = new List<RigTargetDefinition>();
            }

            RigTargetDefinition existing = FindTargetForNode(rig, objectRef);
            if (existing != null)
            {
                // Stamping the path is what makes the adoption stick: next time the window builds
                // the object it resolves this target by path rather than by the name coincidence
                // that found it here. An empty path is not stamped — a caller that never had a
                // previewed hierarchy to read one against would otherwise erase the binding a
                // caller that did had already recorded.
                if (!string.IsNullOrEmpty(objectRef.nodePath))
                {
                    existing.sourceNodePath = objectRef.nodePath;
                }
                MigrateBoneTrackToTransform(clip, objectRef.boneName, existing);
                return existing;
            }

            RigTargetDefinition minted = new RigTargetDefinition();
            minted.displayName = objectRef.boneName;
            minted.sourceNodePath = objectRef.nodePath;
            rig.targets.Add(minted);
            rig.EnsureStableIds();
            MigrateBoneTrackToTransform(clip, objectRef.boneName, minted);
            return minted;
        }

        // The target already standing for a node — by recorded path first, then by name. The name
        // fallback is consulted only when promoting, never when reading the stack, so an accidental
        // name collision cannot silently rebind a part that is working.
        public static RigTargetDefinition FindTargetForNode(RigAsset rig, ClipObjectRef objectRef)
        {
            if (rig == null || rig.targets == null)
            {
                return null;
            }

            if (!string.IsNullOrEmpty(objectRef.nodePath))
            {
                for (int targetIndex = 0; targetIndex < rig.targets.Count; targetIndex++)
                {
                    RigTargetDefinition target = rig.targets[targetIndex];
                    if (target != null
                        && string.Equals(
                            target.sourceNodePath, objectRef.nodePath, StringComparison.Ordinal))
                    {
                        return target;
                    }
                }
            }

            if (string.IsNullOrEmpty(objectRef.boneName))
            {
                return null;
            }
            for (int targetIndex = 0; targetIndex < rig.targets.Count; targetIndex++)
            {
                RigTargetDefinition target = rig.targets[targetIndex];
                if (target != null
                    && string.IsNullOrEmpty(target.sourceNodePath)
                    && string.Equals(target.displayName, objectRef.boneName, StringComparison.Ordinal))
                {
                    return target;
                }
            }
            return null;
        }

        // The target claiming a node's path, or 0 when none does. By path only, unlike
        // FindTargetForNode: this runs on every hierarchy rebuild, so it must not match by name.
        public static uint ResolveTargetIdForNode(RigAsset rig, string nodePath)
        {
            if (rig == null || rig.targets == null || string.IsNullOrEmpty(nodePath))
            {
                return 0u;
            }
            for (int targetIndex = 0; targetIndex < rig.targets.Count; targetIndex++)
            {
                RigTargetDefinition target = rig.targets[targetIndex];
                if (target != null
                    && string.Equals(target.sourceNodePath, nodePath, StringComparison.Ordinal))
                {
                    return target.Id.Value;
                }
            }
            return 0u;
        }

        // The tag a newly created track binds by: the target's own tag when it carries one, 0 (bind
        // by target id) when it does not. Creation default only: no existing track is rewritten.
        public static uint ResolveNewTrackTagId(RigAsset rig, uint targetId)
        {
            if (rig == null || rig.targets == null || targetId == 0u)
            {
                return 0u;
            }
            for (int targetIndex = 0; targetIndex < rig.targets.Count; targetIndex++)
            {
                RigTargetDefinition target = rig.targets[targetIndex];
                if (target != null && target.stableId == targetId)
                {
                    return target.tagId;
                }
            }
            return 0u;
        }

        /// <summary>
        /// Guarantees the target wears a tag, reusing the registry entry named like the part when
        /// nothing else on this rig wears it, minting <c>Name 2</c>, <c>Name 3</c>… otherwise.
        /// Returns the tag id, or 0 when the target does not exist on this rig.
        /// </summary>
        // The registry is a parameter, never fetched from VocabularyRegistryProvider here: EditMode
        // tests drive this with an in-memory registry, and a provider call would mint real ones.
        public static uint EnsureTargetTagged(
            RigAsset rig, uint targetId, IVocabularyRegistry tagRegistry,
            out bool createdRegistryEntry)
        {
            createdRegistryEntry = false;
            if (rig == null || rig.targets == null || targetId == 0u || tagRegistry == null)
            {
                return 0u;
            }

            RigTargetDefinition target = null;
            for (int targetIndex = 0; targetIndex < rig.targets.Count; targetIndex++)
            {
                RigTargetDefinition candidate = rig.targets[targetIndex];
                if (candidate != null && candidate.stableId == targetId)
                {
                    target = candidate;
                    break;
                }
            }
            if (target == null)
            {
                return 0u;
            }
            if (target.tagId != 0u)
            {
                return target.tagId;
            }

            string baseName = string.IsNullOrEmpty(target.displayName)
                ? "Part"
                : target.displayName.Trim();
            for (int suffix = 1; ; suffix++)
            {
                string candidateName = suffix == 1 ? baseName : baseName + " " + suffix.ToString();
                uint existingTagId = FindVocabularyIdByName(tagRegistry, candidateName);
                if (existingTagId != 0u)
                {
                    // Reuse only when no other part on this rig already wears it.
                    if (FindTargetByTag(rig, existingTagId) == null)
                    {
                        target.tagId = existingTagId;
                        return existingTagId;
                    }
                    continue;
                }
                uint mintedTagId = tagRegistry.CreateVocabularyEntry(candidateName);
                createdRegistryEntry = true;
                target.tagId = mintedTagId;
                return mintedTagId;
            }
        }

        /// <summary>The registry entry whose name matches case-insensitively, or 0.</summary>
        public static uint FindVocabularyIdByName(IVocabularyRegistry registry, string entryName)
        {
            if (registry == null || string.IsNullOrEmpty(entryName))
            {
                return 0u;
            }
            string trimmedName = entryName.Trim();
            int entryCount = registry.VocabularyEntryCount;
            for (int entryIndex = 0; entryIndex < entryCount; entryIndex++)
            {
                string existingName = registry.VocabularyEntryName(entryIndex);
                if (existingName != null && string.Equals(
                        existingName.Trim(), trimmedName, StringComparison.OrdinalIgnoreCase))
                {
                    return registry.VocabularyEntryId(entryIndex);
                }
            }
            return 0u;
        }

        // Moves every key of source into destination and unions the animated channels — the data
        // half of "move this row to that tag". The caller deletes the source track and owns undo.
        // On a same-time collision the source key wins: the gesture was "put these keys there".
        public static void MergeTransformTracks(TransformTrack source, TransformTrack destination)
        {
            if (source == null || destination == null || source == destination)
            {
                return;
            }
            destination.channels |= source.channels;
            if (source.keys == null || source.keys.Count == 0)
            {
                return;
            }
            if (destination.keys == null)
            {
                destination.keys = new List<TransformKey>();
            }
            for (int sourceIndex = 0; sourceIndex < source.keys.Count; sourceIndex++)
            {
                TransformKey incomingKey = source.keys[sourceIndex];
                for (int destinationIndex = destination.keys.Count - 1; destinationIndex >= 0; destinationIndex--)
                {
                    if (Math.Abs(destination.keys[destinationIndex].normalizedTime - incomingKey.normalizedTime)
                        <= ClipTransformEditing.KeyTimeTolerance)
                    {
                        destination.keys.RemoveAt(destinationIndex);
                    }
                }
                destination.keys.Add(incomingKey);
            }
            destination.keys.Sort(
                (TransformKey firstKey, TransformKey secondKey) =>
                    firstKey.normalizedTime.CompareTo(secondKey.normalizedTime));
        }

        /// <summary>
        /// Whether two flipbook tracks can merge losslessly: a sprite key's stored number only means
        /// something beside its track's mode, slice space and base index.
        /// </summary>
        public static bool SpriteTracksMergeCompatible(SpriteTrack first, SpriteTrack second)
        {
            return first != null && second != null
                && first.mode == second.mode
                && first.sliceSpace == second.sliceSpace
                && first.baseIndex == second.baseIndex;
        }

        /// <summary>Flipbook counterpart of <see cref="MergeTransformTracks"/>; check
        /// <see cref="SpriteTracksMergeCompatible"/> first.</summary>
        public static void MergeSpriteTracks(SpriteTrack source, SpriteTrack destination)
        {
            if (source == null || destination == null || source == destination
                || source.keys == null || source.keys.Count == 0)
            {
                return;
            }
            if (destination.keys == null)
            {
                destination.keys = new List<SpriteKey>();
            }
            for (int sourceIndex = 0; sourceIndex < source.keys.Count; sourceIndex++)
            {
                SpriteKey incomingKey = source.keys[sourceIndex];
                for (int destinationIndex = destination.keys.Count - 1; destinationIndex >= 0; destinationIndex--)
                {
                    if (Math.Abs(destination.keys[destinationIndex].normalizedTime - incomingKey.normalizedTime)
                        <= ClipTransformEditing.KeyTimeTolerance)
                    {
                        destination.keys.RemoveAt(destinationIndex);
                    }
                }
                destination.keys.Add(incomingKey);
            }
            destination.keys.Sort(
                (SpriteKey firstKey, SpriteKey secondKey) =>
                    firstKey.normalizedTime.CompareTo(secondKey.normalizedTime));
        }

        // What MoveTracksToTag did to one clip, for the caller to report. Counted rather than
        // logged, since the caller sweeps a whole clip set and only the total matters.
        public struct TagMoveOutcome
        {
            /// <summary>Rows that simply took the new tag, their keys untouched.</summary>
            public int movedTrackCount;

            /// <summary>Rows folded into a row the clip already had on the destination tag.</summary>
            public int mergedTrackCount;

            /// <summary>
            /// Flipbook rows left where they were because the destination row's frame settings
            /// differ — a sprite key's number means nothing under another track's mode or base.
            /// </summary>
            public int refusedTrackCount;

            public int ChangedTrackCount
            {
                get { return movedTrackCount + mergedTrackCount; }
            }
        }

        // Re-tags every row in one clip keyed against fromTagId so its keys belong to toTagId
        // instead. A row's tag is its identity, so where the clip already has a row on the
        // destination tag, the moving row is merged into it instead of creating a duplicate.
        // The caller owns undo/dirtying, and must treat any stored track index as invalid
        // afterwards when TagMoveOutcome.mergedTrackCount is non-zero.
        public static TagMoveOutcome MoveTracksToTag(ClipAsset clip, uint fromTagId, uint toTagId)
        {
            TagMoveOutcome outcome = new TagMoveOutcome();
            if (clip == null || fromTagId == 0u || toTagId == 0u || fromTagId == toTagId)
            {
                return outcome;
            }

            List<TransformTrack> transformTracks = clip.transformTracks;
            // Backwards, because a merge removes the track being visited.
            for (int trackIndex = transformTracks != null ? transformTracks.Count - 1 : -1;
                trackIndex >= 0; trackIndex--)
            {
                TransformTrack track = transformTracks[trackIndex];
                if (track == null || track.tagId != fromTagId)
                {
                    continue;
                }
                int destinationIndex = FindTransformTrackIndexByTag(
                    transformTracks, toTagId, trackIndex);
                if (destinationIndex < 0)
                {
                    track.tagId = toTagId;
                    outcome.movedTrackCount++;
                    continue;
                }
                MergeTransformTracks(track, transformTracks[destinationIndex]);
                transformTracks.RemoveAt(trackIndex);
                outcome.mergedTrackCount++;
            }

            List<SpriteTrack> spriteTracks = clip.spriteTracks;
            for (int trackIndex = spriteTracks != null ? spriteTracks.Count - 1 : -1;
                trackIndex >= 0; trackIndex--)
            {
                SpriteTrack track = spriteTracks[trackIndex];
                if (track == null || track.tagId != fromTagId)
                {
                    continue;
                }
                int destinationIndex = FindSpriteTrackIndexByTag(spriteTracks, toTagId, trackIndex);
                if (destinationIndex < 0)
                {
                    track.tagId = toTagId;
                    outcome.movedTrackCount++;
                    continue;
                }
                if (!SpriteTracksMergeCompatible(track, spriteTracks[destinationIndex]))
                {
                    outcome.refusedTrackCount++;
                    continue;
                }
                MergeSpriteTracks(track, spriteTracks[destinationIndex]);
                spriteTracks.RemoveAt(trackIndex);
                outcome.mergedTrackCount++;
            }

            return outcome;
        }

        // Whether any transform or flipbook row in this clip is keyed against tagId — for a caller
        // sweeping a clip set to skip clips a retag will not touch before paying for undo.
        public static bool ClipHasTrackTagged(ClipAsset clip, uint tagId)
        {
            if (clip == null || tagId == 0u)
            {
                return false;
            }
            return FindTransformTrackIndexByTag(clip.transformTracks, tagId, -1) >= 0
                || FindSpriteTrackIndexByTag(clip.spriteTracks, tagId, -1) >= 0;
        }

        private static int FindTransformTrackIndexByTag(
            List<TransformTrack> tracks, uint tagId, int excludedIndex)
        {
            for (int trackIndex = 0; tracks != null && trackIndex < tracks.Count; trackIndex++)
            {
                if (trackIndex != excludedIndex && tracks[trackIndex] != null
                    && tracks[trackIndex].tagId == tagId)
                {
                    return trackIndex;
                }
            }
            return -1;
        }

        private static int FindSpriteTrackIndexByTag(
            List<SpriteTrack> tracks, uint tagId, int excludedIndex)
        {
            for (int trackIndex = 0; tracks != null && trackIndex < tracks.Count; trackIndex++)
            {
                if (trackIndex != excludedIndex && tracks[trackIndex] != null
                    && tracks[trackIndex].tagId == tagId)
                {
                    return trackIndex;
                }
            }
            return -1;
        }

        /// <summary>
        /// Creates the track or socket a kind stands for, bound to the object. A part-bound kind on
        /// an unclaimed node promotes it first, so the object this binds against may be a part that
        /// did not exist when the call was made.
        /// </summary>
        /// <returns>The instance created, or an index of −1 when nothing could be added.</returns>
        public static ClipComponentInstance Add(
            ClipAsset clip, RigAsset rig, ClipObjectRef objectRef, ClipComponentKind kind,
            string newComponentName)
        {
            string unavailableReason;
            if (!CanAdd(clip, rig, objectRef, kind, out unavailableReason))
            {
                return new ClipComponentInstance(kind, ClipComponentInstance.NoTrackIndex);
            }

            uint targetId = objectRef.targetId;
            if (RequiresRigTarget(kind) && targetId == 0u)
            {
                RigTargetDefinition promoted = PromoteToRigTarget(clip, rig, objectRef);
                if (promoted == null)
                {
                    return new ClipComponentInstance(kind, ClipComponentInstance.NoTrackIndex);
                }
                targetId = promoted.Id.Value;
            }

            switch (kind)
            {
                case ClipComponentKind.Transform:
                {
                    EnsureList(ref clip.transformTracks);
                    TransformTrack track = new TransformTrack();
                    track.targetId = targetId;
                    track.tagId = ResolveNewTrackTagId(rig, targetId);
                    clip.transformTracks.Add(track);
                    return new ClipComponentInstance(kind, clip.transformTracks.Count - 1);
                }
                case ClipComponentKind.BoneTransform:
                {
                    EnsureList(ref clip.boneTracks);
                    BoneTrack track = new BoneTrack();
                    track.boneName = objectRef.boneName;
                    clip.boneTracks.Add(track);
                    return new ClipComponentInstance(kind, clip.boneTracks.Count - 1);
                }
                case ClipComponentKind.Flipbook:
                {
                    EnsureList(ref clip.spriteTracks);
                    SpriteTrack track = new SpriteTrack();
                    track.targetId = targetId;
                    track.tagId = ResolveNewTrackTagId(rig, targetId);
                    clip.spriteTracks.Add(track);
                    return new ClipComponentInstance(kind, clip.spriteTracks.Count - 1);
                }
                case ClipComponentKind.Billboard:
                {
                    if (rig.billboardRoots == null)
                    {
                        rig.billboardRoots = new List<BillboardRootDefinition>();
                    }
                    BillboardRootDefinition definition = new BillboardRootDefinition();
                    definition.displayName = newComponentName;
                    definition.address = objectRef.billboardAddress;
                    rig.billboardRoots.Add(definition);

                    // The caller mints the id, as it does for a socket: a root saved with id 0 is
                    // one no billboard track could ever address.
                    return new ClipComponentInstance(kind, rig.billboardRoots.Count - 1);
                }
                case ClipComponentKind.Ragdoll:
                {
                    if (rig.ragdollBodies == null)
                    {
                        rig.ragdollBodies = new List<RagdollBodyDefinition>();
                    }
                    RagdollBodyDefinition body = new RagdollBodyDefinition();
                    body.displayName = newComponentName;
                    body.address = objectRef.ragdollAddress;
                    rig.ragdollBodies.Add(body);

                    // Box, mass, damping and limits are left at the definition's own field
                    // initializers; the window sizes the box from the node's renderer afterwards
                    // when it has one, since the model has no viewport to measure against.
                    return new ClipComponentInstance(kind, rig.ragdollBodies.Count - 1);
                }
                default:
                {
                    if (rig.sockets == null)
                    {
                        rig.sockets = new List<SocketDefinition>();
                    }
                    SocketDefinition socket = new SocketDefinition();
                    socket.displayName = newComponentName;
                    if (objectRef.kind == ClipObjectKind.RigTarget)
                    {
                        socket.mode = SocketAttachMode.RigTarget;
                        socket.targetId = objectRef.targetId;
                    }
                    else
                    {
                        socket.mode = SocketAttachMode.Bone;
                        socket.boneName = objectRef.boneName;
                    }
                    rig.sockets.Add(socket);
                    return new ClipComponentInstance(kind, rig.sockets.Count - 1);
                }
            }
        }

        /// <summary>Deletes the track or socket a component stands for.</summary>
        /// <returns>Whether anything was removed.</returns>
        public static bool Remove(ClipAsset clip, RigAsset rig, ClipComponentInstance instance)
        {
            switch (instance.kind)
            {
                case ClipComponentKind.Transform:
                    return RemoveAt(clip == null ? null : clip.transformTracks, instance.index);
                case ClipComponentKind.BoneTransform:
                    return RemoveAt(clip == null ? null : clip.boneTracks, instance.index);
                case ClipComponentKind.Flipbook:
                    return RemoveAt(clip == null ? null : clip.spriteTracks, instance.index);
                case ClipComponentKind.Billboard:
                {
                    // The tracks go with the root: a track bound to a root the rig no longer
                    // declares animates nothing, so leaving one behind would be silently broken.
                    BillboardRootDefinition definition =
                        GetAt(rig == null ? null : rig.billboardRoots, instance.index);
                    if (definition == null)
                    {
                        return false;
                    }
                    uint rootStableId = definition.Id.Value;
                    if (clip != null && clip.billboardTracks != null)
                    {
                        for (int trackIndex = clip.billboardTracks.Count - 1; trackIndex >= 0; trackIndex--)
                        {
                            BillboardTrack track = clip.billboardTracks[trackIndex];
                            if (track != null && track.rootStableId == rootStableId)
                            {
                                clip.billboardTracks.RemoveAt(trackIndex);
                            }
                        }
                    }
                    return RemoveAt(rig.billboardRoots, instance.index);
                }
                case ClipComponentKind.Ragdoll:
                    // No clip-side data to take with it — a ragdoll body carries no keys at all, so
                    // unlike Billboard there is nothing here for the caller to warn about.
                    return RemoveAt(rig == null ? null : rig.ragdollBodies, instance.index);
                default:
                    return RemoveAt(rig == null ? null : rig.sockets, instance.index);
            }
        }

        // Moves a node's bone keys onto a transform track bound to the part it just became, then
        // deletes the bone track: two tracks posing the same node is not a merge the bake can make sense of.
        private static void MigrateBoneTrackToTransform(
            ClipAsset clip, string boneName, RigTargetDefinition target)
        {
            if (clip == null || clip.boneTracks == null || target == null
                || string.IsNullOrEmpty(boneName))
            {
                return;
            }

            int boneTrackIndex = -1;
            for (int trackIndex = 0; trackIndex < clip.boneTracks.Count; trackIndex++)
            {
                BoneTrack candidate = clip.boneTracks[trackIndex];
                if (candidate != null
                    && string.Equals(candidate.boneName, boneName, StringComparison.Ordinal))
                {
                    boneTrackIndex = trackIndex;
                    break;
                }
            }
            if (boneTrackIndex < 0)
            {
                return;
            }

            BoneTrack boneTrack = clip.boneTracks[boneTrackIndex];
            uint targetId = target.Id.Value;
            if (targetId == 0u)
            {
                return;
            }
            if (boneTrack.keys == null || boneTrack.keys.Count == 0)
            {
                // An empty track is not poses, it is a leftover binding. The node is posed as a
                // part from here on, so nothing is lost by dropping it.
                clip.boneTracks.RemoveAt(boneTrackIndex);
                return;
            }

            EnsureList(ref clip.transformTracks);
            TransformTrack transformTrack = null;
            for (int trackIndex = 0; trackIndex < clip.transformTracks.Count; trackIndex++)
            {
                TransformTrack candidate = clip.transformTracks[trackIndex];
                if (candidate != null && candidate.targetId == targetId)
                {
                    transformTrack = candidate;
                    break;
                }
            }

            // Refused onto a part that is already keyed. Those poses were authored against this
            // part, and appending a second object's keys to them would interleave two animations
            // into one unreadable track. The bone track is then left exactly where it is: deleting
            // authored keys to tidy up a binding is not a trade this is entitled to make, and the
            // stack shows the stranded track so it can be dealt with deliberately.
            if (transformTrack != null && transformTrack.keys != null
                && transformTrack.keys.Count > 0)
            {
                return;
            }

            if (transformTrack == null)
            {
                transformTrack = new TransformTrack();
                transformTrack.targetId = targetId;
                clip.transformTracks.Add(transformTrack);
            }

            transformTrack.keys = new List<TransformKey>(boneTrack.keys.Count);
            for (int keyIndex = 0; keyIndex < boneTrack.keys.Count; keyIndex++)
            {
                transformTrack.keys.Add(ClipKeyConversion.ToTransformKey(boneTrack.keys[keyIndex]));
            }
            clip.boneTracks.RemoveAt(boneTrackIndex);
        }

        // The instances of one kind on one object, in the order the owning list holds them. Unlike
        // CollectInstances, this never invents the placeholder an unkeyed intrinsic component
        // carries: the question here is what exists, not what the panel shows.
        public static void CollectInstancesOfKind(
            ClipAsset clip, RigAsset rig, ClipObjectRef objectRef, ClipComponentKind kind,
            List<ClipComponentInstance> instances)
        {
            if (instances == null)
            {
                return;
            }
            instances.Clear();
            CollectInstancesOfKindInto(clip, rig, objectRef, kind, instances);
        }

        private static void CollectInstancesOfKindInto(
            ClipAsset clip, RigAsset rig, ClipObjectRef objectRef, ClipComponentKind kind,
            List<ClipComponentInstance> instances)
        {
            switch (kind)
            {
                case ClipComponentKind.Transform:
                {
                    if (clip == null || clip.transformTracks == null || objectRef.targetId == 0u)
                    {
                        return;
                    }
                    for (int trackIndex = 0; trackIndex < clip.transformTracks.Count; trackIndex++)
                    {
                        TransformTrack track = clip.transformTracks[trackIndex];
                        if (track != null
                            && TrackBindsTarget(track.targetId, track.tagId, objectRef.targetId, rig))
                        {
                            instances.Add(new ClipComponentInstance(kind, trackIndex));
                        }
                    }
                    return;
                }
                case ClipComponentKind.BoneTransform:
                {
                    // The empty name is refused rather than matched: a rig-target row has no node
                    // name, so matching a blank-named track to it would hang that track off every part.
                    if (clip == null || clip.boneTracks == null
                        || string.IsNullOrEmpty(objectRef.boneName))
                    {
                        return;
                    }
                    for (int trackIndex = 0; trackIndex < clip.boneTracks.Count; trackIndex++)
                    {
                        BoneTrack track = clip.boneTracks[trackIndex];
                        if (track != null
                            && string.Equals(track.boneName, objectRef.boneName, StringComparison.Ordinal))
                        {
                            instances.Add(new ClipComponentInstance(kind, trackIndex));
                        }
                    }
                    return;
                }
                case ClipComponentKind.Flipbook:
                {
                    if (clip == null || clip.spriteTracks == null || objectRef.targetId == 0u)
                    {
                        return;
                    }
                    for (int trackIndex = 0; trackIndex < clip.spriteTracks.Count; trackIndex++)
                    {
                        SpriteTrack track = clip.spriteTracks[trackIndex];
                        if (track != null
                            && TrackBindsTarget(track.targetId, track.tagId, objectRef.targetId, rig))
                        {
                            instances.Add(new ClipComponentInstance(kind, trackIndex));
                        }
                    }
                    return;
                }
                case ClipComponentKind.Billboard:
                {
                    // The component is the root the rig declares, so its index addresses the rig's
                    // root list. The clip's tracks hang off it and may not exist yet: a node can
                    // billboard without any clip animating how much.
                    if (rig == null || rig.billboardRoots == null || objectRef.billboardRootId == 0u)
                    {
                        return;
                    }
                    for (int rootIndex = 0; rootIndex < rig.billboardRoots.Count; rootIndex++)
                    {
                        BillboardRootDefinition definition = rig.billboardRoots[rootIndex];
                        if (definition != null && definition.Id.Value == objectRef.billboardRootId)
                        {
                            instances.Add(new ClipComponentInstance(kind, rootIndex));
                            return;
                        }
                    }
                    return;
                }
                case ClipComponentKind.Ragdoll:
                {
                    // The component is the body the rig declares, addressed exactly as a billboard
                    // root is: the window has already resolved which body (if any) welds to this
                    // node and carries its id on the reference, because only the window holds the
                    // previewed hierarchy the address is read against.
                    if (rig == null || rig.ragdollBodies == null || objectRef.ragdollBodyId == 0u)
                    {
                        return;
                    }
                    for (int bodyIndex = 0; bodyIndex < rig.ragdollBodies.Count; bodyIndex++)
                    {
                        RagdollBodyDefinition definition = rig.ragdollBodies[bodyIndex];
                        if (definition != null && definition.Id.Value == objectRef.ragdollBodyId)
                        {
                            instances.Add(new ClipComponentInstance(kind, bodyIndex));
                            return;
                        }
                    }
                    return;
                }
                default:
                {
                    if (rig == null || rig.sockets == null)
                    {
                        return;
                    }
                    for (int socketIndex = 0; socketIndex < rig.sockets.Count; socketIndex++)
                    {
                        SocketDefinition socket = rig.sockets[socketIndex];
                        if (socket == null || !FollowsObject(socket, objectRef))
                        {
                            continue;
                        }
                        instances.Add(new ClipComponentInstance(kind, socketIndex));
                    }
                    return;
                }
            }
        }

        /// <summary>
        /// Whether a <see cref="TransformTrack"/> or <see cref="SpriteTrack"/> animates the rig
        /// target <paramref name="objectTargetId"/> — directly by <paramref name="trackTargetId"/>,
        /// or through <paramref name="trackTagId"/> resolving to it.
        /// </summary>
        /// <param name="rig">
        /// The rig <paramref name="trackTagId"/> resolves against. Null (or a rig declaring no
        /// target with that tag) makes a tag-bound track match nothing, rather than falling back to
        /// matching by <paramref name="trackTargetId"/>.
        /// </param>
        public static bool TrackBindsTarget(
            uint trackTargetId, uint trackTagId, uint objectTargetId, RigAsset rig)
        {
            if (trackTagId != 0u)
            {
                RigTargetDefinition resolvedTarget = FindTargetByTag(rig, trackTagId);
                return resolvedTarget != null && resolvedTarget.stableId == objectTargetId;
            }
            return trackTargetId == objectTargetId;
        }

        /// <summary>
        /// The rig target carrying <paramref name="tagId"/>, or null when none does. A null rig, the
        /// reserved id 0, or a rig where the tag is not (yet) unique — rule T1 already makes that an
        /// authoring error, so this simply reports the first match — all fall through the loop and
        /// return null or the sole candidate respectively.
        /// </summary>
        public static RigTargetDefinition FindTargetByTag(RigAsset rig, uint tagId)
        {
            if (rig == null || rig.targets == null || tagId == 0u)
            {
                return null;
            }
            for (int targetIndex = 0; targetIndex < rig.targets.Count; targetIndex++)
            {
                RigTargetDefinition targetDefinition = rig.targets[targetIndex];
                if (targetDefinition != null && targetDefinition.tagId == tagId)
                {
                    return targetDefinition;
                }
            }
            return null;
        }

        /// <summary>Whether a socket hangs off this object — the object being its source.</summary>
        private static bool FollowsObject(SocketDefinition socket, ClipObjectRef objectRef)
        {
            if (objectRef.kind == ClipObjectKind.RigTarget)
            {
                return socket.mode == SocketAttachMode.RigTarget
                    && socket.targetId == objectRef.targetId;
            }
            return socket.mode == SocketAttachMode.Bone
                && string.Equals(socket.boneName, objectRef.boneName, StringComparison.Ordinal);
        }

        private static TItem GetAt<TItem>(List<TItem> list, int index) where TItem : class
        {
            if (list == null || index < 0 || index >= list.Count)
            {
                return null;
            }
            return list[index];
        }

        private static bool RemoveAt<TItem>(List<TItem> list, int index)
        {
            if (list == null || index < 0 || index >= list.Count)
            {
                return false;
            }
            list.RemoveAt(index);
            return true;
        }

        private static void EnsureList<TItem>(ref List<TItem> list)
        {
            if (list == null)
            {
                list = new List<TItem>();
            }
        }
    }
}
