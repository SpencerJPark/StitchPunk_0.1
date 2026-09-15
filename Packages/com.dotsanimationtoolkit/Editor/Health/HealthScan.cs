// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Runs every Health rule in code order, then orders the findings: stale VAT bakes first, then errors, warnings, notes.</summary>
    public static class HealthScan
    {
        private readonly struct OrderedFinding
        {
            internal readonly HealthFinding finding;
            internal readonly int originalIndex;

            internal OrderedFinding(HealthFinding orderedFinding, int orderedIndex)
            {
                finding = orderedFinding;
                originalIndex = orderedIndex;
            }
        }

        private sealed class OrderedFindingComparer : IComparer<OrderedFinding>
        {
            public int Compare(OrderedFinding firstFinding, OrderedFinding secondFinding)
            {
                bool firstPinned = firstFinding.finding.IsPinnedFirst;
                bool secondPinned = secondFinding.finding.IsPinnedFirst;
                if (firstPinned != secondPinned)
                {
                    return firstPinned ? -1 : 1;
                }

                int severityComparison = secondFinding.finding.severity.CompareTo(firstFinding.finding.severity);
                if (severityComparison != 0)
                {
                    return severityComparison;
                }

                return firstFinding.originalIndex.CompareTo(secondFinding.originalIndex);
            }
        }

        public static List<HealthFinding> Run(HealthScanContext context)
        {
            List<HealthFinding> findings = new List<HealthFinding>();
            if (context == null)
            {
                return findings;
            }

            ClipMembershipValidation.EvaluateClipsInNoSet(context, findings);
            ClipMembershipValidation.EvaluateClipSetsListingNullClips(context, findings);
            ProfileHealthValidation.EvaluateUnregisteredAnimationNames(context, findings);
            ProfileHealthValidation.EvaluateProfileRigMismatchingClipSetRig(context, findings);
            RigUsageValidation.EvaluateRigsUsedByNoProfile(context, findings);
            VatFreshnessValidation.EvaluateStaleOrUnbakedVatSets(context, findings);
            TagAndKeyValidation.EvaluateTrackTagsNotInRegistry(context, findings);
            TagAndKeyValidation.EvaluateEventKeysNotInRegistry(context, findings);
            StableIdValidation.EvaluateUnpersistedStableIds(context, findings);
            TagAndKeyValidation.EvaluateClipsPosingNothingOnRig(context, findings);

            OrderedFinding[] orderedFindings = new OrderedFinding[findings.Count];
            for (int findingIndex = 0; findingIndex < findings.Count; findingIndex++)
            {
                orderedFindings[findingIndex] = new OrderedFinding(findings[findingIndex], findingIndex);
            }

            System.Array.Sort(orderedFindings, new OrderedFindingComparer());

            for (int findingIndex = 0; findingIndex < orderedFindings.Length; findingIndex++)
            {
                findings[findingIndex] = orderedFindings[findingIndex].finding;
            }

            return findings;
        }
    }
}
