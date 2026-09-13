// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;

namespace DotsAnimationToolkit.Editor
{
    public enum AssetReferenceKind : byte
    {
        ProfileRig,
        ProfileClipSet,
        ProfileAnimationClip,
        ProfileRagdollEvent,
        ClipSetClip,
        ClipSetVatTextures,
        ClipTrackTag,
        ClipEventMarker,
        CutsceneSlotRig,
        CutsceneSlotClipSet,
        CutsceneSlotProfile,
        CutsceneTrackTag,
        CutsceneEventMarker,
        RigTargetTag
    }

    public readonly struct AssetReference
    {
        public readonly UnityEngine.Object owner;
        public readonly AssetReferenceKind kind;
        public readonly string detail;

        public AssetReference(UnityEngine.Object owner, AssetReferenceKind kind, string detail)
        {
            this.owner = owner;
            this.kind = kind;
            this.detail = detail ?? string.Empty;
        }
    }

    /// <summary>
    /// Answers "what references this?" across every toolkit asset in the project. Editor-only and
    /// in-memory: the first query after <see cref="MarkDirty"/> rescans the AssetDatabase.
    /// </summary>
    public static class AssetReferenceIndex
    {
        public static event Action Rebuilt;

        private static readonly List<ClipAsset> cachedClips = new List<ClipAsset>();
        private static readonly List<ClipSetAsset> cachedClipSets = new List<ClipSetAsset>();
        private static readonly List<RigAsset> cachedRigs = new List<RigAsset>();
        private static readonly List<ActorProfileAsset> cachedProfiles = new List<ActorProfileAsset>();
        private static readonly List<CutsceneAsset> cachedCutscenes = new List<CutsceneAsset>();
        private static readonly List<VatTextureSetAsset> cachedVatTextureSets = new List<VatTextureSetAsset>();
        private static bool isDirty = true;

        public static void MarkDirty()
        {
            isDirty = true;
        }

        private static void RebuildIfDirty()
        {
            if (!isDirty)
            {
                return;
            }

            cachedClips.Clear();
            cachedClipSets.Clear();
            cachedRigs.Clear();
            cachedProfiles.Clear();
            cachedCutscenes.Clear();
            cachedVatTextureSets.Clear();

            // One combined FindAssets call measures ~65 ms against 250+ ms for six type-filtered
            // calls -- the AssetDatabase scan cost is dominated by the GUID walk, not the filter count.
            string[] foundGuids = AssetDatabase.FindAssets(
                "t:ClipAsset t:ClipSetAsset t:RigAsset t:ActorProfileAsset t:CutsceneAsset t:VatTextureSetAsset");

            HashSet<string> uniqueAssetPaths = new HashSet<string>();
            for (int guidIndex = 0; guidIndex < foundGuids.Length; guidIndex++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(foundGuids[guidIndex]);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    uniqueAssetPaths.Add(assetPath);
                }
            }

            foreach (string assetPath in uniqueAssetPaths)
            {
                UnityEngine.Object[] loadedSubAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                foreach (UnityEngine.Object loadedSubAsset in loadedSubAssets)
                {
                    ClipAsset clipAsset = loadedSubAsset as ClipAsset;
                    if (clipAsset != null)
                    {
                        cachedClips.Add(clipAsset);
                        continue;
                    }

                    ClipSetAsset clipSetAsset = loadedSubAsset as ClipSetAsset;
                    if (clipSetAsset != null)
                    {
                        cachedClipSets.Add(clipSetAsset);
                        continue;
                    }

                    RigAsset rigAsset = loadedSubAsset as RigAsset;
                    if (rigAsset != null)
                    {
                        cachedRigs.Add(rigAsset);
                        continue;
                    }

                    ActorProfileAsset profileAsset = loadedSubAsset as ActorProfileAsset;
                    if (profileAsset != null)
                    {
                        cachedProfiles.Add(profileAsset);
                        continue;
                    }

                    CutsceneAsset cutsceneAsset = loadedSubAsset as CutsceneAsset;
                    if (cutsceneAsset != null)
                    {
                        cachedCutscenes.Add(cutsceneAsset);
                        continue;
                    }

                    VatTextureSetAsset vatTextureSetAsset = loadedSubAsset as VatTextureSetAsset;
                    if (vatTextureSetAsset != null)
                    {
                        cachedVatTextureSets.Add(vatTextureSetAsset);
                    }
                }
            }

