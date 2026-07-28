// Copyright (c) 2026 Stitch Punk. All rights reserved.

using Unity.Entities;
using UnityEngine;

namespace StitchPunk.AnimationToolkit
{
    /// <summary>
    /// Per-actor link between the baked registry and its VAT textures
    /// (architecture section 4.4). The primary GPU binding is material-level (shared material →
    /// shared batch); this component exists for bake-time validation and for advanced hosts that
    /// build materials at runtime. Per-instance texture properties would break BRG batching and do
    /// not exist in the contract (section 6.6).
    /// </summary>
    public struct VatTextureBinding : IComponentData
    {
        /// <summary>The texture set's stable key; matches <see cref="ClipRegistryBlob.vatSetKey"/>.</summary>
        public ulong setKey;

        /// <summary>Bone texture (bone flavor) or position texture (vertex flavor), resolved from the texture set at bake.</summary>
        public UnityObjectRef<Texture2D> boneOrPositionTexture;

        /// <summary>Optional vertex-flavor normal texture; default (null-equivalent) when absent.</summary>
        public UnityObjectRef<Texture2D> normalTexture;
    }
}
