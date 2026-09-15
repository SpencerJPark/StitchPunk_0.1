// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Project-wide id changes: re-key or merge event keys, and move keyed tracks to another tag.
    /// Each operation is one undo group across every asset it touches.
    /// </summary>
    public static class RefactorEditing
    {
        public const string RekeyEventUndoName = "Re-key event";
        public const string MergeEventKeysUndoName = "Merge event keys";
        public const string ReplaceTrackTagUndoName = "Replace track tag";

        public static List<AssetReference> PreviewRekeyEvent(uint fromKey)
        {
            return AssetReferenceIndex.ReferencesToEventKey(fromKey);
        }

        public static int RekeyEvent(uint fromKey, uint toKey)
        {
            if (fromKey == 0u || toKey == 0u || fromKey == toKey)
            {
                return 0;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(RekeyEventUndoName);

            HashSet<UnityEngine.Object> touchedOwners = RekeyEventInCurrentUndoGroup(fromKey, toKey, RekeyEventUndoName);

            Undo.CollapseUndoOperations(undoGroup);
            foreach (UnityEngine.Object touchedOwner in touchedOwners)
            {
                AssetDatabase.SaveAssetIfDirty(touchedOwner);
            }

            AssetReferenceIndex.MarkDirty();
            return touchedOwners.Count;
        }

        public static int MergeEventKeys(uint fromKey, uint intoKey)
        {
            return MergeEventKeys(fromKey, intoKey, VocabularyRegistryProvider.AnimEventKeys);
        }

        public static int MergeEventKeys(uint fromKey, uint intoKey, AnimEventKeyRegistry registry)
        {
            if (fromKey == 0u || intoKey == 0u || fromKey == intoKey)
            {
                return 0;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(MergeEventKeysUndoName);

            HashSet<UnityEngine.Object> touchedOwners = RekeyEventInCurrentUndoGroup(fromKey, intoKey, MergeEventKeysUndoName);

            if (registry != null && registry.entries != null)
            {
                bool registryHasEntryToRemove = false;
                foreach (AnimEventKeyEntry registryEntry in registry.entries)
                {
                    if (registryEntry != null && registryEntry.eventKey == fromKey)
                    {
                        registryHasEntryToRemove = true;
                        break;
                    }
                }

                if (registryHasEntryToRemove)
                {
                    Undo.RecordObject(registry, MergeEventKeysUndoName);
                    // Undoing this removal restores the entry in memory only; the project registry file is rewritten on its next Persist.
                    registry.entries.RemoveAll((AnimEventKeyEntry registryEntry) => registryEntry != null && registryEntry.eventKey == fromKey);
                    EditorUtility.SetDirty(registry);
                }
            }

            Undo.CollapseUndoOperations(undoGroup);
            foreach (UnityEngine.Object touchedOwner in touchedOwners)
            {
                AssetDatabase.SaveAssetIfDirty(touchedOwner);
            }

            VocabularyRegistryProvider.Persist(registry);
            if (registry != null && AssetDatabase.Contains(registry))
            {
                AssetDatabase.SaveAssetIfDirty(registry);
            }

            AssetReferenceIndex.MarkDirty();
            return touchedOwners.Count;
        }

        public static List<AssetReference> PreviewReplaceTrackTag(uint fromTagId)
        {
            List<AssetReference> references = AssetReferenceIndex.ReferencesToTag(fromTagId);
            List<AssetReference> filteredReferences = new List<AssetReference>();
            foreach (AssetReference reference in references)
            {
                if (reference.kind != AssetReferenceKind.RigTargetTag)
                {
                    filteredReferences.Add(reference);
                }
            }

            return filteredReferences;
        }

        public static int ReplaceTrackTag(uint fromTagId, uint toTagId)
        {
            if (fromTagId == 0u || toTagId == 0u || fromTagId == toTagId)
            {
                return 0;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(ReplaceTrackTagUndoName);

            HashSet<UnityEngine.Object> touchedOwners = new HashSet<UnityEngine.Object>();
            HashSet<UnityEngine.Object> distinctOwners = new HashSet<UnityEngine.Object>();
            foreach (AssetReference reference in PreviewReplaceTrackTag(fromTagId))
            {
                distinctOwners.Add(reference.owner);
            }

            foreach (UnityEngine.Object owner in distinctOwners)
            {
                if (owner is ClipAsset clipAsset)
                {
                    List<int> transformTrackIndices = RefactorTargetResolver.FindTransformTrackIndices(clipAsset, fromTagId);
                    List<int> spriteTrackIndices = RefactorTargetResolver.FindSpriteTrackIndices(clipAsset, fromTagId);
                    if (transformTrackIndices.Count > 0 || spriteTrackIndices.Count > 0)
                    {
                        Undo.RecordObject(clipAsset, ReplaceTrackTagUndoName);
                        foreach (int transformTrackIndex in transformTrackIndices)
                        {
                            clipAsset.transformTracks[transformTrackIndex].tagId = toTagId;
                        }

                        foreach (int spriteTrackIndex in spriteTrackIndices)
                        {
                            clipAsset.spriteTracks[spriteTrackIndex].tagId = toTagId;
                        }

                        EditorUtility.SetDirty(clipAsset);
                        touchedOwners.Add(clipAsset);
                    }
                }
                else if (owner is CutsceneAsset cutsceneAsset)
                {
                    List<CutsceneKeyedTrack> keyedTracks = RefactorTargetResolver.FindCutscenePartTracks(cutsceneAsset, fromTagId);
                    if (keyedTracks.Count > 0)
                    {
                        Undo.RecordObject(cutsceneAsset, ReplaceTrackTagUndoName);
                        foreach (CutsceneKeyedTrack keyedTrack in keyedTracks)
                        {
                            keyedTrack.tagId = toTagId;
                        }

                        EditorUtility.SetDirty(cutsceneAsset);
                        touchedOwners.Add(cutsceneAsset);
                    }
                }
            }

            Undo.CollapseUndoOperations(undoGroup);
            foreach (UnityEngine.Object touchedOwner in touchedOwners)
            {
                AssetDatabase.SaveAssetIfDirty(touchedOwner);
            }

            AssetReferenceIndex.MarkDirty();
            return touchedOwners.Count;
        }

        private static HashSet<UnityEngine.Object> RekeyEventInCurrentUndoGroup(uint fromKey, uint toKey, string undoName)
        {
            HashSet<UnityEngine.Object> touchedOwners = new HashSet<UnityEngine.Object>();
            HashSet<UnityEngine.Object> distinctOwners = new HashSet<UnityEngine.Object>();
            foreach (AssetReference reference in PreviewRekeyEvent(fromKey))
            {
                distinctOwners.Add(reference.owner);
            }

            foreach (UnityEngine.Object owner in distinctOwners)
            {
                if (owner is ClipAsset clipAsset)
                {
                    List<int> markerIndices = RefactorTargetResolver.FindEventMarkerIndices(clipAsset, fromKey);
                    if (markerIndices.Count > 0)
                    {
                        Undo.RecordObject(clipAsset, undoName);
                        foreach (int markerIndex in markerIndices)
                        {
                            EventMarker marker = clipAsset.events[markerIndex];
                            marker.eventKey = toKey;
                            clipAsset.events[markerIndex] = marker;
                        }

                        EditorUtility.SetDirty(clipAsset);
                        touchedOwners.Add(clipAsset);
                    }
                }
                else if (owner is CutsceneAsset cutsceneAsset)
                {
                    List<int> markerIndices = RefactorTargetResolver.FindCutsceneEventMarkerIndices(cutsceneAsset, fromKey);
                    if (markerIndices.Count > 0)
                    {
                        Undo.RecordObject(cutsceneAsset, undoName);
                        foreach (int markerIndex in markerIndices)
                        {
                            cutsceneAsset.events[markerIndex].eventKey = toKey;
                        }

                        EditorUtility.SetDirty(cutsceneAsset);
                        touchedOwners.Add(cutsceneAsset);
                    }
                }
                else if (owner is ActorProfileAsset profileAsset)
                {
                    List<ActorAnimationDefinition> ragdollDefinitions = RefactorTargetResolver.FindRagdollEventDefinitions(profileAsset, fromKey);
                    if (ragdollDefinitions.Count > 0)
                    {
                        Undo.RecordObject(profileAsset, undoName);
                        foreach (ActorAnimationDefinition ragdollDefinition in ragdollDefinitions)
                        {
                            ragdollDefinition.ragdollAtEventKey = toKey;
                        }

                        EditorUtility.SetDirty(profileAsset);
                        touchedOwners.Add(profileAsset);
                    }
                }
            }

            return touchedOwners;
        }
    }
}