            isDirty = false;
            Rebuilt?.Invoke();
        }

        public static List<AssetReference> ReferencesToRig(RigAsset rig)
        {
            if (rig == null)
            {
                return new List<AssetReference>();
            }

            RebuildIfDirty();
            List<AssetReference> foundReferences = new List<AssetReference>();

            foreach (ActorProfileAsset profileAsset in cachedProfiles)
            {
                if (profileAsset != null && profileAsset.rig == rig)
                {
                    foundReferences.Add(new AssetReference(profileAsset, AssetReferenceKind.ProfileRig, string.Empty));
                }
            }

            foreach (CutsceneAsset cutsceneAsset in cachedCutscenes)
            {
                if (cutsceneAsset == null || cutsceneAsset.slots == null)
                {
                    continue;
                }

                foreach (CutsceneSlot cutsceneSlot in cutsceneAsset.slots)
                {
                    if (cutsceneSlot != null && cutsceneSlot.rig == rig)
                    {
                        foundReferences.Add(new AssetReference(cutsceneAsset, AssetReferenceKind.CutsceneSlotRig, "slot " + cutsceneSlot.name));
                    }
                }
            }

            return foundReferences;
        }

        public static List<AssetReference> ReferencesToClip(ClipAsset clip)
        {
            if (clip == null)
            {
                return new List<AssetReference>();
            }

            RebuildIfDirty();
            List<AssetReference> foundReferences = new List<AssetReference>();

            foreach (ClipSetAsset clipSetAsset in cachedClipSets)
            {
                if (clipSetAsset != null && clipSetAsset.clips != null && clipSetAsset.clips.Contains(clip))
                {
                    foundReferences.Add(new AssetReference(clipSetAsset, AssetReferenceKind.ClipSetClip, string.Empty));
                }
            }

            foreach (ActorProfileAsset profileAsset in cachedProfiles)
            {
                if (profileAsset == null || profileAsset.layers == null)
                {
                    continue;
                }

                foreach (ActorLayerDefinition layerDefinition in profileAsset.layers)
                {
                    if (layerDefinition == null || layerDefinition.animations == null)
                    {
                        continue;
                    }

                    foreach (ActorAnimationDefinition animationDefinition in layerDefinition.animations)
                    {
                        if (animationDefinition == null)
                        {
                            continue;
                        }

                        string animationName = ResolveAnimationName(animationDefinition.animationKey);

                        if (animationDefinition.clip == clip)
                        {
                            foundReferences.Add(new AssetReference(
                                profileAsset,
                                AssetReferenceKind.ProfileAnimationClip,
                                "Layer " + layerDefinition.displayName + " ▸ " + animationName));
                        }

                        string matchingDirectionSlotName = FindMatchingDirectionSlotName(animationDefinition.directionSlots, clip);
                        if (matchingDirectionSlotName != null)
                        {
                            foundReferences.Add(new AssetReference(
                                profileAsset,
                                AssetReferenceKind.ProfileAnimationClip,
                                "Layer " + layerDefinition.displayName + " ▸ " + animationName + " (" + matchingDirectionSlotName + ")"));
                        }
                    }
                }
            }

            return foundReferences;
        }

        public static List<AssetReference> ReferencesToClipSet(ClipSetAsset clipSet)
        {
            if (clipSet == null)
            {
                return new List<AssetReference>();
            }

            RebuildIfDirty();
            List<AssetReference> foundReferences = new List<AssetReference>();

            foreach (ActorProfileAsset profileAsset in cachedProfiles)
            {
                if (profileAsset != null && profileAsset.clipSets != null && profileAsset.clipSets.Contains(clipSet))
                {
                    foundReferences.Add(new AssetReference(profileAsset, AssetReferenceKind.ProfileClipSet, string.Empty));
                }
            }

            foreach (CutsceneAsset cutsceneAsset in cachedCutscenes)
            {
                if (cutsceneAsset == null || cutsceneAsset.slots == null)
                {
                    continue;
                }

                foreach (CutsceneSlot cutsceneSlot in cutsceneAsset.slots)
                {
                    if (cutsceneSlot != null && cutsceneSlot.clipSets != null && cutsceneSlot.clipSets.Contains(clipSet))
                    {
                        foundReferences.Add(new AssetReference(cutsceneAsset, AssetReferenceKind.CutsceneSlotClipSet, "slot " + cutsceneSlot.name));
                    }
                }
            }

            return foundReferences;
        }

