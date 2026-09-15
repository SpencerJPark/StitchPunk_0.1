// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Health rules for clip set membership: clips no set lists, and sets listing a missing clip.</summary>
    public static class ClipMembershipValidation
    {
        public static void EvaluateClipsInNoSet(HealthScanContext context, List<HealthFinding> output)
        {
        }

        public static void EvaluateClipSetsListingNullClips(HealthScanContext context, List<HealthFinding> output)
        {
        }
    }
}
