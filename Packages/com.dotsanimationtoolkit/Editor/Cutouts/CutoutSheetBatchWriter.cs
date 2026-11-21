// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    public struct CutoutSheetBatchSettings
    {
        public string outputFolder;
        public float pixelsPerUnit;
        public Vector2 originPreset;
        public int fitVertexBudget;
        public float fitPaddingPixels;
        public bool writePrefabPerStill;

        public static CutoutSheetBatchSettings Default => new CutoutSheetBatchSettings
        {
            outputFolder = "Assets",
            pixelsPerUnit = 100f,
            originPreset = new Vector2(0.5f, 0f),
            fitVertexBudget = 8,
            fitPaddingPixels = 2f,
            writePrefabPerStill = false
        };
    }

    // Writes one CutoutAsset and mesh per still island and per flipbook group of a sheet, re-saving existing assets in place.
    public static class CutoutSheetBatchWriter
    {
        public static bool TryWriteBatch(Texture2D sheet, IReadOnlyList<CutoutIsland> stills,
            IReadOnlyList<CutoutSheetFlipbookSuggestion> groups, CutoutSheetBatchSettings settings, out string report)
        {
            if (sheet == null)
            {
                report = "Pick a sheet texture first.";
                return false;
            }
            if (!sheet.isReadable)
            {
                report = "Turn on Read/Write in the import settings of " + sheet.name + ".";
                return false;
            }
            if (string.IsNullOrEmpty(settings.outputFolder)
                || !(settings.outputFolder == "Assets" || settings.outputFolder.StartsWith("Assets/", System.StringComparison.Ordinal)))
            {
                report = "Pick an output folder inside Assets.";
                return false;
            }

            int stillCount = stills == null ? 0 : stills.Count;
            int groupCount = groups == null ? 0 : groups.Count;
            if (stillCount + groupCount == 0)
            {
                report = "There are no stills or flipbooks to write.";
                return false;
            }

            List<bool[]> sourceMasks = CutoutAlphaTracer.ReadSourceMasks(sheet, CutoutAlphaTracer.DefaultAlphaThreshold);
            if (sourceMasks.Count == 0 || sourceMasks[0] == null)
            {
                report = "Could not read the sheet's pixels.";
                return false;
            }

            bool[] sheetMask = sourceMasks[0];
            Vector2Int sheetSize = new Vector2Int(sheet.width, sheet.height);
            string folder = EnsureFolder(settings.outputFolder, sheet.name);

            AssetDatabase.StartAssetEditing();
            try
            {
                for (int stillIndex = 0; stillIndex < stillCount; stillIndex++)
                {
                    RectInt bounds = stills[stillIndex].boundsPixels;
                    bool[] pieceMask = CutoutIslandFinder.CropMask(sheetMask, sheetSize, bounds);
                    string pieceName = sheet.name + "_" + stillIndex.ToString("00");
                    WritePiece(sheet, folder, pieceName, pieceMask, bounds, null, settings, out Mesh pieceMesh);
                    if (settings.writePrefabPerStill && pieceMesh != null)
                    {
                        WritePrefab(folder, pieceName, pieceMesh);
                    }
                }

                for (int groupIndex = 0; groupIndex < groupCount; groupIndex++)
                {
                    List<RectInt> cells = groups[groupIndex].cells;
                    if (cells == null || cells.Count == 0)
                    {
                        continue;
                    }

                    List<bool[]> cellMasks = new List<bool[]>(cells.Count);
                    for (int cellIndex = 0; cellIndex < cells.Count; cellIndex++)
                    {
                        cellMasks.Add(CutoutIslandFinder.CropMask(sheetMask, sheetSize, cells[cellIndex]));
                    }

                    bool[] unionMask = CutoutAlphaTracer.Union(cellMasks);
                    string groupName = sheet.name + "_Flipbook" + groupIndex.ToString("00");
                    WritePiece(sheet, folder, groupName, unionMask, cells[0], cells, settings, out Mesh groupMesh);
                    WriteSheetFlipbook(sheet, folder, groupName, cells);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            AssetDatabase.SaveAssets();
            report = "Cut " + stillCount + " stills and " + groupCount + " flipbooks into " + folder + "/.";
            return true;
        }

        private static string EnsureFolder(string parentFolder, string childName)
        {
            string folder = parentFolder + "/" + childName;
            if (!AssetDatabase.IsValidFolder(folder))
            {
                AssetDatabase.CreateFolder(parentFolder, childName);
            }
            return folder;
        }

        private static void WritePiece(Texture2D sheet, string folder, string pieceName, bool[] mask, RectInt sourceRect,
            List<RectInt> flipbookCells, CutoutSheetBatchSettings settings, out Mesh mesh)
        {
            Vector2Int rectSize = sourceRect.size;
            Vector2 origin = new Vector2(settings.originPreset.x * rectSize.x, settings.originPreset.y * rectSize.y);
            List<Vector2> outline = CutoutAlphaTracer.FitOutline(mask, rectSize, settings.fitVertexBudget, settings.fitPaddingPixels);

            string meshPath = folder + "/" + pieceName + "_Mesh.asset";
            string assetPath = folder + "/" + pieceName + ".asset";
            mesh = null;

            CutoutAsset template = ScriptableObject.CreateInstance<CutoutAsset>();
            template.flipbook = sheet;
            template.pixelsPerUnit = settings.pixelsPerUnit;
            template.outlinePixels = outline ?? new List<Vector2>();
            template.originPixels = origin;
            template.fitVertexBudget = settings.fitVertexBudget;
            template.fitPaddingPixels = settings.fitPaddingPixels;
            template.sourceRectPixels = sourceRect;
            template.outputPath = meshPath;
            if (flipbookCells != null)
            {
                template.sheetFrameCells = new List<RectInt>(flipbookCells);
            }

            Mesh existingMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            Mesh targetMesh = existingMesh != null ? existingMesh : new Mesh();
            targetMesh.Clear();
            bool built = CutoutMeshBuilder.TryBuild(template.outlinePixels, template.innerEdges, sourceRect,
                new Vector2Int(sheet.width, sheet.height), origin, 0f, settings.pixelsPerUnit,
                template.facing, template.normalMode, template.roundness, targetMesh);
            if (built)
            {
                if (existingMesh == null)
                {
                    targetMesh.name = pieceName + "_Mesh";
                    AssetDatabase.CreateAsset(targetMesh, meshPath);
                }
                else
                {
                    EditorUtility.SetDirty(existingMesh);
                }
                template.outputMesh = targetMesh;
                mesh = targetMesh;
            }
            else if (existingMesh == null)
            {
                Object.DestroyImmediate(targetMesh);
            }

            CutoutAsset existingAsset = AssetDatabase.LoadAssetAtPath<CutoutAsset>(assetPath);
            if (existingAsset != null)
            {
                // Same asset, same GUID: references to the cutout survive a re-cut.
                EditorUtility.CopySerialized(template, existingAsset);
                EditorUtility.SetDirty(existingAsset);
                Object.DestroyImmediate(template);
            }
            else
            {
                template.name = pieceName;
                AssetDatabase.CreateAsset(template, assetPath);
            }
        }

        private static void WriteSheetFlipbook(Texture2D sheet, string folder, string groupName, List<RectInt> cells)
        {
            string path = folder + "/" + groupName + "_Sheet.asset";
            SheetFlipbookAsset target = AssetDatabase.LoadAssetAtPath<SheetFlipbookAsset>(path);
            bool isNew = target == null;
            if (isNew)
            {
                target = ScriptableObject.CreateInstance<SheetFlipbookAsset>();
            }

            target.sheet = sheet;
            target.frames = new List<SheetFlipbookFrame>(cells.Count);
            for (int cellIndex = 0; cellIndex < cells.Count; cellIndex++)
            {
                target.frames.Add(new SheetFlipbookFrame
                {
                    name = groupName + "_" + cellIndex.ToString("00"),
                    cellPixels = cells[cellIndex]
                });
            }

            if (isNew)
            {
                target.name = groupName + "_Sheet";
                AssetDatabase.CreateAsset(target, path);
            }
            else
            {
                EditorUtility.SetDirty(target);
            }
        }

        private static void WritePrefab(string folder, string pieceName, Mesh mesh)
        {
            GameObject root = new GameObject(pieceName);
            try
            {
                root.AddComponent<MeshFilter>().sharedMesh = mesh;
                root.AddComponent<MeshRenderer>();
                // Saving over an existing prefab path overwrites in place and keeps its GUID.
                PrefabUtility.SaveAsPrefabAsset(root, folder + "/" + pieceName + ".prefab");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