        public static List<AssetReference> ReferencesToProfile(ActorProfileAsset profile)
        {
            if (profile == null)
            {
                return new List<AssetReference>();
            }

            RebuildIfDirty();
            List<AssetReference> foundReferences = new List<AssetReference>();

            foreach (CutsceneAsset cutsceneAsset in cachedCutscenes)
            {
                if (cutsceneAsset == null || cutsceneAsset.slots == null)
                {
                    continue;
                }

                foreach (CutsceneSlot cutsceneSlot in cutsceneAsset.slots)
                {
                    if (cutsceneSlot != null && cutsceneSlot.profile == profile)
                    {
                        foundReferences.Add(new AssetReference(cutsceneAsset, AssetReferenceKind.CutsceneSlotProfile, "slot " + cutsceneSlot.name));
                    }
                }
            }

            return foundReferences;
        }

        public static List<AssetReference> ReferencesToVatTextures(VatTextureSetAsset textures)
        {
            if (textures == null)
            {
                return new List<AssetReference>();
            }

            RebuildIfDirty();
            List<AssetReference> foundReferences = new List<AssetReference>();

            foreach (ClipSetAsset clipSetAsset in cachedClipSets)
            {
                if (clipSetAsset != null && clipSetAsset.vatTextures == textures)
                {
                    foundReferences.Add(new AssetReference(clipSetAsset, AssetReferenceKind.ClipSetVatTextures, string.Empty));
                }
            }

            return foundReferences;
        }

        /// <param name="eventKey">0 is reserved and always answers with an empty list.</param>
        public static List<AssetReference> ReferencesToEventKey(uint eventKey)
        {
            if (eventKey == 0)
            {
                return new List<AssetReference>();
            }

            RebuildIfDirty();
            List<AssetReference> foundReferences = new List<AssetReference>();

            foreach (ClipAsset clipAsset in cachedClips)
            {
                if (clipAsset == null || clipAsset.events == null)
                {
                    continue;
                }

                foreach (EventMarker eventMarker in clipAsset.events)
                {
                    if (eventMarker.eventKey == eventKey)
                    {
                        foundReferences.Add(new AssetReference(
                            clipAsset,
                            AssetReferenceKind.ClipEventMarker,
                            "marker @" + eventMarker.normalizedTime.ToString("0.00")));
                    }
                }
            }

            foreach (CutsceneAsset cutsceneAsset in cachedCutscenes)
            {
                if (cutsceneAsset == null || cutsceneAsset.events == null)
                {
                    continue;
                }

                foreach (CutsceneEventMarker cutsceneEventMarker in cutsceneAsset.events)
                {
                    if (cutsceneEventMarker != null && cutsceneEventMarker.eventKey == eventKey)
                    {
                        foundReferences.Add(new AssetReference(
                            cutsceneAsset,
                            AssetReferenceKind.CutsceneEventMarker,
                            "marker @" + cutsceneEventMarker.time.ToString("0.00") + "s"));
                    }
                }
            }

            foreach (ActorProfileAsset profileAsset in cachedProfiles)
            {
                if (profileAsset == null || profileAsset.layers == null)
                {
                    continue;
                }

                foreach (ActorLayerDefinition layerDefinition in profileAsset.layers)
                {
                    if (layerDefinition == null || layerDefinition.animations == null)
                    {
                        continue;
                    }

                    foreach (ActorAnimationDefinition animationDefinition in layerDefinition.animations)
                    {
                        if (animationDefinition != null &&
                            animationDefinition.ragdollTrigger != RagdollTrigger.None &&
                            animationDefinition.ragdollAtEventKey == eventKey)
                        {
                            string animationName = ResolveAnimationName(animationDefinition.animationKey);
                            foundReferences.Add(new AssetReference(
                                profileAsset,
                                AssetReferenceKind.ProfileRagdollEvent,
                                "Layer " + layerDefinition.displayName + " ▸ " + animationName));
                        }
                    }
                }
            }

            return foundReferences;
        }

