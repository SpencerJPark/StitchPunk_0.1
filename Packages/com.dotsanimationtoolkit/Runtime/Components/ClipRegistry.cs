// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Entities;

namespace DotsAnimationToolkit
{
    /// <summary>Actor-root component holding the baked clip registry. Two actors sharing a <c>ClipSetAsset</c> share one blob via the BlobAssetStore's content-hash dedup.</summary>
    public struct ClipRegistry : IComponentData
    {
        public BlobAssetReference<ClipRegistryBlob> Value; // BlobAssetStore-owned; never manually disposed
    }
}
