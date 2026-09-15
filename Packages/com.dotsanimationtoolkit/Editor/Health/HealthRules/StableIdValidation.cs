// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Health rule for toolkit assets whose stable id exists only in memory and would re-mint on the next load.</summary>
    public static class StableIdValidation
    {
        public static void EvaluateUnpersistedStableIds(HealthScanContext context, List<HealthFinding> output)
        {
        }
    }
}