        /// <param name="tagId">0 means untagged and always answers with an empty list.</param>
        public static List<AssetReference> ReferencesToTag(uint tagId)
        {
            if (tagId == 0)
            {
                return new List<AssetReference>();
            }

            RebuildIfDirty();
            List<AssetReference> foundReferences = new List<AssetReference>();
            string tagName = ResolveTagName(tagId);

            foreach (ClipAsset clipAsset in cachedClips)
            {
                if (clipAsset == null)
                {
                    continue;
                }

                if (clipAsset.transformTracks != null)
                {
                    foreach (TransformTrack transformTrack in clipAsset.transformTracks)
                    {
                        if (transformTrack != null && transformTrack.tagId == tagId)
                        {
                            foundReferences.Add(new AssetReference(clipAsset, AssetReferenceKind.ClipTrackTag, "track " + tagName));
                        }
                    }
                }

                if (clipAsset.spriteTracks != null)
                {
                    foreach (SpriteTrack spriteTrack in clipAsset.spriteTracks)
                    {
                        if (spriteTrack != null && spriteTrack.tagId == tagId)
                        {
                            foundReferences.Add(new AssetReference(clipAsset, AssetReferenceKind.ClipTrackTag, "track " + tagName));
                        }
                    }
                }
            }

            foreach (RigAsset rigAsset in cachedRigs)
            {
                if (rigAsset == null || rigAsset.targets == null)
                {
                    continue;
                }

                foreach (RigTargetDefinition targetDefinition in rigAsset.targets)
                {
                    if (targetDefinition != null && targetDefinition.tagId == tagId)
                    {
                        foundReferences.Add(new AssetReference(rigAsset, AssetReferenceKind.RigTargetTag, "target " + targetDefinition.displayName));
                    }
                }
            }

            foreach (CutsceneAsset cutsceneAsset in cachedCutscenes)
            {
                if (cutsceneAsset == null || cutsceneAsset.slots == null)
                {
                    continue;
                }

                foreach (CutsceneSlot cutsceneSlot in cutsceneAsset.slots)
                {
                    if (cutsceneSlot == null || cutsceneSlot.partTracks == null)
                    {
                        continue;
                    }

                    foreach (CutsceneKeyedTrack keyedTrack in cutsceneSlot.partTracks)
                    {
                        if (keyedTrack != null && keyedTrack.tagId == tagId)
                        {
                            foundReferences.Add(new AssetReference(cutsceneAsset, AssetReferenceKind.CutsceneTrackTag, "slot " + cutsceneSlot.name));
                        }
                    }
                }
            }

            return foundReferences;
        }

        // Pure over the two assets handed in: never triggers a rescan, so a hierarchy repaint may
        // call it per row.
        public static int CountTracksBoundToTarget(ClipAsset clip, RigAsset rig, uint targetId)
        {
            if (clip == null || targetId == 0)
            {
                return 0;
            }

            RigTargetDefinition matchedTargetDefinition = null;
            if (rig != null && rig.targets != null)
            {
                foreach (RigTargetDefinition targetDefinition in rig.targets)
                {
                    if (targetDefinition != null && targetDefinition.Id.Value == targetId)
                    {
                        matchedTargetDefinition = targetDefinition;
                        break;
                    }
                }
            }

            int boundTrackCount = 0;

            if (clip.transformTracks != null)
            {
                foreach (TransformTrack transformTrack in clip.transformTracks)
                {
                    if (transformTrack != null && MatchesTargetOrFallsBackToRawId(transformTrack.targetId, transformTrack.tagId, matchedTargetDefinition, targetId))
                    {
                        boundTrackCount++;
                    }
                }
            }

            if (clip.spriteTracks != null)
            {
                foreach (SpriteTrack spriteTrack in clip.spriteTracks)
                {
                    if (spriteTrack != null && MatchesTargetOrFallsBackToRawId(spriteTrack.targetId, spriteTrack.tagId, matchedTargetDefinition, targetId))
                    {
                        boundTrackCount++;
                    }
                }
            }

            return boundTrackCount;
        }

