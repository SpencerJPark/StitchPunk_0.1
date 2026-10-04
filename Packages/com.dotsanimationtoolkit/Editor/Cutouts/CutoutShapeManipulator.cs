// Copyright (c) 2026 Spencer Park. All rights reserved.
using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    public sealed class CutoutShapeManipulator : PointerManipulator
    {
        public const float HandlePickRadiusPoints = 8f;
        public const float EdgePickDistancePoints = 6f;

        private enum DragKind { None, Origin, Vertex, ReferenceMove, ReferenceResize, ArtMove, ArtScale, EdgeRubberBand }

        public event Action EditStarting;
        public event Action EditFinished;
        public event Action<string> EdgeRejected;

        private readonly CutoutCanvasElement canvas;
        private DragKind dragKind = DragKind.None;
        private int dragPointerId = -1;
        private int dragVertexIndex = -1;
        private Vector2 referencePressWorld;
        private Rect referenceStartRect;
        private Vector2 artPressWorld;
        private Vector2 artStartLocation;
        private Vector2 scaleHeldPixel;
        private Vector2 scaleHeldWorld;
        private Vector2 scaleDraggedPixel;

        public CutoutShapeManipulator(CutoutCanvasElement canvas)
        {
            this.canvas = canvas;
            target = canvas;
            activators.Add(new ManipulatorActivationFilter { button = MouseButton.LeftMouse });
        }

        public static Vector2 SnapToPixel(Vector2 pixel)
        {
            return new Vector2(Mathf.Round(pixel.x), Mathf.Round(pixel.y));
        }

        public static Vector2 SnapWorldToGrid(Vector2 world, float gridStep)
        {
            return new Vector2(Mathf.Round(world.x / gridStep) * gridStep, Mathf.Round(world.y / gridStep) * gridStep);
        }

        protected override void RegisterCallbacksOnTarget()
        {
            target.focusable = true;
            target.RegisterCallback<PointerDownEvent>(OnPointerDown);
            target.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            target.RegisterCallback<PointerUpEvent>(OnPointerUp);
            target.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            target.RegisterCallback<KeyDownEvent>(OnKeyDown);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<PointerDownEvent>(OnPointerDown);
            target.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
            target.UnregisterCallback<PointerUpEvent>(OnPointerUp);
            target.UnregisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            target.UnregisterCallback<KeyDownEvent>(OnKeyDown);
        }

        private void OnPointerDown(PointerDownEvent pointerEvent)
        {
            if (pointerEvent.button != (int)MouseButton.LeftMouse || pointerEvent.altKey || !canvas.HasCutout)
            {
                return;
            }

            target.Focus();
            switch (canvas.Mode)
            {
                case CutoutCanvasMode.Object:
                    PressInObjectMode(pointerEvent);
                    break;
                case CutoutCanvasMode.EditVertices:
                    PressInEditVerticesMode(pointerEvent);
                    break;
                case CutoutCanvasMode.EditEdges:
                    PressInEditEdgesMode(pointerEvent);
                    break;
            }
        }

        private void PressInObjectMode(PointerDownEvent pointerEvent)
        {
            Vector2 localPosition = pointerEvent.localPosition;
            if (canvas.IsOriginVisible && IsWithinPickRadius(localPosition, canvas.PixelToElement(canvas.OriginPixels)))
            {
                BeginDrag(pointerEvent, DragKind.Origin);
                return;
            }

            if (canvas.IsShapeVisible)
            {
                Vector2Int frameSize = canvas.FrameSize;
                Vector2[] cornerPixels =
                {
                    new Vector2(0f, 0f),
                    new Vector2(frameSize.x, 0f),
                    new Vector2(frameSize.x, frameSize.y),
                    new Vector2(0f, frameSize.y)
                };
                for (int cornerIndex = 0; cornerIndex < cornerPixels.Length; cornerIndex++)
                {
                    if (!IsWithinPickRadius(localPosition, canvas.PixelToElement(cornerPixels[cornerIndex])))
                    {
                        continue;
                    }

                    scaleDraggedPixel = cornerPixels[cornerIndex];
                    scaleHeldPixel = cornerPixels[(cornerIndex + 2) % cornerPixels.Length];
                    scaleHeldWorld = canvas.PixelToWorld(scaleHeldPixel);
                    BeginDrag(pointerEvent, DragKind.ArtScale);
                    return;
                }
            }

            Vector2 pointerWorld = canvas.ElementToWorld(localPosition);
            if (canvas.IsShapeVisible && canvas.ArtRectWorld.Contains(pointerWorld))
            {
                artPressWorld = pointerWorld;
                artStartLocation = canvas.ArtPositionWorld;
                BeginDrag(pointerEvent, DragKind.ArtMove);
                return;
            }

            if (canvas.HasReference && canvas.IsReferenceVisible)
            {
                Rect referenceRect = canvas.ReferenceRectWorld;
                Vector2 topRightElement = canvas.WorldToElement(new Vector2(referenceRect.xMax, referenceRect.yMax));
                if (IsWithinPickRadius(localPosition, topRightElement))
                {
                    referenceStartRect = referenceRect;
                    BeginDrag(pointerEvent, DragKind.ReferenceResize);
                    return;
                }

                if (referenceRect.Contains(pointerWorld))
                {
                    referenceStartRect = referenceRect;
                    referencePressWorld = pointerWorld;
                    BeginDrag(pointerEvent, DragKind.ReferenceMove);
                }
            }
        }

        private void PressInEditVerticesMode(PointerDownEvent pointerEvent)
        {
            if (!canvas.IsShapeVisible)
            {
                return;
            }

            Vector2 localPosition = pointerEvent.localPosition;
            int vertexIndex = FindVertexAt(localPosition);
            if (vertexIndex >= 0)
            {
                canvas.SelectedVertexIndex = vertexIndex;
                dragVertexIndex = vertexIndex;
                BeginDrag(pointerEvent, DragKind.Vertex);
                return;
            }

            Vector2 projectedElementPoint;
            int edgeIndex = FindEdgeAt(localPosition, out projectedElementPoint);
            if (edgeIndex >= 0 && pointerEvent.clickCount == 2)
            {
                EditStarting?.Invoke();
                Vector2 insertedPixel = SnapToPixel(canvas.ElementToPixel(projectedElementPoint));
                int insertedIndex = edgeIndex + 1;
                canvas.OutlinePixels.Insert(insertedIndex, insertedPixel);
                PolygonTriangulator.ShiftEdgesForInsertedVertex(canvas.InnerEdges, insertedIndex);
                canvas.SelectedVertexIndex = insertedIndex;
                canvas.HoveredEdgeIndex = -1;
                canvas.Refresh();
                canvas.RaiseShapeChanged();
                EditFinished?.Invoke();
                pointerEvent.StopPropagation();
                return;
            }

            if (edgeIndex < 0)
            {
                canvas.SelectedVertexIndex = -1;
            }
        }

        private void PressInEditEdgesMode(PointerDownEvent pointerEvent)
        {
            if (!canvas.IsShapeVisible)
            {
                return;
            }

            Vector2 localPosition = pointerEvent.localPosition;
            int vertexIndex = FindVertexAt(localPosition);
            if (vertexIndex >= 0)
            {
                dragVertexIndex = vertexIndex;
                canvas.SetEdgeRubberBand(vertexIndex, localPosition);
                BeginDrag(pointerEvent, DragKind.EdgeRubberBand, raiseEditStarting: false);
                return;
            }

            canvas.SelectedInnerEdgeIndex = FindInnerEdgeAt(localPosition);
        }

        private void BeginDrag(PointerDownEvent pointerEvent, DragKind kind, bool raiseEditStarting = true)
        {
            if (raiseEditStarting)
            {
                EditStarting?.Invoke();
            }

            dragKind = kind;
            dragPointerId = pointerEvent.pointerId;
            target.CapturePointer(pointerEvent.pointerId);
            pointerEvent.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent pointerEvent)
        {
            if (dragKind == DragKind.None)
            {
                UpdateHover(pointerEvent.localPosition);
                return;
            }

            bool useGrid = pointerEvent.ctrlKey || pointerEvent.commandKey;
            Vector2 localPosition = pointerEvent.localPosition;

            switch (dragKind)
            {
                case DragKind.Origin:
                    canvas.SetOriginPixels(SnapPixelFromElement(localPosition, useGrid));
                    break;
                case DragKind.Vertex:
                    if (dragVertexIndex >= 0 && dragVertexIndex < canvas.OutlinePixels.Count)
                    {
                        canvas.OutlinePixels[dragVertexIndex] = SnapPixelFromElement(localPosition, useGrid);
                    }
                    break;
                case DragKind.ReferenceMove:
                    MoveReference(canvas.ElementToWorld(localPosition), useGrid);
                    break;
                case DragKind.ReferenceResize:
                    ResizeReference(canvas.ElementToWorld(localPosition), useGrid);
                    break;
                case DragKind.ArtMove:
                    MoveArt(canvas.ElementToWorld(localPosition), useGrid);
                    break;
                case DragKind.ArtScale:
                    ScaleArt(canvas.ElementToWorld(localPosition), useGrid);
                    break;
                case DragKind.EdgeRubberBand:
                    canvas.SetEdgeRubberBand(dragVertexIndex, localPosition);
                    canvas.HoveredVertexIndex = FindVertexAt(localPosition);
                    pointerEvent.StopPropagation();
                    return;
            }

            canvas.Refresh();
            canvas.RaiseShapeChanged();
            pointerEvent.StopPropagation();
        }

        private void MoveArt(Vector2 pointerWorld, bool useGrid)
        {
            Vector2 newLocation = artStartLocation + (pointerWorld - artPressWorld);
            if (useGrid)
            {
                newLocation = SnapWorldToGrid(newLocation, CutoutCanvasElement.MinorGridWorldUnits);
            }

            canvas.SetArtPositionWorld(newLocation);
        }

        private void ScaleArt(Vector2 pointerWorld, bool useGrid)
        {
            Vector2 pixelDiagonal = scaleDraggedPixel - scaleHeldPixel;
            float pixelDiagonalLength = pixelDiagonal.magnitude;
            if (pixelDiagonalLength <= 0f)
            {
                return;
            }

            float worldDistanceAlongDiagonal = Vector2.Dot(pointerWorld - scaleHeldWorld, pixelDiagonal / pixelDiagonalLength);
            if (worldDistanceAlongDiagonal <= 0.0001f)
            {
                return;
            }

            float newPixelsPerUnit = Mathf.Max(1f, pixelDiagonalLength / worldDistanceAlongDiagonal);
            if (useGrid)
            {
                newPixelsPerUnit = Mathf.Max(1f, Mathf.Round(newPixelsPerUnit));
            }

            canvas.SetPixelsPerUnitHoldingPixel(newPixelsPerUnit, scaleHeldPixel);
        }

        private void MoveReference(Vector2 pointerWorld, bool useGrid)
        {
            Vector2 newPosition = referenceStartRect.position + (pointerWorld - referencePressWorld);
            if (useGrid)
            {
                newPosition = SnapWorldToGrid(newPosition, CutoutCanvasElement.MinorGridWorldUnits);
            }

            canvas.ReferenceRectWorld = new Rect(newPosition, referenceStartRect.size);
        }

        private void ResizeReference(Vector2 pointerWorld, bool useGrid)
        {
            if (useGrid)
            {
                pointerWorld = SnapWorldToGrid(pointerWorld, CutoutCanvasElement.MinorGridWorldUnits);
            }

            float aspectHeightOverWidth = referenceStartRect.width > 0f ? referenceStartRect.height / referenceStartRect.width : 1f;
            float newWidth = Mathf.Max(0.01f, pointerWorld.x - referenceStartRect.xMin);
            canvas.ReferenceRectWorld = new Rect(referenceStartRect.xMin, referenceStartRect.yMin, newWidth, newWidth * aspectHeightOverWidth);
        }

        private void OnPointerUp(PointerUpEvent pointerEvent)
        {
            if (dragKind == DragKind.None || pointerEvent.pointerId != dragPointerId)
            {
                return;
            }

            if (dragKind == DragKind.EdgeRubberBand)
            {
                TryAddInnerEdgeFromRubberBand(FindVertexAt(pointerEvent.localPosition));
            }

            if (target.HasPointerCapture(dragPointerId))
            {
                target.ReleasePointer(dragPointerId);
            }

            FinishDrag();
            pointerEvent.StopPropagation();
        }

        private void TryAddInnerEdgeFromRubberBand(int releasedVertexIndex)
        {
            int startVertexIndex = dragVertexIndex;
            if (releasedVertexIndex < 0 || releasedVertexIndex == startVertexIndex)
            {
                return;
            }

            string rejectionReason;
            if (!PolygonTriangulator.IsValidInnerEdge(canvas.OutlinePixels, canvas.InnerEdges, startVertexIndex, releasedVertexIndex, out rejectionReason))
            {
                EdgeRejected?.Invoke(rejectionReason);
                return;
            }

            EditStarting?.Invoke();
            canvas.InnerEdges.Add(new Vector2Int(Mathf.Min(startVertexIndex, releasedVertexIndex), Mathf.Max(startVertexIndex, releasedVertexIndex)));
            canvas.Refresh();
            canvas.RaiseShapeChanged();
            EditFinished?.Invoke();
        }

        private void OnPointerCaptureOut(PointerCaptureOutEvent captureEvent)
        {
            FinishDrag();
        }

        private void FinishDrag()
        {
            if (dragKind == DragKind.None)
            {
                return;
            }

            DragKind finishedKind = dragKind;
            dragKind = DragKind.None;
            dragPointerId = -1;
            dragVertexIndex = -1;
            if (finishedKind == DragKind.EdgeRubberBand)
            {
                canvas.ClearEdgeRubberBand();
                return;
            }

            EditFinished?.Invoke();
        }

        private void OnKeyDown(KeyDownEvent keyEvent)
        {
            if (keyEvent.keyCode != KeyCode.Delete && keyEvent.keyCode != KeyCode.Backspace)
            {
                return;
            }

            if (!canvas.HasCutout || dragKind != DragKind.None)
            {
                return;
            }

            bool handled = false;
            if (canvas.Mode == CutoutCanvasMode.EditVertices)
            {
                handled = DeleteSelectedVertex();
            }
            else if (canvas.Mode == CutoutCanvasMode.EditEdges)
            {
                handled = DeleteSelectedInnerEdge();
            }

            if (handled)
            {
                keyEvent.StopPropagation();
            }
        }

        private bool DeleteSelectedVertex()
        {
            int selectedIndex = canvas.SelectedVertexIndex;
            List<Vector2> outline = canvas.OutlinePixels;
            if (selectedIndex < 0 || selectedIndex >= outline.Count || outline.Count <= CutoutAsset.MinimumVertexCount)
            {
                return false;
            }

            EditStarting?.Invoke();
            PolygonTriangulator.RemoveEdgesForDeletedVertex(canvas.InnerEdges, selectedIndex);
            outline.RemoveAt(selectedIndex);
            canvas.SelectedVertexIndex = -1;
            canvas.HoveredVertexIndex = -1;
            canvas.HoveredEdgeIndex = -1;
            canvas.Refresh();
            canvas.RaiseShapeChanged();
            EditFinished?.Invoke();
            return true;
        }

        private bool DeleteSelectedInnerEdge()
        {
            int selectedIndex = canvas.SelectedInnerEdgeIndex;
            if (selectedIndex < 0 || selectedIndex >= canvas.InnerEdges.Count)
            {
                return false;
            }

            EditStarting?.Invoke();
            canvas.InnerEdges.RemoveAt(selectedIndex);
            canvas.SelectedInnerEdgeIndex = -1;
            canvas.HoveredInnerEdgeIndex = -1;
            canvas.Refresh();
            canvas.RaiseShapeChanged();
            EditFinished?.Invoke();
            return true;
        }

        private void UpdateHover(Vector2 localPosition)
        {
            int hoveredVertex = -1;
            int hoveredEdge = -1;
            int hoveredInnerEdge = -1;
            if (canvas.HasCutout && canvas.IsShapeVisible)
            {
                if (canvas.Mode == CutoutCanvasMode.EditVertices)
                {
                    hoveredVertex = FindVertexAt(localPosition);
                    if (hoveredVertex < 0)
                    {
                        Vector2 unusedProjection;
                        hoveredEdge = FindEdgeAt(localPosition, out unusedProjection);
                    }
                }
                else if (canvas.Mode == CutoutCanvasMode.EditEdges)
                {
                    hoveredVertex = FindVertexAt(localPosition);
                    if (hoveredVertex < 0)
                    {
                        hoveredInnerEdge = FindInnerEdgeAt(localPosition);
                    }
                }
            }

            if (canvas.HoveredVertexIndex != hoveredVertex)
            {
                canvas.HoveredVertexIndex = hoveredVertex;
            }

            if (canvas.HoveredEdgeIndex != hoveredEdge)
            {
                canvas.HoveredEdgeIndex = hoveredEdge;
            }

            if (canvas.HoveredInnerEdgeIndex != hoveredInnerEdge)
            {
                canvas.HoveredInnerEdgeIndex = hoveredInnerEdge;
            }
        }

        private Vector2 SnapPixelFromElement(Vector2 elementPoint, bool useGrid)
        {
            Vector2 pixel = canvas.ElementToPixel(elementPoint);
            if (!useGrid)
            {
                return SnapToPixel(pixel);
            }

            Vector2 snappedWorld = SnapWorldToGrid(canvas.PixelToWorld(pixel), CutoutCanvasElement.MinorGridWorldUnits);
            return canvas.WorldToPixel(snappedWorld);
        }

        private static bool IsWithinPickRadius(Vector2 elementPoint, Vector2 handleElementPoint)
        {
            return (elementPoint - handleElementPoint).sqrMagnitude <= HandlePickRadiusPoints * HandlePickRadiusPoints;
        }

        private int FindVertexAt(Vector2 localPosition)
        {
            List<Vector2> outline = canvas.OutlinePixels;
            int closestIndex = -1;
            float closestSquaredDistance = HandlePickRadiusPoints * HandlePickRadiusPoints;
            for (int vertexIndex = 0; vertexIndex < outline.Count; vertexIndex++)
            {
                float squaredDistance = (localPosition - canvas.PixelToElement(outline[vertexIndex])).sqrMagnitude;
                if (squaredDistance <= closestSquaredDistance)
                {
                    closestSquaredDistance = squaredDistance;
                    closestIndex = vertexIndex;
                }
            }

            return closestIndex;
        }

        private int FindInnerEdgeAt(Vector2 localPosition)
        {
            List<Vector2> outline = canvas.OutlinePixels;
            List<Vector2Int> innerEdges = canvas.InnerEdges;
            int closestIndex = -1;
            float closestDistance = EdgePickDistancePoints;
            for (int edgeIndex = 0; edgeIndex < innerEdges.Count; edgeIndex++)
            {
                Vector2Int edge = innerEdges[edgeIndex];
                if (edge.x < 0 || edge.y < 0 || edge.x >= outline.Count || edge.y >= outline.Count)
                {
                    continue;
                }

                Vector2 projectedUnused;
                float distance = DistanceToSegment(localPosition, canvas.PixelToElement(outline[edge.x]), canvas.PixelToElement(outline[edge.y]), out projectedUnused);
                if (distance <= closestDistance)
                {
                    closestDistance = distance;
                    closestIndex = edgeIndex;
                }
            }

            return closestIndex;
        }

        private int FindEdgeAt(Vector2 localPosition, out Vector2 projectedElementPoint)
        {
            List<Vector2> outline = canvas.OutlinePixels;
            int closestIndex = -1;
            float closestDistance = EdgePickDistancePoints;
            projectedElementPoint = Vector2.zero;
            for (int edgeIndex = 0; edgeIndex < outline.Count; edgeIndex++)
            {
                Vector2 segmentStart = canvas.PixelToElement(outline[edgeIndex]);
                Vector2 segmentEnd = canvas.PixelToElement(outline[(edgeIndex + 1) % outline.Count]);
                Vector2 projected;
                float distance = DistanceToSegment(localPosition, segmentStart, segmentEnd, out projected);
                if (distance <= closestDistance)
                {
                    closestDistance = distance;
                    closestIndex = edgeIndex;
                    projectedElementPoint = projected;
                }
            }

            return closestIndex;
        }

        private static float DistanceToSegment(Vector2 point, Vector2 segmentStart, Vector2 segmentEnd, out Vector2 projected)
        {
            Vector2 segment = segmentEnd - segmentStart;
            float segmentLengthSquared = segment.sqrMagnitude;
            float along = segmentLengthSquared > 0f
                ? Mathf.Clamp01(Vector2.Dot(point - segmentStart, segment) / segmentLengthSquared)
                : 0f;
            projected = segmentStart + segment * along;
            return (point - projected).magnitude;
        }
    }
}
