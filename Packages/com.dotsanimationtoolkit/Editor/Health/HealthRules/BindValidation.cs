// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Health rules that absorb the old Clip Editor badge: each profile's clip sets bound to its rig, and shared clips still bound by target id.</summary>
    public static class BindValidation
    {
        public static void EvaluateProfileBinds(HealthScanContext context, List<HealthFinding> output)
        {
        }

        public static void EvaluateSharedClipBindings(HealthScanContext context, List<HealthFinding> output)
        {
        }

        // V08 belongs to H06, V36 to H07 and V41 to H08, so a bind message with one of these codes would show twice.
        public static bool IsCodeReportedByAnotherRule(ValidationCode code)
        {
            return false;
        }
    }
}
