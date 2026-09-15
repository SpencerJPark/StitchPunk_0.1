// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    public enum TrackBindingState : byte
    {
        Bound,
        Skipped,
        Dangling
    }

    public enum RetargetTrackKind : byte
    {
        Transform,
        Sprite,
        Bone,
        Billboard
    }

    public readonly struct TrackBinding
    {
        public readonly string trackName;
        public readonly uint tagId;
        public readonly TrackBindingState state;
        public readonly string targetDisplayName;
        public readonly RetargetTrackKind kind;
        public readonly int trackIndex;
        public readonly string reason;

        public TrackBinding(
            string trackName,
            uint tagId,
            TrackBindingState state,
            string targetDisplayName,
            RetargetTrackKind kind,
            int trackIndex,
            string reason)
        {
            this.trackName = trackName;
            this.tagId = tagId;
            this.state = state;
            this.targetDisplayName = targetDisplayName;
            this.kind = kind;
            this.trackIndex = trackIndex;
            this.reason = reason;
        }

        // Only tag-carrying track kinds can be remapped; bones bind by name, billboards by root.
        public bool CanRemapTag
        {
            get { return kind == RetargetTrackKind.Transform || kind == RetargetTrackKind.Sprite; }
        }
    }

    public readonly struct RosterCoverageEntry
    {
        public readonly RigAsset rig;
        public readonly int boundCount;
        public readonly int totalCount;

        public RosterCoverageEntry(RigAsset rig, int boundCount, int totalCount)
        {
            this.rig = rig;
            this.boundCount = boundCount;
            this.totalCount = totalCount;
        }
    }

    /// <summary>
    /// Which of a clip's tracks land on a rig, and why the rest do not. Agrees with the bind
    /// validator: an unknown tag is Dangling on every rig, a known tag the rig lacks is Skipped.
    /// </summary>
    public static class RetargetBindingResolver
    {
        public const string DanglingReason = "tag not in registry";

        public static List<TrackBinding> Resolve(ClipAsset clip, RigAsset rig, TargetTagRegistry registry)
        {
            return Resolve(clip, rig, registry, null);
        }

        // hierarchyContainsBoneName null falls back to a first-name search of the rig's source prefab,
        // the same first-match rule ClipPreviewController.FindHierarchyIndexByName uses.
        public static List<TrackBinding> Resolve(
            ClipAsset clip, RigAsset rig, TargetTagRegistry registry, Func<string, bool> hierarchyContainsBoneName)
        {
            List<TrackBinding> bindings = new List<TrackBinding>();
            if (clip == null)
            {
                return bindings;
            }

            if (clip.transformTracks != null)
            {
                for (int trackIndex = 0; trackIndex < clip.transformTracks.Count; trackIndex++)
                {
                    TransformTrack transformTrack = clip.transformTracks[trackIndex];
                    if (transformTrack != null)
                    {
                        bindings.Add(ResolveTaggedTrack(
                            RetargetTrackKind.Transform, trackIndex, transformTrack.targetId, transformTrack.tagId, rig, registry));
                    }
                }
            }

            if (clip.spriteTracks != null)
            {
                for (int trackIndex = 0; trackIndex < clip.spriteTracks.Count; trackIndex++)
                {
                    SpriteTrack spriteTrack = clip.spriteTracks[trackIndex];
                    if (spriteTrack != null)
                    {
                        bindings.Add(ResolveTaggedTrack(
                            RetargetTrackKind.Sprite, trackIndex, spriteTrack.targetId, spriteTrack.tagId, rig, registry));
                    }
                }
            }

            if (clip.boneTracks != null)
            {
                for (int trackIndex = 0; trackIndex < clip.boneTracks.Count; trackIndex++)
                {
                    BoneTrack boneTrack = clip.boneTracks[trackIndex];
                    if (boneTrack != null)
                    {
                        bindings.Add(ResolveBoneTrack(trackIndex, boneTrack, rig, hierarchyContainsBoneName));
                    }
                }
            }

            if (clip.billboardTracks != null)
            {
                for (int trackIndex = 0; trackIndex < clip.billboardTracks.Count; trackIndex++)
                {
                    BillboardTrack billboardTrack = clip.billboardTracks[trackIndex];
                    if (billboardTrack != null)
                    {
                        bindings.Add(ResolveBillboardTrack(trackIndex, billboardTrack, rig));
                    }
                }
            }

            return bindings;
        }

        public static int CountBound(List<TrackBinding> bindings)
        {
            if (bindings == null)
            {
                return 0;
            }

            int boundCount = 0;
            foreach (TrackBinding binding in bindings)
            {
                if (binding.state == TrackBindingState.Bound)
                {
                    boundCount++;
                }
            }

            return boundCount;
        }

        // firstRig (the tab's selected rig) is listed first when it is among the rigs, or prepended when not.
        public static List<RosterCoverageEntry> BuildRoster(
            ClipAsset clip, IReadOnlyList<RigAsset> rigs, RigAsset firstRig, TargetTagRegistry registry)
        {
            List<RigAsset> orderedRigs = new List<RigAsset>();
            if (firstRig != null)
            {
                orderedRigs.Add(firstRig);
            }

            if (rigs != null)
            {
                foreach (RigAsset rosterRig in rigs)
                {
                    if (rosterRig != null && !orderedRigs.Contains(rosterRig))
                    {
                        orderedRigs.Add(rosterRig);
                    }
                }
            }

            List<RosterCoverageEntry> entries = new List<RosterCoverageEntry>(orderedRigs.Count);
            foreach (RigAsset rosterRig in orderedRigs)
            {
                List<TrackBinding> bindings = Resolve(clip, rosterRig, registry);
                entries.Add(new RosterCoverageEntry(rosterRig, CountBound(bindings), bindings.Count));
            }

            return entries;
        }

        private static TrackBinding ResolveTaggedTrack(
            RetargetTrackKind kind, int trackIndex, uint targetId, uint tagId, RigAsset rig, TargetTagRegistry registry)
        {
            RigTargetDefinition matchedTarget = FindBoundTarget(targetId, tagId, rig);

            if (tagId == 0u)
            {
                string untaggedName = matchedTarget != null ? matchedTarget.displayName : "Untagged track";
                if (matchedTarget != null)
                {
                    return new TrackBinding(untaggedName, 0u, TrackBindingState.Bound, matchedTarget.displayName, kind, trackIndex, string.Empty);
                }

                return new TrackBinding(
                    untaggedName, 0u, TrackBindingState.Skipped, string.Empty, kind, trackIndex, DescribeMissingPart(rig));
            }

            // Mirrors the validator's V36: only a supplied registry can call a tag dangling.
            if (registry != null && !registry.ContainsId(tagId))
            {
                return new TrackBinding("Missing tag", tagId, TrackBindingState.Dangling, string.Empty, kind, trackIndex, DanglingReason);
            }

            string tagName = registry != null ? registry.FindName(tagId) : null;
            string trackName = string.IsNullOrEmpty(tagName) ? "Unnamed tag" : tagName;
            if (matchedTarget != null)
            {
                return new TrackBinding(trackName, tagId, TrackBindingState.Bound, matchedTarget.displayName, kind, trackIndex, string.Empty);
            }

            return new TrackBinding(trackName, tagId, TrackBindingState.Skipped, string.Empty, kind, trackIndex, DescribeMissingPart(rig));
        }

        private static RigTargetDefinition FindBoundTarget(uint targetId, uint tagId, RigAsset rig)
        {
            if (rig == null || rig.targets == null)
            {
                return null;
            }

            foreach (RigTargetDefinition targetDefinition in rig.targets)
            {
                if (TrackTargetMatchResolver.TrackBindsTarget(targetId, tagId, targetDefinition))
                {
                    return targetDefinition;
                }
            }

            return null;
        }

        private static TrackBinding ResolveBoneTrack(
            int trackIndex, BoneTrack boneTrack, RigAsset rig, Func<string, bool> hierarchyContainsBoneName)
        {
            string boneName = string.IsNullOrEmpty(boneTrack.boneName) ? "Unnamed bone" : boneTrack.boneName;
            bool boneFound = !string.IsNullOrEmpty(boneTrack.boneName)
                && (hierarchyContainsBoneName != null
                    ? hierarchyContainsBoneName(boneTrack.boneName)
                    : SourcePrefabContainsName(rig, boneTrack.boneName));
            if (boneFound)
            {
                return new TrackBinding(boneName, 0u, TrackBindingState.Bound, boneTrack.boneName, RetargetTrackKind.Bone, trackIndex, string.Empty);
            }

            string reason = rig == null ? "no rig picked" : "no bone with this name on " + rig.name;
            return new TrackBinding(boneName, 0u, TrackBindingState.Skipped, string.Empty, RetargetTrackKind.Bone, trackIndex, reason);
        }

        private static TrackBinding ResolveBillboardTrack(int trackIndex, BillboardTrack billboardTrack, RigAsset rig)
        {
            if (rig != null && rig.billboardRoots != null)
            {
                foreach (BillboardRootDefinition rootDefinition in rig.billboardRoots)
                {
                    if (rootDefinition != null && rootDefinition.Id.Value == billboardTrack.rootStableId)
                    {
                        string rootName = string.IsNullOrEmpty(rootDefinition.displayName) ? "Billboard root" : rootDefinition.displayName;
                        return new TrackBinding(rootName, 0u, TrackBindingState.Bound, rootName, RetargetTrackKind.Billboard, trackIndex, string.Empty);
                    }
                }
            }

            string reason = rig == null ? "no rig picked" : "no billboard root for it on " + rig.name;
            return new TrackBinding("Billboard track", 0u, TrackBindingState.Skipped, string.Empty, RetargetTrackKind.Billboard, trackIndex, reason);
        }

        private static bool SourcePrefabContainsName(RigAsset rig, string nodeName)
        {
            if (rig == null || rig.sourcePrefab == null)
            {
                return false;
            }

            Transform[] prefabTransforms = rig.sourcePrefab.GetComponentsInChildren<Transform>(true);
            foreach (Transform prefabTransform in prefabTransforms)
            {
                if (prefabTransform.name == nodeName)
                {
                    return true;
                }
            }

            return false;
        }

        private static string DescribeMissingPart(RigAsset rig)
        {
            return rig == null ? "no rig picked" : "no tagged part on " + rig.name;
        }
    }
}
