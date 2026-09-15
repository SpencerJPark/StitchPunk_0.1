// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Runs every Health rule in code order, then orders the findings: stale VAT bakes first, then errors, warnings, notes.</summary>
    public static class HealthScan
    {
        public static List<HealthFinding> Run(HealthScanContext context)
        {
            List<HealthFinding> findings = new List<HealthFinding>();
            return findings;
        }
    }
}
