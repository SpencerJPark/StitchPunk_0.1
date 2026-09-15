// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Health rule for rigs that no actor profile uses.</summary>
    public static class RigUsageValidation
    {
        public static void EvaluateRigsUsedByNoProfile(HealthScanContext context, List<HealthFinding> output)
        {
            foreach (RigAsset rig in context.rigs)
            {
                if (rig == null)
                {
                    continue;
                }

                bool isUsedByAnyProfile = false;
                foreach (ActorProfileAsset profile in context.profiles)
                {
                    if (profile != null && profile.rig == rig)
                    {
                        isUsedByAnyProfile = true;
                        break;
                    }
                }

                if (!isUsedByAnyProfile)
                {
                    output.Add(new HealthFinding
                    {
                        severity = HealthSeverity.Note,
                        code = HealthFinding.RigUsedByNoProfileCode,
                        message = $"Rig '{rig.name}' is used by no actor profile.",
                        target = rig,
                    });
                }
            }
        }
    }
}
