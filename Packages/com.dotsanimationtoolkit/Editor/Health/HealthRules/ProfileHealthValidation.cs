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

                ActorProfileAsset capturedProfile = profile;
                List<ValidationMessage> unregisteredMessages = ProfileP2Scan.ScanProfile(profile, context.animationNames);
                foreach (ValidationMessage unregisteredMessage in unregisteredMessages)
                {
                    HealthFinding finding = new HealthFinding();
                    finding.severity = HealthSeverity.Error;
                    finding.code = HealthFinding.ProfileNamesUnregisteredAnimationCode;
                    finding.message = "Profile '" + profile.name + "': " + unregisteredMessage.text;
                    finding.title = "Profile names an unregistered animation";
                    finding.detail = "Play-by-name fails silently for this name: nothing plays and no error is logged.";
                    finding.target = profile;
                    finding.relatedAssets.Add(profile);
                    finding.actions.Add(HealthFindingAction.Locate("Locate profile", "Selects and pings the actor profile.", capturedProfile));
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

                    ActorProfileAsset capturedProfile = profile;
                    ClipSetAsset capturedClipSet = clipSet;

                    HealthFinding finding = new HealthFinding();
                    finding.severity = HealthSeverity.Error;
                    finding.code = HealthFinding.ProfileRigDiffersFromClipSetRigCode;
                    finding.message = "Profile '" + profile.name + "' uses rig '" + profile.rig.name +
                        "', but clip set '" + clipSet.name + "' was baked for " + bakedRigDescription;
                    finding.title = "Profile rig differs from its clip set's baked rig";
                    finding.detail = "VAT textures were baked for another rig, so the actor deforms wrongly at runtime.";
                    finding.target = profile;
                    finding.secondaryTarget = clipSet;
                    finding.relatedAssets.Add(clipSet);
                    finding.relatedAssets.Add(clipSet.vatTextures);
                    finding.relatedAssets.Add(profile.rig);
                    if (bakedRig != null)
                    {
                        finding.relatedAssets.Add(bakedRig);
                    }
                    finding.actions.Add(HealthFindingAction.Locate("Locate profile", "Selects and pings the actor profile.", capturedProfile));
                    finding.actions.Add(HealthFindingAction.Locate("Locate clip set", "Selects and pings the clip set.", capturedClipSet));
                    output.Add(finding);
                }
            }
        }
    }
}
