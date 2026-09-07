// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using Unity.Collections;
using Unity.Entities;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// The composer's view of <c>ClipPreviewController</c>: the registry it advances against and
    /// the one call that hands the controller a composited pose to render. <c>ClipPreviewController</c>
    /// implements this; <c>ActorPreviewComposerTests</c> drives a stub instead.
    /// </summary>
    public interface IActorPosePresenter
    {
        /// <summary>The registry the presenter currently samples, built from the profile's rig and clip sets. Uncreated before <see cref="SetClipSets"/> has run once.</summary>
        BlobAssetReference<ClipRegistryBlob> Registry { get; }

        void SetClipSets(IReadOnlyList<ClipSetAsset> clipSets);

        bool SampleCompositedPose(in NativeArray<PlaybackLayer> layers, bool mirrorX);
    }
}
