// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Health rule for clip sets whose VAT texture set is stale or was never baked.</summary>
    public static class VatFreshnessValidation
    {
        public static void EvaluateStaleOrUnbakedVatSets(HealthScanContext context, List<HealthFinding> output)
        {
            foreach (ClipSetAsset clipSet in context.clipSets)
            {
                if (clipSet == null)
                {
                    continue;
                }

                VatTextureSetAsset textures = clipSet.vatTextures;
                if (textures == null && !VatSourceHashResolver.HasVatBoundClips(clipSet))
                {
                    continue;
                }

                RigAsset rig = FindRigTheSetWasBakedFrom(context, clipSet, textures);

                string reason;
                VatBakeFreshness freshness = VatSourceHashResolver.Resolve(clipSet, rig, textures, out reason);
                if (freshness == VatBakeFreshness.Fresh)
                {
                    continue;
                }

                bool isUnbaked = freshness == VatBakeFreshness.Unbaked;
                string freshnessWord = isUnbaked ? "unbaked" : "stale";
                string message = rig != null
                    ? "VAT set for clip set '" + clipSet.name + "' on rig '" + rig.name + "' is " + freshnessWord + ": " + reason
                    : "VAT set for clip set '" + clipSet.name + "' has no baked rig yet and is " + freshnessWord + ": " + reason;

                ClipSetAsset capturedClipSet = clipSet;
                RigAsset capturedRig = rig;
                VatTextureSetAsset capturedTextures = textures;

                HealthFinding finding = new HealthFinding();
                finding.severity = HealthSeverity.Error;
                finding.code = HealthFinding.StaleOrUnbakedVatSetCode;
                finding.message = message;
                finding.title = isUnbaked ? "VAT bake is not baked yet" : "VAT bake is stale";
                finding.detail = isUnbaked
                    ? "Actors using this clip set play no motion at runtime, with no run-time error."
                    : "Actors using this clip set play old motion at runtime, with no run-time error. Rebake after editing clips or the rig.";
                finding.target = textures != null ? (UnityEngine.Object)textures : clipSet;
                finding.secondaryTarget = textures != null ? clipSet : null;
                finding.relatedAssets.Add(clipSet);
                if (textures != null)
                {
                    finding.relatedAssets.Add(textures);
                }
                if (rig != null)
                {
                    finding.relatedAssets.Add(rig);
                }

                if (context.rebakeRequested != null)
                {
                    finding.actions.Add(new HealthFindingAction
                    {
                        label = "Rebake",
                        description = "Opens VAT Bake with this clip set and its rig.",
                        run = () => context.rebakeRequested(capturedClipSet, capturedRig)
                    });
                }

                finding.actions.Add(HealthFindingAction.Locate("Locate", "Selects and pings the clip set.", capturedClipSet));

                if (capturedTextures != null)
                {
                    finding.actions.Add(new HealthFindingAction
                    {
                        label = "Delete VAT textures…",
                        description = "Moves the texture set and the part files only it uses to the OS trash; the clip set becomes unbaked.",
                        isDestructive = true,
                        buildConfirmation = () => HealthFindingAction.BuildDeleteConfirmation(
                            UnityEditor.AssetDatabase.GetAssetPath(capturedTextures),
                            AssetReferenceIndex.ReferencesToVatTextures(capturedTextures)) +
                            "\n\nPart textures and runtime meshes no other VAT set uses go to the trash with it.",
                        run = () => VatTextureSetAssetUtility.TrashTextureSet(capturedTextures)
                    });
                }

                output.Add(finding);
            }
        }

        // Mirrors the Clip Sets tab: a bound texture set's own sourceRigKey wins, otherwise fall back to
        // the rig of whichever profile lists this clip set (the tab falls back to its shared selection instead).
        private static RigAsset FindRigTheSetWasBakedFrom(HealthScanContext context, ClipSetAsset clipSet, VatTextureSetAsset textures)
        {
            if (textures != null && textures.sourceRigKey != 0UL)
            {
                return VatSourceHashResolver.FindRigByStableId(context.rigs, textures.sourceRigKey);
            }

            foreach (ActorProfileAsset profile in context.profiles)
            {
                if (profile == null || profile.rig == null || profile.clipSets == null)
                {
                    continue;
                }

                if (profile.clipSets.Contains(clipSet))
                {
                    return profile.rig;
                }
            }

            return null;
        }
    }
}
