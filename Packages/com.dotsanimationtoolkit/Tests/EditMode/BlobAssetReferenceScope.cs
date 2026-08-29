// Copyright (c) 2026 Spencer Park. All rights reserved.

using DotsAnimationToolkit.Authoring;
using Unity.Entities;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// Owns one built <see cref="ClipRegistryBlob"/> for the duration of a test. In entity baking the
    /// <c>BlobAssetStore</c> owns the blob and nothing disposes it by hand; in an EditMode fixture
    /// there is no store, so every build must be released explicitly or the allocator reports a leak
    /// at the end of the run.
    /// </summary>
    internal sealed class BlobAssetReferenceScope
    {
        /// <summary>
        /// The fixture that knows which rig each set is bound to, since no asset records it.
        /// Optional: a caller that always names the rig explicitly needs none.
        /// </summary>
        private readonly AuthoringTestAssets boundAssets;

        internal BlobAssetReferenceScope()
        {
        }

        internal BlobAssetReferenceScope(AuthoringTestAssets assets)
        {
            boundAssets = assets;
        }

        /// <summary>The blob produced by the most recent <see cref="Build"/>.</summary>
        internal BlobAssetReference<ClipRegistryBlob> Registry;

        /// <summary>The dedup hash produced by the most recent <see cref="Build"/>.</summary>
        internal Unity.Entities.Hash128 ContentHash;

        /// <summary>
        /// Builds the registry for one set on the rig the owning fixture bound it to, releasing any
        /// blob this scope already held.
        /// </summary>
        /// <param name="clipSet">The set to bake.</param>
        internal void Build(ClipSetAsset clipSet)
        {
            Build(boundAssets != null ? boundAssets.RigBoundTo(clipSet) : null, clipSet);
        }

        /// <summary>Builds the registry for an explicit bind: one rig and the sets played on it.</summary>
        internal void Build(RigAsset rig, params ClipSetAsset[] clipSets)
        {
            Dispose();
            ClipRegistryBuilder.Build(rig, clipSets, out Registry, out ContentHash);
        }

        /// <summary>Releases the held blob if there is one. Safe to call repeatedly.</summary>
        internal void Dispose()
        {
            if (Registry.IsCreated)
            {
                Registry.Dispose();
            }
            Registry = default;
            ContentHash = default;
        }
    }
}
