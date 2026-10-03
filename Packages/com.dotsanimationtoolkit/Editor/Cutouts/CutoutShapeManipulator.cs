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

        private enum DragKind { None, Origin, Vertex, ReferenceMove, ReferenceResize }

        public event Action EditStarting;
        public event Action EditFinished;

        private readonly CutoutCanvasElement canvas;
        private DragKind dragKind = DragKind.None;
        private int dragPointerId = -1;
        private int dragVertexIndex = -1;
        private Vector2 referencePressWorld;
        private Rect referenceStartRect;

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
            Vector2 localPosition = pointerEvent.localPosition;
            List<Vector2> outline = canvas.OutlinePixels;

            if (canvas.IsOriginVisible && IsWithinPickRadius(localPosition, canvas.PixelToElement(canvas.OriginPixels)))
            {
                BeginDrag(pointerEvent, DragKind.Origin);
                return;
            }

            if (canvas.IsShapeVisible)
            {
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
                if (edgeIndex >= 0)
                {
                    EditStarting?.Invoke();
                    Vector2 insertedPixel = SnapToPixel(canvas.ElementToPixel(projectedElementPoint));
                    int insertedIndex = edgeIndex + 1;
                    outline.Insert(insertedIndex, insertedPixel);
                    canvas.SelectedVertexIndex = insertedIndex;
                    canvas.HoveredEdgeIndex = -1;
                    dragVertexIndex = insertedIndex;
                    canvas.Refresh();
                    canvas.RaiseShapeChanged();
                    BeginDrag(pointerEvent, DragKind.Vertex, raiseEditStarting: false);
                    return;
                }
            }

            if (canvas.HasReference && canvas.IsReferenceVisible)
            {
                Rect referenceRect = canvas.ReferenceRectWorld;
                Vector2 topRightElement = canvas.WorldToElement(new Vector2(referenceRect.xMax, referenceRect.yMax));
                Vector2 pointerWorld = canvas.ElementToWorld(localPosition);
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
                    return;
                }
            }

            canvas.SelectedVertexIndex = -1;
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
            }

            canvas.Refresh();
            canvas.RaiseShapeChanged();
            pointerEvent.StopPropagation();
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

            if (target.HasPointerCapture(dragPointerId))
            {
                target.ReleasePointer(dragPointerId);
            }

            FinishDrag();
            pointerEvent.StopPropagation();
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

            dragKind = DragKind.None;
            dragPointerId = -1;
            dragVertexIndex = -1;
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

            int selectedIndex = canvas.SelectedVertexIndex;
            List<Vector2> outline = canvas.OutlinePixels;
            if (selectedIndex < 0 || selectedIndex >= outline.Count || outline.Count <= CutoutAsset.MinimumVertexCount)
            {
                return;
            }

            EditStarting?.Invoke();
            outline.RemoveAt(selectedIndex);
            canvas.SelectedVertexIndex = -1;
            canvas.HoveredVertexIndex = -1;
            canvas.HoveredEdgeIndex = -1;
            canvas.Refresh();
            canvas.RaiseShapeChanged();
            EditFinished?.Invoke();
            keyEvent.StopPropagation();
        }

        private void UpdateHover(Vector2 localPosition)
        {
            int hoveredVertex = -1;
            int hoveredEdge = -1;
            if (canvas.HasCutout && canvas.IsShapeVisible)
            {
                hoveredVertex = FindVertexAt(localPosition);
                if (hoveredVertex < 0)
                {
                    Vector2 unusedProjection;
                    hoveredEdge = FindEdgeAt(localPosition, out unusedProjection);
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
                Vector2 segment = segmentEnd - segmentStart;
                float segmentLengthSquared = segment.sqrMagnitude;
                float along = segmentLengthSquared > 0f
                    ? Mathf.Clamp01(Vector2.Dot(localPosition - segmentStart, segment) / segmentLengthSquared)
                    : 0f;
                Vector2 projected = segmentStart + segment * along;
                float distance = (localPosition - projected).magnitude;
                if (distance <= closestDistance)
                {
                    closestDistance = distance;
                    closestIndex = edgeIndex;
                    projectedElementPoint = projected;
                }
            }

            return closestIndex;
        }
    }
}