        // Empty string for an empty list. Otherwise one summary line ("Referenced by 2 profiles,
        // 1 clip set.") followed by the distinct owner names, at most ten, then "+N more".
        public static string SummarizeForDialog(List<AssetReference> references)
        {
            if (references == null || references.Count == 0)
            {
                return string.Empty;
            }

            List<UnityEngine.Object> distinctOwnersInFirstSeenOrder = new List<UnityEngine.Object>();
            HashSet<UnityEngine.Object> seenOwners = new HashSet<UnityEngine.Object>();

            foreach (AssetReference reference in references)
            {
                if (reference.owner != null && seenOwners.Add(reference.owner))
                {
                    distinctOwnersInFirstSeenOrder.Add(reference.owner);
                }
            }

            Dictionary<string, int> ownerCountByNoun = new Dictionary<string, int>();
            foreach (UnityEngine.Object owner in distinctOwnersInFirstSeenOrder)
            {
                string noun = NounForOwnerType(owner);
                if (ownerCountByNoun.ContainsKey(noun))
                {
                    ownerCountByNoun[noun] = ownerCountByNoun[noun] + 1;
                }
                else
                {
                    ownerCountByNoun[noun] = 1;
                }
            }

            List<string> nounsOrderedByCountDescending = new List<string>(ownerCountByNoun.Keys);
            nounsOrderedByCountDescending.Sort((firstNoun, secondNoun) =>
            {
                int countComparison = ownerCountByNoun[secondNoun].CompareTo(ownerCountByNoun[firstNoun]);
                return countComparison != 0 ? countComparison : firstNoun.CompareTo(secondNoun);
            });

            List<string> summaryParts = new List<string>();
            foreach (string noun in nounsOrderedByCountDescending)
            {
                int nounCount = ownerCountByNoun[noun];
                summaryParts.Add(nounCount.ToString() + " " + (nounCount == 1 ? noun : noun + "s"));
            }

            string summaryLine = "Referenced by " + string.Join(", ", summaryParts) + ".";

            int shownOwnerCount = distinctOwnersInFirstSeenOrder.Count < 10 ? distinctOwnersInFirstSeenOrder.Count : 10;
            List<string> shownOwnerNames = new List<string>();
            for (int ownerIndex = 0; ownerIndex < shownOwnerCount; ownerIndex++)
            {
                UnityEngine.Object owner = distinctOwnersInFirstSeenOrder[ownerIndex];
                shownOwnerNames.Add(string.IsNullOrEmpty(owner.name) ? "(unnamed)" : owner.name);
            }

            string ownerNamesLine = string.Join(", ", shownOwnerNames);
            int remainingOwnerCount = distinctOwnersInFirstSeenOrder.Count - shownOwnerCount;
            if (remainingOwnerCount > 0)
            {
                ownerNamesLine = ownerNamesLine + ", +" + remainingOwnerCount + " more";
            }

            return summaryLine + "\n" + ownerNamesLine;
        }

        private static string ResolveAnimationName(uint animationKey)
        {
            string registryName = VocabularyRegistryProvider.AnimationNames.FindName(animationKey);
            return string.IsNullOrEmpty(registryName) ? animationKey.ToString() : registryName;
        }

        private static string ResolveTagName(uint tagId)
        {
            string registryName = VocabularyRegistryProvider.TargetTags.FindName(tagId);
            return string.IsNullOrEmpty(registryName) ? tagId.ToString() : registryName;
        }

        private static string FindMatchingDirectionSlotName(DirectionSlots directionSlots, ClipAsset clip)
        {
            if (directionSlots == null)
            {
                return null;
            }

            if (directionSlots.southEast == clip)
            {
                return "southEast";
            }

            if (directionSlots.northEast == clip)
            {
                return "northEast";
            }

            if (directionSlots.south == clip)
            {
                return "south";
            }

            if (directionSlots.north == clip)
            {
                return "north";
            }

            if (directionSlots.east == clip)
            {
                return "east";
            }

            return null;
        }

        // A target the open rig does not declare can still be bound by raw id, so it is matched as
        // an untagged target rather than dropped.
        private static bool MatchesTargetOrFallsBackToRawId(uint trackTargetId, uint trackTagId, RigTargetDefinition matchedTargetDefinition, uint targetId)
        {
            if (matchedTargetDefinition != null)
            {
                return TrackTargetMatchResolver.TrackBindsTarget(trackTargetId, trackTagId, matchedTargetDefinition);
            }

            return TrackTargetMatchResolver.TrackBindsTarget(trackTargetId, trackTagId, targetId, 0u);
        }

        private static string NounForOwnerType(UnityEngine.Object owner)
        {
            if (owner is ActorProfileAsset)
            {
                return "profile";
            }

            if (owner is ClipSetAsset)
            {
                return "clip set";
            }

            if (owner is CutsceneAsset)
            {
                return "cutscene";
            }

            if (owner is ClipAsset)
            {
                return "clip";
            }

            if (owner is RigAsset)
            {
                return "rig";
            }

            return "asset";
        }
    }
}
