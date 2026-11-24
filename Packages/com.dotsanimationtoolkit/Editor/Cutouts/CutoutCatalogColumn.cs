// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    public sealed class CutoutCatalogColumn : ToolkitCatalogColumn<CutoutAsset>
    {
        private const float DragStartDistance = 4f;

        private CutoutAsset pendingDragCutout;
        private Vector2 pendingDragOrigin;

        public CutoutCatalogColumn() : base(BuildOptions())
        {
            RegisterCallback<PointerDownEvent>(OnCutoutRowPointerDown, TrickleDown.TrickleDown);
            RegisterCallback<PointerMoveEvent>(OnCutoutRowPointerMove, TrickleDown.TrickleDown);
            RegisterCallback<PointerUpEvent>(OnCutoutRowPointerUp, TrickleDown.TrickleDown);
        }

        public void RescanProject()
        {
            Rescan();
        }

        private static CutoutAsset FindCutoutUnderPointer(VisualElement target)
        {
            VisualElement current = target;
            while (current != null)
            {
                CutoutAsset cutout = current.userData as CutoutAsset;
                if (cutout != null)
                {
                    return cutout;
                }

                current = current.parent;
            }

            return null;
        }

        private void OnCutoutRowPointerDown(PointerDownEvent pointerDownEvent)
        {
            pendingDragCutout = pointerDownEvent.button == 0
                ? FindCutoutUnderPointer(pointerDownEvent.target as VisualElement)
                : null;
            pendingDragOrigin = pointerDownEvent.position;
        }

        private void OnCutoutRowPointerMove(PointerMoveEvent pointerMoveEvent)
        {
            if (pendingDragCutout == null || (pointerMoveEvent.pressedButtons & 1) == 0)
            {
                return;
            }

            if (Vector2.Distance(pendingDragOrigin, pointerMoveEvent.position) < DragStartDistance)
            {
                return;
            }

            CutoutAsset draggedCutout = pendingDragCutout;
            pendingDragCutout = null;
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.objectReferences = new UnityEngine.Object[] { draggedCutout };
            DragAndDrop.StartDrag(draggedCutout.name);
        }

        private void OnCutoutRowPointerUp(PointerUpEvent pointerUpEvent)
        {
            pendingDragCutout = null;
        }

        public void SetSelectedCutout(CutoutAsset cutout)
        {
            Select(cutout);
        }

        private static CatalogColumnOptions<CutoutAsset> BuildOptions()
        {
            return new CatalogColumnOptions<CutoutAsset>
            {
                elementName = "cutout-catalog-column",
                namePrefix = "cutouts",
                // No title: the Cutouts sidebar owns the header.
                title = string.Empty,
                newButtonIconName = "d_Toolbar Plus",
                newButtonTooltip = "Start a cutout: pick the flipbook or image to draw it over",
                refreshButtonIconName = "d_Refresh",
                refreshButtonTooltip = "Rescan the project for cutouts",
                emptyProjectTitle = "No cutouts yet",
                emptyProjectMessage = "A cutout is a flat mesh drawn tight around a flipbook's or an image's art.",
                emptyProjectActionText = "New Cutout",
                emptySearchMessage = "No cutouts match your search.",
                scan = ScanProjectCutouts,
                secondLine = DescribeCutout,
                tooltip = DescribeCutoutTooltip,
                allowRename = true,
                allowDelete = true,
            };
        }

        private static IReadOnlyList<CutoutAsset> ScanProjectCutouts()
        {
            List<CutoutAsset> cutouts = new List<CutoutAsset>();
            string[] guids = AssetDatabase.FindAssets("t:CutoutAsset");
            for (int guidIndex = 0; guidIndex < guids.Length; guidIndex++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[guidIndex]);
                CutoutAsset cutout = AssetDatabase.LoadAssetAtPath<CutoutAsset>(assetPath);
                if (cutout != null)
                {
                    cutouts.Add(cutout);
                }
            }

            cutouts.Sort((CutoutAsset left, CutoutAsset right) =>
                string.Compare(left.name, right.name, StringComparison.OrdinalIgnoreCase));
            return cutouts;
        }

        private static string DescribeCutout(CutoutAsset cutout)
        {
            string sourceName = cutout.flipbook != null ? cutout.flipbook.name : "no source";
            return cutout.outlinePixels.Count + " verts · " + sourceName;
        }

        private static string DescribeCutoutTooltip(CutoutAsset cutout)
        {
            string tooltipText = AssetDatabase.GetAssetPath(cutout);
            if (!string.IsNullOrEmpty(cutout.outputPath))
            {
                tooltipText += "\n" + cutout.outputPath;
            }

            return tooltipText;
        }
    }
}
