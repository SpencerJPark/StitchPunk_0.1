// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.IO;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    public static class MaterialAuthoringUtility
    {
        private const string MainTexturePropertyName = "_MainTex";
        private const string BaseColorPropertyName = "_BaseColor";
        private const string CutoffPropertyName = "_Cutoff";

        public static bool TryCreateMaterial(string assetPath, MaterialFeature features, Texture sourceTexture,
            out Material createdMaterial, out string failureMessage)
        {
            createdMaterial = null;
            failureMessage = string.Empty;

            if (string.IsNullOrEmpty(assetPath))
            {
                failureMessage = "Choose where to save the material first.";
                return false;
            }

            string shaderPath = MaterialFeatureResolver.ResolveToolkitShaderPath(features);
            if (shaderPath == null)
            {
                failureMessage = "No shipped shader does Flipbook and VAT together; use your own shader.";
                return false;
            }

            if ((features & MaterialFeature.Vat) != 0)
            {
                failureMessage = "VAT is turned on by the VAT bake, not when a material is made.";
                return false;
            }

            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            if (shader == null)
            {
                failureMessage = "Could not load the template shader at '" + shaderPath + "'.";
                return false;
            }

            Material material = new Material(shader);
            material.enableInstancing = true; // Entities Graphics needs it.

            if (sourceTexture is Texture2DArray && material.HasProperty(MaterialFeatureResolver.FlipbookTextureArrayPropertyName))
            {
                material.SetTexture(MaterialFeatureResolver.FlipbookTextureArrayPropertyName, sourceTexture);
            }
            else if (sourceTexture is Texture2D && material.HasProperty(MainTexturePropertyName))
            {
                material.SetTexture(MainTexturePropertyName, sourceTexture);
            }

            string uniqueAssetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);
            material.name = Path.GetFileNameWithoutExtension(uniqueAssetPath);

            AssetDatabase.CreateAsset(material, uniqueAssetPath);
            AssetDatabase.SaveAssetIfDirty(material);

            createdMaterial = material;
            return true;
        }

        public static bool TrySetToolkitFeatures(Material material, MaterialFeature features, out string resultMessage)
        {
            resultMessage = string.Empty;
            if (material == null)
            {
                resultMessage = "There is no material to change.";
                return false;
            }

            if (!MaterialFeatureResolver.IsToolkitShader(material.shader))
            {
                resultMessage = material.name + " uses a custom shader; its features come from its own properties.";
                return false;
            }

            MaterialFeature currentFeatures = MaterialFeatureResolver.ReadFeatures(material);
            if (features == currentFeatures)
            {
                resultMessage = material.name + " already has those features set.";
                return true;
            }

            string shaderPath = MaterialFeatureResolver.ResolveToolkitShaderPath(features);
            if (shaderPath == null)
            {
                resultMessage = "No shipped shader does Flipbook and VAT together; use your own shader.";
                return false;
            }

            Shader newShader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            if (newShader == null)
            {
                resultMessage = "Could not load the template shader at '" + shaderPath + "'.";
                return false;
            }

            bool flipbookChanged = (features & MaterialFeature.Flipbook) != (currentFeatures & MaterialFeature.Flipbook);

            bool hadMainTexture = material.HasProperty(MainTexturePropertyName);
            Texture mainTexture = hadMainTexture ? material.GetTexture(MainTexturePropertyName) : null;
            bool hadBaseColor = material.HasProperty(BaseColorPropertyName);
            Color baseColor = hadBaseColor ? material.GetColor(BaseColorPropertyName) : Color.white;
            bool hadCutoff = material.HasProperty(CutoffPropertyName);
            float cutoff = hadCutoff ? material.GetFloat(CutoffPropertyName) : 0f;

            material.shader = newShader;
            material.enableInstancing = true;

            // Static and VAT graphs share _MainTex; the array graph does not, and a Texture2D cannot become a Texture2DArray.
            if (hadMainTexture && !flipbookChanged && material.HasProperty(MainTexturePropertyName))
            {
                material.SetTexture(MainTexturePropertyName, mainTexture);
            }

            if (hadBaseColor && material.HasProperty(BaseColorPropertyName))
            {
                material.SetColor(BaseColorPropertyName, baseColor);
            }

            if (hadCutoff && material.HasProperty(CutoffPropertyName))
            {
                material.SetFloat(CutoffPropertyName, cutoff);
            }

            EditorUtility.SetDirty(material);
            if (AssetDatabase.Contains(material))
            {
                AssetDatabase.SaveAssetIfDirty(material);
            }

            resultMessage = flipbookChanged
                ? material.name + " was switched, and its texture was cleared; assign a new one."
                : material.name + " was switched; its texture, colour and cutoff were kept.";
            return true;
        }
    }
}
