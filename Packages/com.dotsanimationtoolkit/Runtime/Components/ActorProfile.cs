// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Entities;

namespace DotsAnimationToolkit
{
    /// <summary>Actor-root component holding the baked profile. Two actors sharing an <c>ActorProfileAsset</c> share one blob via the BlobAssetStore's content-hash dedup.</summary>
    public struct ActorProfile : IComponentData
    {
        public BlobAssetReference<ActorProfileBlob> Value; // BlobAssetStore-owned; never manually disposed
    }
}
