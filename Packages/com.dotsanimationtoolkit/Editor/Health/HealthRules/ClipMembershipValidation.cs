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
                    output.Add(new HealthFinding
                    {
                        severity = HealthSeverity.Warning,
                        code = HealthFinding.ClipInNoSetCode,
                        message = $"Clip '{clip.name}' is in no clip set.",
                        target = clip,
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

                output.Add(new HealthFinding
                {
                    severity = HealthSeverity.Error,
                    code = HealthFinding.ClipSetListsNullClipCode,
                    message = $"Clip set '{clipSet.name}' lists {missingClipCount} missing clip(s).",
                    target = clipSet,
                });
            }
        }
    }
}
