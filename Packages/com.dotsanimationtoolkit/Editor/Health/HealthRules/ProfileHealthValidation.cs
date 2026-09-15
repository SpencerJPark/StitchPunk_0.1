// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Health rules for actor profiles: unregistered animation names, and a rig that differs from a listed clip set's baked rig.</summary>
    public static class ProfileHealthValidation
    {
        public static void EvaluateUnregisteredAnimationNames(HealthScanContext context, List<HealthFinding> output)
        {
            if (context.animationNames == null)
            {
                return;
            }

            foreach (ActorProfileAsset profile in context.profiles)
            {
                if (profile == null)
                {
                    continue;
                }

                List<ValidationMessage> unregisteredMessages = ProfileP2Scan.ScanProfile(profile, context.animationNames);
                foreach (ValidationMessage unregisteredMessage in unregisteredMessages)
                {
                    HealthFinding finding = new HealthFinding();
                    finding.severity = HealthSeverity.Error;
                    finding.code = HealthFinding.ProfileNamesUnregisteredAnimationCode;
                    finding.message = "Profile '" + profile.name + "': " + unregisteredMessage.text;
                    finding.target = profile;
                    output.Add(finding);
                }
            }
        }

        public static void EvaluateProfileRigMismatchingClipSetRig(HealthScanContext context, List<HealthFinding> output)
        {
            foreach (ActorProfileAsset profile in context.profiles)
            {
                if (profile == null || profile.rig == null)
                {
                    continue;
                }

                foreach (ClipSetAsset clipSet in profile.clipSets)
                {
                    if (clipSet == null || clipSet.vatTextures == null)
                    {
                        continue;
                    }

                    // A clip set names no rig; its VAT bake's sourceRigKey is the only rig it records.
                    ulong sourceRigKey = clipSet.vatTextures.sourceRigKey;
                    if (sourceRigKey == 0UL || sourceRigKey == profile.rig.StableId)
                    {
                        continue;
                    }

                    RigAsset bakedRig = VatSourceHashResolver.FindRigByStableId(context.rigs, sourceRigKey);
                    string bakedRigDescription = bakedRig != null
                        ? "rig '" + bakedRig.name + "'."
                        : "a rig that is not in the project.";

                    HealthFinding finding = new HealthFinding();
                    finding.severity = HealthSeverity.Error;
                    finding.code = HealthFinding.ProfileRigDiffersFromClipSetRigCode;
                    finding.message = "Profile '" + profile.name + "' uses rig '" + profile.rig.name +
                        "', but clip set '" + clipSet.name + "' was baked for " + bakedRigDescription;
                    finding.target = profile;
                    finding.secondaryTarget = clipSet;
                    output.Add(finding);
                }
            }
        }
    }
}
