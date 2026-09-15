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

                string freshnessWord = freshness == VatBakeFreshness.Unbaked ? "unbaked" : "stale";
                string rigName = rig != null ? rig.name : "no rig";

                HealthFinding finding = new HealthFinding();
                finding.severity = HealthSeverity.Error;
                finding.code = HealthFinding.StaleOrUnbakedVatSetCode;
                finding.message = "VAT set for clip set '" + clipSet.name + "' on rig '" + rigName + "' is " + freshnessWord + ": " + reason;
                finding.target = textures != null ? (UnityEngine.Object)textures : clipSet;
                finding.secondaryTarget = textures != null ? clipSet : null;

                if (context.rebakeRequested != null)
                {
                    ClipSetAsset capturedClipSet = clipSet;
                    RigAsset capturedRig = rig;
                    finding.fixLabel = "Rebake";
                    finding.fix = () => context.rebakeRequested(capturedClipSet, capturedRig);
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
