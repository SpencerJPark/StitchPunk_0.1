// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.IO;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Saves a mesh with its material on it as a prefab beside the mesh, at one fixed path so it is easy to find again.</summary>
    public static class MaterialPrefabSaving
    {
        public static string ComputePrefabPath(Mesh mesh)
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
            return folder + "/" + SanitizeFileName(mesh.name) + ".prefab";
        }

        public static GameObject FindPrefabForMesh(Mesh mesh)
        {
            string prefabPath = ComputePrefabPath(mesh);
            if (string.IsNullOrEmpty(prefabPath))
            {
                return null;
            }

            return AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        }

        public static bool TrySavePrefab(Mesh mesh, Material material, out GameObject savedPrefab, out string resultMessage)
        {
            savedPrefab = null;
            if (mesh == null || material == null)
            {
                resultMessage = "Pick a mesh and a material before saving a prefab.";
                return false;
            }

            string prefabPath = ComputePrefabPath(mesh);
            if (string.IsNullOrEmpty(prefabPath))
            {
                resultMessage = mesh.name + " is not a saved asset, so there is no folder to put the prefab in.";
                return false;
            }

            string prefabFileName = Path.GetFileName(prefabPath);
            GameObject existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (existingPrefab != null)
            {
                int assignedSlotCount;
                string failureMessage;
                if (!MaterialMeshAssignment.TryAssignToRenderersUsingMesh(
                        existingPrefab, mesh, material, out assignedSlotCount, out failureMessage))
                {
                    resultMessage = failureMessage;
                    return false;
                }

                savedPrefab = existingPrefab;
                resultMessage = "Updated " + prefabFileName + ": " + assignedSlotCount + " part(s) now use " + material.name + ".";
                return true;
            }

            GameObject root = new GameObject(mesh.name);
            try
            {
                root.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer meshRenderer = root.AddComponent<MeshRenderer>();
                Material[] repeatedMaterials = new Material[Mathf.Max(mesh.subMeshCount, 1)];
                for (int slotIndex = 0; slotIndex < repeatedMaterials.Length; slotIndex++)
                {
                    repeatedMaterials[slotIndex] = material;
                }

                meshRenderer.sharedMaterials = repeatedMaterials;
                savedPrefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            if (savedPrefab == null)
            {
                resultMessage = "Unity could not save " + prefabFileName + ".";
                return false;
            }

            resultMessage = "Saved " + prefabFileName + " with " + material.name + ".";
            return true;
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
