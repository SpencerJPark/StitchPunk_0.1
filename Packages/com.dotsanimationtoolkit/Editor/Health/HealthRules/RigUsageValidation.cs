// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;

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
                    RigAsset capturedRig = rig;
                    output.Add(new HealthFinding
                    {
                        severity = HealthSeverity.Note,
                        code = HealthFinding.RigUsedByNoProfileCode,
                        message = $"Rig '{rig.name}' is used by no actor profile.",
                        title = "Rig is used by no profile",
                        detail = "No actor profile binds this rig, so it is probably leftover.",
                        target = rig,
                        relatedAssets = { rig },
                        actions =
                        {
                            HealthFindingAction.Locate("Locate", "Selects and pings the rig.", rig),
                            new HealthFindingAction
                            {
                                label = "Delete rig…",
                                description = "Moves the rig asset to the OS trash.",
                                isDestructive = true,
                                buildConfirmation = () => HealthFindingAction.BuildDeleteConfirmation(AssetDatabase.GetAssetPath(capturedRig), AssetReferenceIndex.ReferencesToRig(capturedRig)),
                                run = () => RigAssetUtility.DeleteRig(capturedRig),
                            },
                        },
                    });
                }
            }
        }
    }
}
