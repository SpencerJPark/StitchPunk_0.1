// Copyright (c) 2026 Spencer Park. All rights reserved.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Draws sheet-island outlines over the canvas, coloured per flipbook group, with click and rubber-band selection.</summary>
    public sealed class CutoutSheetOverlay : VisualElement
    {
        private const float DragThresholdPoints = 4f;
        private const float OutlineWidthPoints = 1f;
        private const float SelectedOutlineWidthPoints = 2f;

        private static readonly Color StillColor = new Color(1f, 1f, 1f, 0.35f);
        private static readonly Color[] GroupColors = new Color[]
        {
            ToolkitPalette.LaneProp,
            ToolkitPalette.LaneEvents,
            ToolkitPalette.MarkerPart,
            ToolkitPalette.LaneCamera,
            ToolkitPalette.MarkerFacing,
            ToolkitPalette.MarkerCut,
        };

        private readonly List<CutoutIsland> islands = new List<CutoutIsland>();
        private readonly List<int> groupIndexPerIsland = new List<int>();
        private readonly List<int> selection = new List<int>();

        private bool pressActive;
        private bool rubberBandActive;
        private bool pressAdditive;
        private Vector2 pressPoint;
        private Vector2 dragPoint;
        private int pressedIslandIndex = -1;
        private List<int> selectionAtPress = new List<int>();

        public event Action<IReadOnlyList<int>> SelectionChanged;

        public Func<Vector2, Vector2> SheetPixelToElement { get; set; }

        public IReadOnlyList<int> Selection => selection;

        public CutoutSheetOverlay()
        {
            pickingMode = PickingMode.Position;
            style.position = Position.Absolute;
            style.left = 0f;
            style.top = 0f;
            style.right = 0f;
            style.bottom = 0f;
            generateVisualContent += PaintContent;
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
        }

        public void SetIslands(IReadOnlyList<CutoutIsland> newIslands, IReadOnlyList<int> newGroupIndexPerIsland)
        {
            islands.Clear();
            groupIndexPerIsland.Clear();
            if (newIslands != null)
            {
                for (int index = 0; index < newIslands.Count; index++)
                {
                    islands.Add(newIslands[index]);
                    bool hasGroup = newGroupIndexPerIsland != null && index < newGroupIndexPerIsland.Count;
                    groupIndexPerIsland.Add(hasGroup ? newGroupIndexPerIsland[index] : -1);
                }
            }

            selection.RemoveAll(selectedIndex => selectedIndex < 0 || selectedIndex >= islands.Count);
            Refresh();
        }

        public void SetSelection(IReadOnlyList<int> islandIndices)
        {
            selection.Clear();
            if (islandIndices != null)
            {
                for (int index = 0; index < islandIndices.Count; index++)
                {
                    int islandIndex = islandIndices[index];
                    if (islandIndex >= 0 && islandIndex < islands.Count && !selection.Contains(islandIndex))
                    {
                        selection.Add(islandIndex);
                    }
                }
            }

            Refresh();
        }

        public void Refresh()
        {
            MarkDirtyRepaint();
        }

        private Rect IslandElementRect(CutoutIsland island)
        {
            if (SheetPixelToElement == null)
            {
                return default;
            }

            Vector2 lowerLeft = SheetPixelToElement(new Vector2(island.boundsPixels.xMin, island.boundsPixels.yMin));
            Vector2 upperRight = SheetPixelToElement(new Vector2(island.boundsPixels.xMax, island.boundsPixels.yMax));
            return Rect.MinMaxRect(
                Mathf.Min(lowerLeft.x, upperRight.x),
                Mathf.Min(lowerLeft.y, upperRight.y),
                Mathf.Max(lowerLeft.x, upperRight.x),
                Mathf.Max(lowerLeft.y, upperRight.y));
        }

        private int IslandIndexAt(Vector2 elementPoint)
        {
            int best = -1;
            float bestArea = float.MaxValue;
            for (int index = 0; index < islands.Count; index++)
            {
                Rect rect = IslandElementRect(islands[index]);
                float area = rect.width * rect.height;
                if (rect.Contains(elementPoint) && area < bestArea)
                {
                    best = index;
                    bestArea = area;
                }
            }

            return best;
        }

        private static bool IsCanvasGesture(IPointerEvent pointerEvent)
        {
            return pointerEvent.button != 0 || pointerEvent.altKey;
        }

        private void OnPointerDown(PointerDownEvent pointerEvent)
        {
            if (IsCanvasGesture(pointerEvent))
            {
                return;
            }

            pressActive = true;
            rubberBandActive = false;
            pressAdditive = pointerEvent.shiftKey;
            pressPoint = pointerEvent.localPosition;
            dragPoint = pressPoint;
            pressedIslandIndex = IslandIndexAt(pressPoint);
            selectionAtPress = new List<int>(selection);
            this.CapturePointer(pointerEvent.pointerId);
            pointerEvent.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent pointerEvent)
        {
            if (!pressActive)
            {
                return;
            }

            dragPoint = pointerEvent.localPosition;
            if (pressedIslandIndex < 0 && !rubberBandActive && Vector2.Distance(pressPoint, dragPoint) >= DragThresholdPoints)
            {
                rubberBandActive = true;
            }

            if (rubberBandActive)
            {
                ApplyRubberBand();
                MarkDirtyRepaint();
                pointerEvent.StopPropagation();
            }
        }

        private void OnPointerUp(PointerUpEvent pointerEvent)
        {
            if (!pressActive || pointerEvent.button != 0)
            {
                return;
            }

            if (!rubberBandActive)
            {
                ApplyClick();
            }

            EndPress();
            this.ReleasePointer(pointerEvent.pointerId);
            pointerEvent.StopPropagation();
        }

        private void OnPointerCaptureOut(PointerCaptureOutEvent pointerEvent)
        {
            EndPress();
        }

        private void EndPress()
        {
            pressActive = false;
            rubberBandActive = false;
            pressedIslandIndex = -1;
            MarkDirtyRepaint();
        }

        private void ApplyClick()
        {
            List<int> next = new List<int>(pressAdditive ? selectionAtPress : new List<int>());
            if (pressedIslandIndex >= 0)
            {
                if (pressAdditive && next.Contains(pressedIslandIndex))
                {
                    next.Remove(pressedIslandIndex);
                }
                else if (!next.Contains(pressedIslandIndex))
                {
                    next.Add(pressedIslandIndex);
                }
            }

            CommitSelection(next);
        }

        private void ApplyRubberBand()
        {
            Rect band = Rect.MinMaxRect(
                Mathf.Min(pressPoint.x, dragPoint.x),
                Mathf.Min(pressPoint.y, dragPoint.y),
                Mathf.Max(pressPoint.x, dragPoint.x),
                Mathf.Max(pressPoint.y, dragPoint.y));
            List<int> next = new List<int>(pressAdditive ? selectionAtPress : new List<int>());
            for (int index = 0; index < islands.Count; index++)
            {
                if (band.Overlaps(IslandElementRect(islands[index])) && !next.Contains(index))
                {
                    next.Add(index);
                }
            }

            CommitSelection(next);
        }

        private void CommitSelection(List<int> next)
        {
            if (SelectionMatches(next))
            {
                return;
            }

            selection.Clear();
            selection.AddRange(next);
            MarkDirtyRepaint();
            SelectionChanged?.Invoke(selection);
        }

        private bool SelectionMatches(List<int> other)
        {
            if (other.Count != selection.Count)
            {
                return false;
            }

            for (int index = 0; index < other.Count; index++)
            {
                if (!selection.Contains(other[index]))
                {
                    return false;
                }
            }

            return true;
        }

        private void PaintContent(MeshGenerationContext context)
        {
            if (SheetPixelToElement == null)
            {
                return;
            }

            Painter2D painter = context.painter2D;
            for (int index = 0; index < islands.Count; index++)
            {
                if (!selection.Contains(index))
                {
                    int groupIndex = groupIndexPerIsland[index];
                    Color color = groupIndex < 0 ? StillColor : GroupColors[groupIndex % GroupColors.Length];
                    StrokeRect(painter, IslandElementRect(islands[index]), color, OutlineWidthPoints);
                }
            }

            for (int selectedIndex = 0; selectedIndex < selection.Count; selectedIndex++)
            {
                Rect rect = IslandElementRect(islands[selection[selectedIndex]]);
                painter.fillColor = ToolkitPalette.BoxSelectFill;
                FillRect(painter, rect);
                StrokeRect(painter, rect, ToolkitPalette.Accent, SelectedOutlineWidthPoints);
            }

            if (rubberBandActive)
            {
                Rect band = Rect.MinMaxRect(
                    Mathf.Min(pressPoint.x, dragPoint.x),
                    Mathf.Min(pressPoint.y, dragPoint.y),
                    Mathf.Max(pressPoint.x, dragPoint.x),
                    Mathf.Max(pressPoint.y, dragPoint.y));
                painter.fillColor = ToolkitPalette.BoxSelectFill;
                FillRect(painter, band);
                StrokeRect(painter, band, ToolkitPalette.BoxSelectOutline, 1f);
            }
        }

        private static void TraceRect(Painter2D painter, Rect rect)
        {
            painter.BeginPath();
            painter.MoveTo(new Vector2(rect.xMin, rect.yMin));
            painter.LineTo(new Vector2(rect.xMax, rect.yMin));
            painter.LineTo(new Vector2(rect.xMax, rect.yMax));
            painter.LineTo(new Vector2(rect.xMin, rect.yMax));
            painter.ClosePath();
        }

        private static void FillRect(Painter2D painter, Rect rect)
        {
            TraceRect(painter, rect);
            painter.Fill();
        }

        private static void StrokeRect(Painter2D painter, Rect rect, Color color, float width)
        {
            painter.strokeColor = color;
            painter.lineWidth = width;
            TraceRect(painter, rect);
            painter.Stroke();
        }
    }
}
