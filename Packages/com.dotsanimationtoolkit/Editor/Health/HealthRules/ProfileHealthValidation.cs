// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Health rules for actor profiles: unregistered animation names, and a rig that differs from a listed clip set's baked rig.</summary>
    public static class ProfileHealthValidation
    {
        public static void EvaluateUnregisteredAnimationNames(HealthScanContext context, List<HealthFinding> output)
        {
        }

        public static void EvaluateProfileRigMismatchingClipSetRig(HealthScanContext context, List<HealthFinding> output)
        {
        }
    }
}
