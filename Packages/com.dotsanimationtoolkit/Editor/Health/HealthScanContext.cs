// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Everything a Health rule reads: the six toolkit asset lists and the three registries, so fixtures build one from in-memory assets.</summary>
    public sealed class HealthScanContext
    {
        public IReadOnlyList<ClipAsset> clips = Array.Empty<ClipAsset>();
        public IReadOnlyList<ClipSetAsset> clipSets = Array.Empty<ClipSetAsset>();
        public IReadOnlyList<RigAsset> rigs = Array.Empty<RigAsset>();
        public IReadOnlyList<ActorProfileAsset> profiles = Array.Empty<ActorProfileAsset>();
        public IReadOnlyList<CutsceneAsset> cutscenes = Array.Empty<CutsceneAsset>();
        public IReadOnlyList<VatTextureSetAsset> vatTextureSets = Array.Empty<VatTextureSetAsset>();

        // A null registry skips its rule rather than falling back to the project registry, which keeps rules pure.
        public IVocabularyRegistry animationNames;
        public TargetTagRegistry targetTags;
        public AnimEventKeyRegistry eventKeys;

        // Invoked by a stale VAT finding's Rebake fix; the host jumps to VAT Bake with the set and its baked rig.
        public Action<ClipSetAsset, RigAsset> rebakeRequested;

        public static HealthScanContext FromProject(Action<ClipSetAsset, RigAsset> rebakeRequested)
        {
            HealthScanContext context = new HealthScanContext();
            context.clips = new List<ClipAsset>(AssetReferenceIndex.Clips);
            context.clipSets = new List<ClipSetAsset>(AssetReferenceIndex.ClipSets);
            context.rigs = new List<RigAsset>(AssetReferenceIndex.Rigs);
            context.profiles = new List<ActorProfileAsset>(AssetReferenceIndex.Profiles);
            context.cutscenes = new List<CutsceneAsset>(AssetReferenceIndex.Cutscenes);
            context.vatTextureSets = new List<VatTextureSetAsset>(AssetReferenceIndex.VatTextureSets);
            context.animationNames = VocabularyRegistryProvider.AnimationNames;
            context.targetTags = VocabularyRegistryProvider.TargetTags;
            context.eventKeys = VocabularyRegistryProvider.AnimEventKeys;
            context.rebakeRequested = rebakeRequested;
            return context;
        }
    }
}
