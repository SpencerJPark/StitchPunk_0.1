// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Entities;

namespace DotsAnimationToolkit
{
    /// <summary>Singleton holding the project's baked <see cref="AnimEventRoutingBlob"/>, added by the routing authoring component a host drops in its subscene.</summary>
    public struct AnimEventRouting : IComponentData
    {
        public BlobAssetReference<AnimEventRoutingBlob> Value; // BlobAssetStore-owned; never manually disposed
    }
}
