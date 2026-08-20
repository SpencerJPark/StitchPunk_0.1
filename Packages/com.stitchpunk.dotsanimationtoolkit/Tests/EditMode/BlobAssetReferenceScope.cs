// Copyright (c) 2026 Stitch Punk. All rights reserved.

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
        /// <summary>The blob produced by the most recent <see cref="Build"/>.</summary>
        internal BlobAssetReference<ClipRegistryBlob> Registry;

        /// <summary>The dedup hash produced by the most recent <see cref="Build"/>.</summary>
        internal Unity.Entities.Hash128 ContentHash;

        /// <summary>
        /// Builds the registry for a set, releasing any blob this scope already held.
        /// </summary>
        /// <param name="clipSet">The set to bake.</param>
        internal void Build(ClipSetAsset clipSet)
        {
            Dispose();
            ClipRegistryBuilder.Build(clipSet, out Registry, out ContentHash);
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
