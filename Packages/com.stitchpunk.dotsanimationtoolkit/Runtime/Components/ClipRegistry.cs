// Copyright (c) 2026 Stitch Punk. All rights reserved.

using Unity.Entities;

namespace StitchPunk.AnimationToolkit
{
    /// <summary>
    /// Actor-root component holding the baked clip registry (architecture section 5.2). Two actors
    /// sharing a <c>ClipSetAsset</c> share one blob via the BlobAssetStore's content-hash dedup
    /// (section 4.5).
    /// </summary>
    public struct ClipRegistry : IComponentData
    {
        /// <summary>The baked registry blob. BlobAssetStore-owned; never manually disposed.</summary>
        public BlobAssetReference<ClipRegistryBlob> value;
    }
}
