// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Entities;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Per-actor link to the baked VAT texture set. A set may bake several parts, so this only
    /// carries the set key; per-part textures live on <c>VatPartTextureBinding</c> instead.
    /// </summary>
    public struct VatTextureBinding : IComponentData
    {
        public ulong setKey; // matches ClipRegistryBlob.vatSetKey
    }
}
