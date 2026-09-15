// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Health rules for clip set membership: clips no set lists, and sets listing a missing clip.</summary>
    public static class ClipMembershipValidation
    {
        public static void EvaluateClipsInNoSet(HealthScanContext context, List<HealthFinding> output)
        {
            foreach (ClipAsset clip in context.clips)
            {
                if (clip == null)
                {
                    continue;
                }

                bool isListedInAnySet = false;
                foreach (ClipSetAsset clipSet in context.clipSets)
                {
                    if (clipSet != null && clipSet.clips != null && clipSet.clips.Contains(clip))
                    {
                        isListedInAnySet = true;
                        break;
                    }
                }

                if (!isListedInAnySet)
                {
                    ClipAsset capturedClip = clip;
                    output.Add(new HealthFinding
                    {
                        severity = HealthSeverity.Warning,
                        code = HealthFinding.ClipInNoSetCode,
                        message = $"Clip '{clip.name}' is in no clip set.",
                        title = "Clip is in no clip set",
                        detail = "No clip set registers this clip, so no actor can play it. Cutscenes and profiles may still reference it by name.",
                        target = clip,
                        relatedAssets = { clip },
                        actions =
                        {
                            HealthFindingAction.Locate("Locate", "Selects and pings the clip in the Project window.", clip),
                            new HealthFindingAction
                            {
                                label = "Delete clip…",
                                description = "Moves the clip asset to the OS trash.",
                                isDestructive = true,
                                buildConfirmation = () => HealthFindingAction.BuildDeleteConfirmation(AssetDatabase.GetAssetPath(capturedClip), AssetReferenceIndex.ReferencesToClip(capturedClip)),
                                run = () => ClipAssetUtility.TrashClip(capturedClip),
                            },
                        },
                    });
                }
            }
        }

        public static void EvaluateClipSetsListingNullClips(HealthScanContext context, List<HealthFinding> output)
        {
            foreach (ClipSetAsset clipSet in context.clipSets)
            {
                if (clipSet == null || clipSet.clips == null)
                {
                    continue;
                }

                int missingClipCount = 0;
                foreach (ClipAsset clip in clipSet.clips)
                {
                    if (clip == null)
                    {
                        missingClipCount++;
                    }
                }

                if (missingClipCount == 0)
                {
                    continue;
                }

                ClipSetAsset capturedClipSet = clipSet;
                output.Add(new HealthFinding
                {
                    severity = HealthSeverity.Error,
                    code = HealthFinding.ClipSetListsNullClipCode,
                    message = $"Clip set '{clipSet.name}' lists {missingClipCount} missing clip(s).",
                    title = "Clip set lists missing clips",
                    detail = "The set has empty slots. Baking skips them and later clip indices shift.",
                    target = clipSet,
                    relatedAssets = { clipSet },
                    actions =
                    {
                        new HealthFindingAction
                        {
                            label = "Remove missing",
                            description = "Removes every empty slot from the set, as one undo step.",
                            run = () =>
                            {
                                Undo.IncrementCurrentGroup();
                                int undoGroup = Undo.GetCurrentGroup();
                                Undo.SetCurrentGroupName("Remove Missing Clips");
                                for (int clipIndex = capturedClipSet.clips.Count - 1; clipIndex >= 0; clipIndex--)
                                {
                                    if (capturedClipSet.clips[clipIndex] == null)
                                    {
                                        ClipAssetUtility.RemoveClipFromSet(capturedClipSet, clipIndex);
                                    }
                                }
                                Undo.CollapseUndoOperations(undoGroup);
                                AssetDatabase.SaveAssetIfDirty(capturedClipSet);
                            },
                        },
                        HealthFindingAction.Locate("Locate", "Selects and pings the clip set.", clipSet),
                    },
                });
            }
        }
    }
}
