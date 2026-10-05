// Copyright (c) 2026 Spencer Park. All rights reserved.

using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    public static class MaterialMeshAssignment
    {
        public static int CountRendererSlotsUsingMesh(GameObject prefab, Mesh mesh)
        {
            if (prefab == null || mesh == null)
            {
                return 0;
            }

            int matchingRendererCount = 0;
            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                if (RendererUsesMesh(renderers[rendererIndex], mesh))
                {
                    matchingRendererCount++;
                }
            }

            return matchingRendererCount;
        }

        public static bool TryAssignToRenderersUsingMesh(GameObject prefab, Mesh mesh, Material material,
            out int assignedSlotCount, out string failureMessage)
        {
            assignedSlotCount = 0;
            failureMessage = string.Empty;

            if (prefab == null || mesh == null || material == null)
            {
                failureMessage = "A prefab, a mesh and a material are all needed to assign a material.";
                return false;
            }

            string prefabAssetPath = AssetDatabase.GetAssetPath(prefab);
            if (string.IsNullOrEmpty(prefabAssetPath))
            {
                failureMessage = "The source prefab '" + prefab.name + "' is not a saved asset.";
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
                int changedSlotCount = 0;
                Renderer[] renderers = contents.GetComponentsInChildren<Renderer>(true);
                for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                {
                    Renderer renderer = renderers[rendererIndex];
                    if (!RendererUsesMesh(renderer, mesh))
                    {
                        continue;
                    }

                    assignedSlotCount++;
                    Material[] slots = renderer.sharedMaterials;
                    if (slots.Length == 0)
                    {
                        slots = new Material[1];
                    }

                    if (slots[0] == material)
                    {
                        continue;
                    }

                    slots[0] = material;
                    renderer.sharedMaterials = slots;
                    changedSlotCount++;
                }

                if (assignedSlotCount == 0)
                {
                    failureMessage = "No part of " + prefab.name + " uses " + mesh.name + ".";
                    return false;
                }

                if (changedSlotCount > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(contents, prefabAssetPath);
                }

                return true;
            }
            finally
            {
                // In a finally because the temporary scene leaks otherwise.
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        static bool RendererUsesMesh(Renderer renderer, Mesh mesh)
        {
            SkinnedMeshRenderer skinnedRenderer = renderer as SkinnedMeshRenderer;
            if (skinnedRenderer != null)
            {
                return skinnedRenderer.sharedMesh == mesh;
            }

            if (renderer is MeshRenderer)
            {
                MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
                return meshFilter != null && meshFilter.sharedMesh == mesh;
            }

            return false;
        }
    }
}
