// Copyright (c) 2026 Spencer Park. All rights reserved.

using DotsAnimationToolkit.Authoring;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Creates a contract-correct material for one rig target from the package's shipped shader graphs, saved beside the rig's prefab.</summary>
    public static class MaterialTemplateUtility
    {
        public const string QuadTemplateShaderPath = "Packages/com.dotsanimationtoolkit/Shaders/ToolkitSpriteUnlit.shadergraph";
        public const string FlipbookTemplateShaderPath = "Packages/com.dotsanimationtoolkit/Shaders/ToolkitSpriteUnlitArray.shadergraph";
        public const string VatTemplateShaderPath = "Packages/com.dotsanimationtoolkit/Shaders/ToolkitVatCrowdUnlit.shadergraph";

        public static string ResolveTemplateShaderPath(TargetKind kind)
        {
            return QuadTemplateShaderPath;
        }

        public static string ComputeMaterialAssetPath(RigAsset rig, RigTargetDefinition target)
        {
            return string.Empty;
        }

        public static bool TryCreateForTarget(
            RigAsset rig, RigTargetDefinition target, out Material createdMaterial, out string failureMessage)
        {
            createdMaterial = null;
            failureMessage = string.Empty;
            return false;
        }
    }
}
