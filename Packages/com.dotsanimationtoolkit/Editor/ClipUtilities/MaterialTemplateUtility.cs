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
                    // Flipbooks are Texture2DArrays, so a flipbook plane gets the array graph.
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

            // The caller assigns the created material to a renderer through TryAssignToTargetRenderer.
            createdMaterial = material;
            return true;
        }

        public static bool TryAssignToTargetRenderer(
            RigAsset rig, RigTargetDefinition target, Material material,
            Material preferredSlotMaterial, out string assignedDescription, out string failureMessage)
        {
            assignedDescription = string.Empty;
            failureMessage = string.Empty;

            if (rig == null)
            {
                failureMessage = "No rig was supplied, so there is no renderer to assign the material to.";
                return false;
            }

            if (target == null)
            {
                failureMessage = "No rig target was supplied, so there is no renderer to assign the material to.";
                return false;
            }

            if (material == null)
            {
                failureMessage = "No material was supplied, so there is nothing to assign.";
                return false;
            }

            string prefabAssetPath = AssetDatabase.GetAssetPath(rig.sourcePrefab);
            if (rig.sourcePrefab == null || string.IsNullOrEmpty(prefabAssetPath))
            {
                failureMessage = "Rig '" + rig.name + "' has a source prefab that is not a saved asset.";
                return false;
            }

            GameObject contents = PrefabUtility.LoadPrefabContents(prefabAssetPath);
            if (contents == null)
            {
                failureMessage = "Could not open " + prefabAssetPath + " for editing.";
                return false;
            }

            try
            {
                Transform node = PrefabAuthoringBridge.ResolveByPath(contents.transform, target.sourceNodePath);
                if (node == null)
                {
                    failureMessage = "not assigned: node '" + target.sourceNodePath + "' is not in the prefab.";
                    return false;
                }

                Renderer renderer = node.GetComponent<Renderer>();
                if (renderer == null)
                {
                    failureMessage = "not assigned: node '" + node.name + "' has no Renderer.";
                    return false;
                }

                Material[] slots = renderer.sharedMaterials;
                if (slots.Length == 0)
                {
                    slots = new Material[1];
                }

                int slotIndex = 0;
                if (slots.Length > 1 && preferredSlotMaterial != null)
                {
                    for (int candidateIndex = 0; candidateIndex < slots.Length; candidateIndex++)
                    {
                        if (slots[candidateIndex] == preferredSlotMaterial)
                        {
                            slotIndex = candidateIndex;
                            break;
                        }
                    }
                }

                Material replacedMaterial = slots[slotIndex];
                slots[slotIndex] = material;
                renderer.sharedMaterials = slots;
                PrefabUtility.SaveAsPrefabAsset(contents, prefabAssetPath);

                string nodeName = node.name;
                string replacedDescription = replacedMaterial == null
                    ? "replaced nothing"
                    : "replaced " + replacedMaterial.name + ".mat";
                assignedDescription = slots.Length <= 1
                    ? nodeName + " (" + replacedDescription + ")"
                    : nodeName + " slot " + slotIndex + " (" + replacedDescription + ")";

                failureMessage = string.Empty;
                return true;
            }
            finally
            {
                // In a finally because the temporary scene leaks otherwise.
                PrefabUtility.UnloadPrefabContents(contents);
            }
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
