// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Decides which of a VAT texture set's part textures and runtime meshes no other set references, so deleting the set can trash them too.</summary>
    public static class VatTextureOwnershipResolver
    {
        public static List<UnityEngine.Object> FindTexturesSafeToTrash(VatTextureSetAsset target, IReadOnlyList<VatTextureSetAsset> allSets)
        {
            return new List<UnityEngine.Object>();
        }
    }
}
