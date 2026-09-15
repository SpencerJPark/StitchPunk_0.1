// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Collections;
using Unity.Entities;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>Builds an <see cref="AnimEventRoutingBlob"/> from an <see cref="AnimEventRoutingAsset"/>, sorted so the same routes in any list order bake byte-identical.</summary>
    public static class AnimEventRoutingBuilder
    {
        // STUB (A93-T1): empty blob; T3 replaces the body.
        public static BlobAssetReference<AnimEventRoutingBlob> Build(AnimEventRoutingAsset routingAsset, Allocator allocator)
        {
            BlobBuilder blobBuilder = new BlobBuilder(Allocator.Temp);
            try
            {
                ref AnimEventRoutingBlob root = ref blobBuilder.ConstructRoot<AnimEventRoutingBlob>();
                blobBuilder.Allocate(ref root.routes, 0);
                blobBuilder.Allocate(ref root.keys, 0);
                BlobBuilderArray<int> keyStarts = blobBuilder.Allocate(ref root.keyStarts, 1);
                keyStarts[0] = 0;
                return blobBuilder.CreateBlobAssetReference<AnimEventRoutingBlob>(allocator);
            }
            finally
            {
                blobBuilder.Dispose();
            }
        }
    }
}
