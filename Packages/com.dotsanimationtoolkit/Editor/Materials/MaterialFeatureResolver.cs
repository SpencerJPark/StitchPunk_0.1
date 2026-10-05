// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.IO;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    [System.Flags]
    public enum MaterialFeature
    {
        None = 0,
        Flipbook = 1,
        Vat = 2
    }

    /// <summary>Reads a material's motion features from its shader's properties, and maps features to the shipped graphs.</summary>
    public static class MaterialFeatureResolver
    {
        public const string FlipbookTextureArrayPropertyName = "_MainTexArray";
        public const string FlipbookImageIndexPropertyName = "_ImageIndex";
        public const string VatBoneTexturePropertyName = "_VatBoneTex";

        // Read from properties rather than a stored flag, so the same check works on the owner's custom shaders.
        public static MaterialFeature ReadFeatures(Material material)
        {
            MaterialFeature features = MaterialFeature.None;
            if (material == null)
            {
                return features;
            }

            if (material.HasProperty(FlipbookTextureArrayPropertyName) && material.HasProperty(FlipbookImageIndexPropertyName))
            {
                features |= MaterialFeature.Flipbook;
            }

            if (material.HasProperty(VatBoneTexturePropertyName))
            {
                features |= MaterialFeature.Vat;
            }

            return features;
        }

        public static bool IsToolkitShader(Shader shader)
        {
            if (shader == null)
            {
                return false;
            }

            string shaderPath = AssetDatabase.GetAssetPath(shader);
            return shaderPath == MaterialTemplateUtility.QuadTemplateShaderPath
                || shaderPath == MaterialTemplateUtility.FlipbookTemplateShaderPath
                || shaderPath == MaterialTemplateUtility.VatTemplateShaderPath;
        }

        // Null for Flipbook | Vat: no shipped graph does both (A112 §4 defers the keyword graph that would).
        public static string ResolveToolkitShaderPath(MaterialFeature features)
        {
            switch (features)
            {
                case MaterialFeature.None:
                    return MaterialTemplateUtility.QuadTemplateShaderPath;
                case MaterialFeature.Flipbook:
                    return MaterialTemplateUtility.FlipbookTemplateShaderPath;
                case MaterialFeature.Vat:
                    return MaterialTemplateUtility.VatTemplateShaderPath;
                default:
                    return null;
            }
        }

        public static MaterialFeature FeaturesForCutout(CutoutAsset cutout)
        {
            if (cutout == null)
            {
                return MaterialFeature.None;
            }

            return cutout.ResolveArray() != null ? MaterialFeature.Flipbook : MaterialFeature.None;
        }

        public static string ComputeDefaultMaterialPath(Mesh mesh)
        {
            if (mesh == null)
            {
                return string.Empty;
            }

            string meshAssetPath = AssetDatabase.GetAssetPath(mesh);
            if (string.IsNullOrEmpty(meshAssetPath))
            {
                return string.Empty;
            }

            string folder = Path.GetDirectoryName(meshAssetPath).Replace('\\', '/');
            return folder + "/M_" + SanitizeFileName(mesh.name) + ".mat";
        }

        public static Material FindMaterialForMesh(Mesh mesh)
        {
            string materialPath = ComputeDefaultMaterialPath(mesh);
            if (string.IsNullOrEmpty(materialPath))
            {
                return null;
            }

            return AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        }

        private static string SanitizeFileName(string rawName)
        {
            char[] invalidFileNameChars = Path.GetInvalidFileNameChars();
            char[] sanitizedChars = rawName.ToCharArray();
            for (int characterIndex = 0; characterIndex < sanitizedChars.Length; characterIndex++)
            {
                char currentChar = sanitizedChars[characterIndex];
                if (currentChar == ' ' || System.Array.IndexOf(invalidFileNameChars, currentChar) >= 0)
                {
                    sanitizedChars[characterIndex] = '_';
                }
            }

            return new string(sanitizedChars);
        }
    }
}
