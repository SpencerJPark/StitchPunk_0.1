// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.IO;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
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
            switch (kind)
            {
                case TargetKind.VatMesh:
                    return VatTemplateShaderPath;
                case TargetKind.FlipbookPlane:
                    // Sprite sheets are Texture2DArrays, so a flipbook plane gets the array graph.
                    return FlipbookTemplateShaderPath;
                case TargetKind.Quad:
                default:
                    return QuadTemplateShaderPath;
            }
        }

        public static string ComputeMaterialAssetPath(RigAsset rig, RigTargetDefinition target)
        {
            if (rig == null || target == null || rig.sourcePrefab == null)
            {
                return string.Empty;
            }

            string prefabPath = AssetDatabase.GetAssetPath(rig.sourcePrefab);
            if (string.IsNullOrEmpty(prefabPath))
            {
                return string.Empty;
            }

            string folder = Path.GetDirectoryName(prefabPath).Replace('\\', '/');

            string targetName = target.displayName;
            if (string.IsNullOrEmpty(targetName))
            {
                string sourceNodePath = target.sourceNodePath;
                if (!string.IsNullOrEmpty(sourceNodePath))
                {
                    int lastSlashIndex = sourceNodePath.LastIndexOf('/');
                    targetName = lastSlashIndex >= 0 ? sourceNodePath.Substring(lastSlashIndex + 1) : sourceNodePath;
                }
            }
            if (string.IsNullOrEmpty(targetName))
            {
                targetName = "Target";
            }

            return folder + "/M_" + Sanitize(rig.name) + "_" + Sanitize(targetName) + ".mat";
        }

        public static bool TryCreateForTarget(
            RigAsset rig, RigTargetDefinition target, out Material createdMaterial, out string failureMessage)
        {
            createdMaterial = null;
            failureMessage = string.Empty;

            if (rig == null)
            {
                failureMessage = "No rig was supplied, so there is nothing to create a material for.";
                return false;
            }

            if (target == null)
            {
                failureMessage = "No rig target was supplied, so there is nothing to create a material for.";
                return false;
            }

            if (rig.sourcePrefab == null)
            {
                failureMessage = "Rig '" + rig.name + "' has no source prefab, so there is no folder to save the material beside.";
                return false;
            }

            string materialAssetPath = ComputeMaterialAssetPath(rig, target);
            if (string.IsNullOrEmpty(materialAssetPath))
            {
                failureMessage = "Rig '" + rig.name + "' has a source prefab that is not a saved asset, so there is no folder to save the material beside.";
                return false;
            }

            string shaderPath = ResolveTemplateShaderPath(target.kind);
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            if (shader == null)
            {
                failureMessage = "Could not load the template shader at '" + shaderPath + "'.";
                return false;
            }

            Material material = new Material(shader);
            material.enableInstancing = true; // Entities Graphics needs it.

            string uniqueAssetPath = AssetDatabase.GenerateUniqueAssetPath(materialAssetPath);
            material.name = Path.GetFileNameWithoutExtension(uniqueAssetPath);

            AssetDatabase.CreateAsset(material, uniqueAssetPath);
            AssetDatabase.SaveAssetIfDirty(material);

            // Assigning the material to a renderer is a prefab edit the author confirms in the Inspector, not done here.
            createdMaterial = material;
            return true;
        }

        // Stub for A96F: the real prefab-asset write lands in T2.
        public static bool TryAssignToTargetRenderer(
            RigAsset rig, RigTargetDefinition target, Material material,
            Material preferredSlotMaterial, out string assignedDescription, out string failureMessage)
        {
            assignedDescription = string.Empty;
            failureMessage = "Assigning the material to the part's renderer is not implemented yet.";
            return false;
        }

        private static string Sanitize(string rawName)
        {
            char[] invalidFileNameChars = Path.GetInvalidFileNameChars();
            char[] sanitizedChars = rawName.ToCharArray();
            for (int characterIndex = 0; characterIndex < sanitizedChars.Length; characterIndex++)
            {
                char currentChar = sanitizedChars[characterIndex];
                bool isInvalid = currentChar == ' ';
                if (!isInvalid)
                {
                    for (int invalidCharIndex = 0; invalidCharIndex < invalidFileNameChars.Length; invalidCharIndex++)
                    {
                        if (currentChar == invalidFileNameChars[invalidCharIndex])
                        {
                            isInvalid = true;
                            break;
                        }
                    }
                }

                if (isInvalid)
                {
                    sanitizedChars[characterIndex] = '_';
                }
            }

            return new string(sanitizedChars);
        }
    }
}
