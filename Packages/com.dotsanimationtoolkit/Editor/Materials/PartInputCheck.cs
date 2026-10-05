// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    public enum PartInputState { NotChecked, NothingNeeded, AllPresent, MissingInputs, NoMaterial }

    public sealed class PartInputNeed
    {
        public string PropertyName;
        public string TrackKindLabel;
        public readonly List<string> ClipNames = new List<string>();
    }

    public sealed class PartInputReport
    {
        public RigTargetDefinition Target;
        public string DisplayName;
        public Renderer PrefabRenderer;
        public readonly List<Material> Materials = new List<Material>();
        public readonly List<PartInputNeed> Needs = new List<PartInputNeed>();
        public readonly List<string> MissingPropertyNames = new List<string>();
        public readonly List<string> Warnings = new List<string>();
        public PartInputState State;

        public bool HasMissingInputs
        {
            get { return State == PartInputState.MissingInputs || State == PartInputState.NoMaterial; }
        }
    }

    /// <summary>Compares what a clip set's tracks drive on each rig part with what that part's prefab materials declare.</summary>
    public static class PartInputCheck
    {
        private const string ImageIndexProperty = "_ImageIndex";
        private const string MainTexArrayProperty = "_MainTexArray";
        private const string AtlasFrameProperty = "_AtlasFrame";
        private const string VatBoneTextureProperty = "_VatBoneTex";
        private const string SliceLabel = "sprite track (slice)";
        private const string AtlasLabel = "sprite track (atlas)";
        private const string VatLabel = "VAT";

        private static readonly string[] VatProperties =
        {
            "_VatFrameA", "_VatFrameB", "_VatBlend", VatBoneTextureProperty, "_VatTexelParams",
        };

        public static List<PartInputReport> Evaluate(RigAsset rig, ClipSetAsset clipSet)
        {
            List<PartInputReport> reports = new List<PartInputReport>();
            if (rig == null)
            {
                return reports;
            }

            for (int targetIndex = 0; targetIndex < rig.targets.Count; targetIndex++)
            {
                RigTargetDefinition target = rig.targets[targetIndex];
                if (target == null)
                {
                    continue;
                }
                reports.Add(BuildReport(rig, clipSet, target));
            }
            return reports;
        }

        private static PartInputReport BuildReport(RigAsset rig, ClipSetAsset clipSet, RigTargetDefinition target)
        {
            PartInputReport report = new PartInputReport { Target = target };
            Transform node = null;
            if (rig.sourcePrefab != null && !string.IsNullOrEmpty(target.sourceNodePath))
            {
                node = PrefabAuthoringBridge.ResolveByPath(rig.sourcePrefab.transform, target.sourceNodePath);
            }

            report.DisplayName = !string.IsNullOrEmpty(target.displayName) ? target.displayName
                : node != null ? node.name
                : target.sourceNodePath;

            if (node != null)
            {
                report.PrefabRenderer = node.GetComponent<Renderer>();
            }
            if (report.PrefabRenderer != null)
            {
                Material[] sharedMaterials = report.PrefabRenderer.sharedMaterials;
                for (int materialIndex = 0; materialIndex < sharedMaterials.Length; materialIndex++)
                {
                    if (sharedMaterials[materialIndex] != null)
                    {
                        report.Materials.Add(sharedMaterials[materialIndex]);
                    }
                }
            }

            if (clipSet == null)
            {
                report.State = PartInputState.NotChecked;
                return report;
            }

            CollectNeeds(clipSet, target, report.Needs);
            if (report.Needs.Count == 0)
            {
                report.State = PartInputState.NothingNeeded;
                return report;
            }

            if (report.PrefabRenderer == null)
            {
                report.Warnings.Add(node == null
                    ? $"No node was found at '{target.sourceNodePath}' on the rig's prefab."
                    : $"The node at '{target.sourceNodePath}' has no Renderer.");
            }
            if (report.Materials.Count == 0)
            {
                report.State = PartInputState.NoMaterial;
                return report;
            }

            for (int needIndex = 0; needIndex < report.Needs.Count; needIndex++)
            {
                string propertyName = report.Needs[needIndex].PropertyName;
                for (int materialIndex = 0; materialIndex < report.Materials.Count; materialIndex++)
                {
                    if (!report.Materials[materialIndex].HasProperty(propertyName))
                    {
                        report.MissingPropertyNames.Add(propertyName);
                        break;
                    }
                }
            }

            CollectMaterialWarnings(report);
            report.State = report.MissingPropertyNames.Count > 0 ? PartInputState.MissingInputs : PartInputState.AllPresent;
            return report;
        }

        private static void CollectNeeds(ClipSetAsset clipSet, RigTargetDefinition target, List<PartInputNeed> needs)
        {
            for (int clipIndex = 0; clipIndex < clipSet.clips.Count; clipIndex++)
            {
                ClipAsset clip = clipSet.clips[clipIndex];
                if (clip == null)
                {
                    continue;
                }

                for (int trackIndex = 0; trackIndex < clip.spriteTracks.Count; trackIndex++)
                {
                    SpriteTrack track = clip.spriteTracks[trackIndex];
                    if (track == null || !TrackTargetMatchResolver.TrackBindsTarget(track.targetId, track.tagId, target))
                    {
                        continue;
                    }
                    if (track.mode == SpriteFrameMode.AtlasRect)
                    {
                        AddNeed(needs, AtlasFrameProperty, AtlasLabel, clip.name);
                    }
                    else
                    {
                        AddNeed(needs, ImageIndexProperty, SliceLabel, clip.name);
                        AddNeed(needs, MainTexArrayProperty, SliceLabel, clip.name);
                    }
                }

                bool drivesVat = clip.vatSource != null && clip.vatSource.sourceClip != null && target.kind == TargetKind.VatMesh;
                for (int vatIndex = 0; !drivesVat && vatIndex < clip.vatTracks.Count; vatIndex++)
                {
                    VatTrack vatTrack = clip.vatTracks[vatIndex];
                    drivesVat = vatTrack != null && vatTrack.sourceClip != null && vatTrack.targetId == target.Id.Value;
                }
                if (drivesVat)
                {
                    for (int propertyIndex = 0; propertyIndex < VatProperties.Length; propertyIndex++)
                    {
                        AddNeed(needs, VatProperties[propertyIndex], VatLabel, clip.name);
                    }
                }
            }
        }

        private static void AddNeed(List<PartInputNeed> needs, string propertyName, string trackKindLabel, string clipName)
        {
            PartInputNeed existing = null;
            for (int needIndex = 0; needIndex < needs.Count; needIndex++)
            {
                if (needs[needIndex].PropertyName == propertyName)
                {
                    existing = needs[needIndex];
                    break;
                }
            }
            if (existing == null)
            {
                existing = new PartInputNeed { PropertyName = propertyName, TrackKindLabel = trackKindLabel };
                needs.Add(existing);
            }
            if (!existing.ClipNames.Contains(clipName))
            {
                existing.ClipNames.Add(clipName);
            }
        }

        private static void CollectMaterialWarnings(PartInputReport report)
        {
            for (int materialIndex = 0; materialIndex < report.Materials.Count; materialIndex++)
            {
                Material material = report.Materials[materialIndex];
                if (!material.enableInstancing)
                {
                    report.Warnings.Add($"GPU instancing is off on {material.name}; Entities Graphics needs it.");
                }
                if (material.HasProperty(MainTexArrayProperty) && material.GetTexture(MainTexArrayProperty) == null)
                {
                    report.Warnings.Add($"{material.name} has no texture array assigned.");
                }
                if (material.HasProperty(VatBoneTextureProperty) && material.GetTexture(VatBoneTextureProperty) == null)
                {
                    report.Warnings.Add($"{material.name} has no VAT bone texture yet: run VAT Bake.");
                }
            }
        }
    }
}
