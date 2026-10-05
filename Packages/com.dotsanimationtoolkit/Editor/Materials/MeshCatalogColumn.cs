// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    public sealed class MeshCatalogColumn : ToolkitCatalogColumn<Mesh>
    {
        public event Action<Mesh> MeshSelected;

        public MeshCatalogColumn() : base(BuildOptions())
        {
            AssetSelected += RaiseMeshSelected;
        }

        public void RescanProject()
        {
            Rescan();
        }

        public void SetSelectedMesh(Mesh mesh)
        {
            Select(mesh);
        }

        private static CatalogColumnOptions<Mesh> BuildOptions()
        {
            return new CatalogColumnOptions<Mesh>
            {
                elementName = "mesh-catalog-column",
                namePrefix = "meshes",
                // No title: the sidebar owns the header.
                title = string.Empty,
                newButtonIconName = "d_Toolbar Plus",
                newButtonTooltip = "Meshes come from the Cutouts tab or a model import",
                refreshButtonIconName = "d_Refresh",
                refreshButtonTooltip = "Rescan the project for meshes",
                emptyProjectTitle = "No meshes yet",
                emptyProjectMessage = "Cutouts make meshes, and models imported under Assets/ list here too.",
                emptyProjectActionText = "Make a cutout",
                emptySearchMessage = "No meshes match your search.",
                scan = ScanProjectMeshes,
                secondLine = DescribeMesh,
                tooltip = mesh => AssetDatabase.GetAssetPath(mesh),
                thumbnail = mesh => AssetPreview.GetAssetPreview(mesh),
                allowRename = false,
                allowDelete = false,
            };
        }

        private static IReadOnlyList<Mesh> ScanProjectMeshes()
        {
            List<Mesh> meshes = new List<Mesh>();
            HashSet<string> visitedAssetPaths = new HashSet<string>();
            string[] guids = AssetDatabase.FindAssets("t:Mesh", new[] { "Assets" });
            for (int guidIndex = 0; guidIndex < guids.Length; guidIndex++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[guidIndex]);
                if (!visitedAssetPaths.Add(assetPath))
                {
                    continue;
                }

                // A model file holds several meshes, so load every sub-asset.
                UnityEngine.Object[] assetsAtPath = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                for (int assetIndex = 0; assetIndex < assetsAtPath.Length; assetIndex++)
                {
                    Mesh mesh = assetsAtPath[assetIndex] as Mesh;
                    if (mesh != null)
                    {
                        meshes.Add(mesh);
                    }
                }
            }

            meshes.Sort((Mesh left, Mesh right) =>
                string.Compare(left.name, right.name, StringComparison.OrdinalIgnoreCase));
            return meshes;
        }

        private static string DescribeMesh(Mesh mesh)
        {
            Material material = MaterialFeatureResolver.FindMaterialForMesh(mesh);
            string materialText = material != null ? material.name : "no material";
            return mesh.vertexCount + " verts · " + materialText;
        }

        private void RaiseMeshSelected(Mesh mesh)
        {
            MeshSelected?.Invoke(mesh);
        }
    }
}
