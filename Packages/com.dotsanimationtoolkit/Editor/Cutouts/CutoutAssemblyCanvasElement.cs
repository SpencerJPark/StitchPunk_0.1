// Copyright (c) 2026 Spencer Park. All rights reserved.
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// The Assemble-mode canvas of the Cutouts tab: textured pieces on a pan/zoom grid, with
    /// move, rotate, parent and draw-order gestures that edit a CutoutAssemblyAsset undoably.
    /// </summary>
    public sealed class CutoutAssemblyCanvasElement : VisualElement
    {
        private const float DefaultPointsPerWorldUnit = 100f;
        private const float MinimumPointsPerWorldUnit = 4f;
        private const float MaximumPointsPerWorldUnit = 4000f;
        private const float FrameAllMarginFactor = 1.2f;
        private const float OriginDotRadiusPoints = 5f;
        private const float OriginDotHitRadiusPoints = 8f;
        private const float RotationRingRadiusPoints = 56f;
        private const float RotationRingHitHalfWidthPoints = 6f;
        private const float RotationSnapDegrees = 90f;
        private const float DragThresholdPoints = 3f;

        private enum DragKind
        {
            None,
            Pan,
            Move,
            Rotate,
            Parent
        }

        public event Action<int> PieceSelected;
        public event Action AssemblyEdited;

        private readonly VisualElement gridLayer;
        private readonly VisualElement artLayer;
        private readonly VisualElement overlayLayer;
        private readonly List<int> triangleScratch = new List<int>();

        private CutoutAssemblyAsset assembly;
        private int selectedPieceIndex = -1;
        private Vector2 viewCentreWorld;
        private float zoomPointsPerWorldUnit = DefaultPointsPerWorldUnit;
        private bool needsFrameAllOnLayout;

        private DragKind dragKind;
        private int dragPointerId;
        private int dragPieceIndex = -1;
        private Vector2 dragStartElement;
        private Vector2 dragCurrentElement;
        private Vector2 dragPieceStartWorld;
        private float dragStartPointerAngle;
        private float dragStartPieceRotation;
        private bool dragHasMoved;
        private bool dragRecordedUndo;

        public int SelectedPieceIndex => selectedPieceIndex;

        public CutoutAssemblyCanvasElement()
        {
            AddToClassList("cutout-canvas");
            focusable = true;
            pickingMode = PickingMode.Position;
            style.flexGrow = 1f;
            style.overflow = Overflow.Hidden;

            gridLayer = MakeFullSizeLayer();
            gridLayer.generateVisualContent += OnGenerateGrid;
            Add(gridLayer);
            artLayer = MakeFullSizeLayer();
            artLayer.generateVisualContent += OnGenerateArt;
            Add(artLayer);
            overlayLayer = MakeFullSizeLayer();
            overlayLayer.generateVisualContent += OnGenerateOverlay;
            Add(overlayLayer);

            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            RegisterCallback<WheelEvent>(OnWheel);
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            RegisterCallback<KeyDownEvent>(OnKeyDown);
        }

        public void SetAssembly(CutoutAssemblyAsset newAssembly)
        {
            assembly = newAssembly;
            selectedPieceIndex = -1;
            Repaint();
        }

        public void SetSelectedPiece(int pieceIndex)
        {
            selectedPieceIndex = IsValidIndex(pieceIndex) ? pieceIndex : -1;
            Repaint();
        }

        public void AddPiece(CutoutAsset cutout, Vector2 worldPosition)
        {
            if (assembly == null || cutout == null)
            {
                return;
            }

            int highestDrawOrder = -1;
            for (int index = 0; index < assembly.pieces.Count; index++)
            {
                highestDrawOrder = Mathf.Max(highestDrawOrder, assembly.pieces[index].drawOrder);
            }

            RecordEdit("Add Cutout Piece");
            CutoutAssemblyPiece piece = new CutoutAssemblyPiece();
            piece.name = cutout.name;
            piece.cutout = cutout;
            piece.positionWorld = worldPosition;
            piece.drawOrder = highestDrawOrder + 1;
            assembly.pieces.Add(piece);
            FinishEdit();
            SelectPiece(assembly.pieces.Count - 1);
        }

        public Vector2 WorldToElement(Vector2 world)
        {
            Vector2 elementCentre = new Vector2(layout.width * 0.5f, layout.height * 0.5f);
            Vector2 offsetWorld = world - viewCentreWorld;
            return elementCentre + new Vector2(offsetWorld.x, -offsetWorld.y) * zoomPointsPerWorldUnit;
        }

        public Vector2 ElementToWorld(Vector2 elementPoint)
        {
            Vector2 elementCentre = new Vector2(layout.width * 0.5f, layout.height * 0.5f);
            Vector2 offsetElement = (elementPoint - elementCentre) / zoomPointsPerWorldUnit;
            return viewCentreWorld + new Vector2(offsetElement.x, -offsetElement.y);
        }

        public void FrameAll()
        {
            if (layout.width < 1f || layout.height < 1f)
            {
                needsFrameAllOnLayout = true;
                return;
            }

            needsFrameAllOnLayout = false;
            Rect boundsWorld = ComputePiecesBoundsWorld();
            float fitWidth = Mathf.Max(boundsWorld.width, 0.0001f) * FrameAllMarginFactor;
            float fitHeight = Mathf.Max(boundsWorld.height, 0.0001f) * FrameAllMarginFactor;
            float fittedZoom = Mathf.Min(layout.width / fitWidth, layout.height / fitHeight);
            zoomPointsPerWorldUnit = Mathf.Clamp(fittedZoom, MinimumPointsPerWorldUnit, MaximumPointsPerWorldUnit);
            viewCentreWorld = boundsWorld.center;
            Repaint();
        }

        private Rect ComputePiecesBoundsWorld()
        {
            Rect boundsWorld = Rect.MinMaxRect(-1f, -1f, 1f, 1f);
            bool hasBounds = false;
            if (assembly == null)
            {
                return boundsWorld;
            }

            for (int index = 0; index < assembly.pieces.Count; index++)
            {
                Vector2[] corners = GetPieceRectCornersWorld(assembly.pieces[index]);
                for (int cornerIndex = 0; cornerIndex < corners.Length; cornerIndex++)
                {
                    Vector2 corner = corners[cornerIndex];
                    boundsWorld = hasBounds
                        ? Rect.MinMaxRect(Mathf.Min(boundsWorld.xMin, corner.x), Mathf.Min(boundsWorld.yMin, corner.y), Mathf.Max(boundsWorld.xMax, corner.x), Mathf.Max(boundsWorld.yMax, corner.y))
                        : Rect.MinMaxRect(corner.x, corner.y, corner.x, corner.y);
                    hasBounds = true;
                }
            }
            return boundsWorld;
        }

        private static VisualElement MakeFullSizeLayer()
        {
            VisualElement layer = new VisualElement();
            layer.pickingMode = PickingMode.Ignore;
            layer.style.position = Position.Absolute;
            layer.style.left = 0f;
            layer.style.top = 0f;
            layer.style.right = 0f;
            layer.style.bottom = 0f;
            return layer;
        }

        private void Repaint()
        {
            gridLayer.MarkDirtyRepaint();
            artLayer.MarkDirtyRepaint();
            overlayLayer.MarkDirtyRepaint();
        }

        private bool IsValidIndex(int pieceIndex)
        {
            return assembly != null && pieceIndex >= 0 && pieceIndex < assembly.pieces.Count;
        }

        private void SelectPiece(int pieceIndex)
        {
            int resolved = IsValidIndex(pieceIndex) ? pieceIndex : -1;
            bool changed = resolved != selectedPieceIndex;
            selectedPieceIndex = resolved;
            Repaint();
            if (changed)
            {
                PieceSelected?.Invoke(selectedPieceIndex);
            }
        }

        private void RecordEdit(string undoName)
        {
            Undo.RecordObject(assembly, undoName);
        }

        private void FinishEdit()
        {
            EditorUtility.SetDirty(assembly);
            Repaint();
            AssemblyEdited?.Invoke();
        }

        private void OnGeometryChanged(GeometryChangedEvent geometryChangedEvent)
        {
            if (needsFrameAllOnLayout)
            {
                FrameAll();
                return;
            }
            Repaint();
        }

        private static float TotalRotationDegrees(CutoutAssemblyPiece piece)
        {
            return piece.cutout.rotationDegrees + piece.extraRotationDegrees;
        }

        // Piece pixels are relative to the origin, scaled to world units, rotated, then placed.
        private static Vector2 PixelToWorld(CutoutAssemblyPiece piece, Vector2 pixel)
        {
            Vector2 local = (pixel - piece.cutout.originPixels) / Mathf.Max(piece.cutout.pixelsPerUnit, 0.0001f);
            return piece.positionWorld + Rotate(local, TotalRotationDegrees(piece));
        }

        private static Vector2 WorldToPixel(CutoutAssemblyPiece piece, Vector2 world)
        {
            Vector2 local = Rotate(world - piece.positionWorld, -TotalRotationDegrees(piece));
            return local * piece.cutout.pixelsPerUnit + piece.cutout.originPixels;
        }

        private static Vector2 Rotate(Vector2 vector, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float cosine = Mathf.Cos(radians);
            float sine = Mathf.Sin(radians);
            return new Vector2(vector.x * cosine - vector.y * sine, vector.x * sine + vector.y * cosine);
        }

        private static bool IsDrawable(CutoutAssemblyPiece piece)
        {
            return piece != null && piece.cutout != null;
        }

        private static Vector2[] GetPieceRectCornersWorld(CutoutAssemblyPiece piece)
        {
            if (!IsDrawable(piece))
            {
                return new Vector2[0];
            }
            RectInt rect = piece.cutout.ResolveSourceRectPixels();
            return new Vector2[]
            {
                PixelToWorld(piece, Vector2.zero),
                PixelToWorld(piece, new Vector2(rect.width, 0f)),
                PixelToWorld(piece, new Vector2(rect.width, rect.height)),
                PixelToWorld(piece, new Vector2(0f, rect.height))
            };
        }

        private List<int> GetPiecesInDrawOrder()
        {
            List<int> order = new List<int>();
            if (assembly == null)
            {
                return order;
            }
            for (int index = 0; index < assembly.pieces.Count; index++)
            {
                order.Add(index);
            }
            order.Sort((left, right) =>
            {
                int byDrawOrder = assembly.pieces[left].drawOrder.CompareTo(assembly.pieces[right].drawOrder);
                return byDrawOrder != 0 ? byDrawOrder : left.CompareTo(right);
            });
            return order;
        }

        private static bool PointInPolygon(IReadOnlyList<Vector2> polygon, Vector2 point)
        {
            bool inside = false;
            for (int index = 0, previous = polygon.Count - 1; index < polygon.Count; previous = index++)
            {
                Vector2 current = polygon[index];
                Vector2 last = polygon[previous];
                if ((current.y > point.y) != (last.y > point.y)
                    && point.x < (last.x - current.x) * (point.y - current.y) / (last.y - current.y) + current.x)
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        private static bool PieceContainsWorldPoint(CutoutAssemblyPiece piece, Vector2 world)
        {
            if (!IsDrawable(piece))
            {
                return false;
            }
            Vector2 pixel = WorldToPixel(piece, world);
            List<Vector2> outline = piece.cutout.outlinePixels;
            if (outline != null && outline.Count >= 3)
            {
                return PointInPolygon(outline, pixel);
            }
            RectInt rect = piece.cutout.ResolveSourceRectPixels();
            return pixel.x >= 0f && pixel.y >= 0f && pixel.x <= rect.width && pixel.y <= rect.height;
        }

        private int FindTopmostPieceAtElement(Vector2 elementPoint, int excludedPieceIndex)
        {
            Vector2 world = ElementToWorld(elementPoint);
            List<int> order = GetPiecesInDrawOrder();
            for (int orderIndex = order.Count - 1; orderIndex >= 0; orderIndex--)
            {
                int pieceIndex = order[orderIndex];
                if (pieceIndex != excludedPieceIndex && PieceContainsWorldPoint(assembly.pieces[pieceIndex], world))
                {
                    return pieceIndex;
                }
            }
            return -1;
        }

        private int FindOriginDotAtElement(Vector2 elementPoint)
        {
            List<int> order = GetPiecesInDrawOrder();
            for (int orderIndex = order.Count - 1; orderIndex >= 0; orderIndex--)
            {
                CutoutAssemblyPiece piece = assembly.pieces[order[orderIndex]];
                if (IsDrawable(piece) && Vector2.Distance(WorldToElement(piece.positionWorld), elementPoint) <= OriginDotHitRadiusPoints)
                {
                    return order[orderIndex];
                }
            }
            return -1;
        }

        private bool IsOnRotationRing(Vector2 elementPoint)
        {
            if (!IsValidIndex(selectedPieceIndex) || !IsDrawable(assembly.pieces[selectedPieceIndex]))
            {
                return false;
            }
            float distance = Vector2.Distance(WorldToElement(assembly.pieces[selectedPieceIndex].positionWorld), elementPoint);
            return Mathf.Abs(distance - RotationRingRadiusPoints) <= RotationRingHitHalfWidthPoints;
        }

        private float PointerAngleAroundPiece(int pieceIndex, Vector2 elementPoint)
        {
            Vector2 offsetWorld = ElementToWorld(elementPoint) - assembly.pieces[pieceIndex].positionWorld;
            return Mathf.Atan2(offsetWorld.y, offsetWorld.x) * Mathf.Rad2Deg;
        }

        private bool WouldCreateParentCycle(int childIndex, int newParentIndex)
        {
            int cursor = newParentIndex;
            int guard = assembly.pieces.Count + 1;
            while (cursor >= 0 && guard-- > 0)
            {
                if (cursor == childIndex)
                {
                    return true;
                }
                cursor = assembly.pieces[cursor].parentPieceIndex;
            }
            return false;
        }

        private void OnWheel(WheelEvent wheelEvent)
        {
            float clampedDelta = Mathf.Clamp(wheelEvent.delta.y, -3f, 3f);
            Vector2 cursorElement = wheelEvent.localMousePosition;
            Vector2 worldUnderCursor = ElementToWorld(cursorElement);
            zoomPointsPerWorldUnit = Mathf.Clamp(
                zoomPointsPerWorldUnit * Mathf.Pow(1.1f, -clampedDelta), MinimumPointsPerWorldUnit, MaximumPointsPerWorldUnit);

            Vector2 elementCentre = new Vector2(layout.width * 0.5f, layout.height * 0.5f);
            Vector2 cursorOffsetWorld = (cursorElement - elementCentre) / zoomPointsPerWorldUnit;
            viewCentreWorld = worldUnderCursor - new Vector2(cursorOffsetWorld.x, -cursorOffsetWorld.y);
            Repaint();
            wheelEvent.StopPropagation();
        }

        private void OnPointerDown(PointerDownEvent pointerDownEvent)
        {
            Focus();
            bool isMiddleButton = pointerDownEvent.button == 2;
            bool isAltLeftButton = pointerDownEvent.button == 0 && pointerDownEvent.altKey;
            if (isMiddleButton || isAltLeftButton)
            {
                BeginDrag(DragKind.Pan, pointerDownEvent);
                pointerDownEvent.StopPropagation();
                return;
            }

            if (pointerDownEvent.button != 0 || assembly == null)
            {
                return;
            }

            Vector2 elementPoint = pointerDownEvent.localPosition;
            int originDotPieceIndex = FindOriginDotAtElement(elementPoint);
            if (originDotPieceIndex >= 0)
            {
                SelectPiece(originDotPieceIndex);
                BeginDrag(DragKind.Parent, pointerDownEvent);
                dragPieceIndex = originDotPieceIndex;
            }
            else if (IsOnRotationRing(elementPoint))
            {
                BeginDrag(DragKind.Rotate, pointerDownEvent);
                dragPieceIndex = selectedPieceIndex;
                dragStartPointerAngle = PointerAngleAroundPiece(dragPieceIndex, elementPoint);
                dragStartPieceRotation = assembly.pieces[dragPieceIndex].extraRotationDegrees;
            }
            else
            {
                int hitPieceIndex = FindTopmostPieceAtElement(elementPoint, -1);
                SelectPiece(hitPieceIndex);
                if (hitPieceIndex >= 0)
                {
                    BeginDrag(DragKind.Move, pointerDownEvent);
                    dragPieceIndex = hitPieceIndex;
                    dragPieceStartWorld = assembly.pieces[hitPieceIndex].positionWorld;
                }
            }
            pointerDownEvent.StopPropagation();
        }

        private void BeginDrag(DragKind kind, PointerDownEvent pointerDownEvent)
        {
            dragKind = kind;
            dragPointerId = pointerDownEvent.pointerId;
            dragStartElement = pointerDownEvent.localPosition;
            dragCurrentElement = dragStartElement;
            dragHasMoved = false;
            dragRecordedUndo = false;
            dragPieceIndex = -1;
            this.CapturePointer(dragPointerId);
        }

        private void OnPointerMove(PointerMoveEvent pointerMoveEvent)
        {
            if (dragKind == DragKind.None || pointerMoveEvent.pointerId != dragPointerId)
            {
                return;
            }

            dragCurrentElement = pointerMoveEvent.localPosition;
            if (dragKind == DragKind.Pan)
            {
                Vector2 deltaElement = pointerMoveEvent.deltaPosition;
                viewCentreWorld += new Vector2(-deltaElement.x, deltaElement.y) / zoomPointsPerWorldUnit;
                Repaint();
                pointerMoveEvent.StopPropagation();
                return;
            }

            if (!dragHasMoved && Vector2.Distance(dragCurrentElement, dragStartElement) < DragThresholdPoints)
            {
                return;
            }
            dragHasMoved = true;

            if (dragKind == DragKind.Move)
            {
                RecordDragUndoOnce("Move Cutout Piece");
                Vector2 deltaWorld = ElementToWorld(dragCurrentElement) - ElementToWorld(dragStartElement);
                assembly.pieces[dragPieceIndex].positionWorld = dragPieceStartWorld + deltaWorld;
                EditorUtility.SetDirty(assembly);
            }
            else if (dragKind == DragKind.Rotate)
            {
                RecordDragUndoOnce("Rotate Cutout Piece");
                float pointerAngle = PointerAngleAroundPiece(dragPieceIndex, dragCurrentElement);
                float rotation = dragStartPieceRotation + Mathf.DeltaAngle(dragStartPointerAngle, pointerAngle);
                if (!pointerMoveEvent.actionKey)
                {
                    rotation = Mathf.Round(rotation / RotationSnapDegrees) * RotationSnapDegrees;
                }
                assembly.pieces[dragPieceIndex].extraRotationDegrees = rotation;
                EditorUtility.SetDirty(assembly);
            }
            Repaint();
            pointerMoveEvent.StopPropagation();
        }

        private void RecordDragUndoOnce(string undoName)
        {
            if (!dragRecordedUndo)
            {
                RecordEdit(undoName);
                dragRecordedUndo = true;
            }
        }

        private void OnPointerUp(PointerUpEvent pointerUpEvent)
        {
            if (dragKind == DragKind.None || pointerUpEvent.pointerId != dragPointerId)
            {
                return;
            }

            DragKind finishedKind = dragKind;
            dragKind = DragKind.None;
            this.ReleasePointer(dragPointerId);
            if (finishedKind == DragKind.Parent && dragHasMoved)
            {
                CompleteParentDrop(pointerUpEvent.localPosition);
            }
            else if (dragRecordedUndo)
            {
                FinishEdit();
            }
            dragPieceIndex = -1;
            Repaint();
            pointerUpEvent.StopPropagation();
        }

        private void CompleteParentDrop(Vector2 dropElement)
        {
            int newParentIndex = FindTopmostPieceAtElement(dropElement, dragPieceIndex);
            bool isCycle = newParentIndex >= 0 && WouldCreateParentCycle(dragPieceIndex, newParentIndex);
            if (isCycle || assembly.pieces[dragPieceIndex].parentPieceIndex == newParentIndex)
            {
                return;
            }
            RecordEdit("Reparent Cutout Piece");
            assembly.pieces[dragPieceIndex].parentPieceIndex = newParentIndex;
            FinishEdit();
        }

        private void OnPointerCaptureOut(PointerCaptureOutEvent captureOutEvent)
        {
            dragKind = DragKind.None;
            dragPieceIndex = -1;
            Repaint();
        }

        private void OnKeyDown(KeyDownEvent keyDownEvent)
        {
            if (!IsValidIndex(selectedPieceIndex))
            {
                return;
            }

            if (keyDownEvent.character == '[' || keyDownEvent.keyCode == KeyCode.LeftBracket)
            {
                SwapDrawOrderWithNeighbour(-1);
            }
            else if (keyDownEvent.character == ']' || keyDownEvent.keyCode == KeyCode.RightBracket)
            {
                SwapDrawOrderWithNeighbour(1);
            }
            else if (keyDownEvent.keyCode == KeyCode.Delete || keyDownEvent.keyCode == KeyCode.Backspace)
            {
                DeleteSelectedPiece();
            }
            else
            {
                return;
            }
            keyDownEvent.StopPropagation();
        }

        private void SwapDrawOrderWithNeighbour(int direction)
        {
            List<int> order = GetPiecesInDrawOrder();
            int neighbourPosition = order.IndexOf(selectedPieceIndex) + direction;
            if (neighbourPosition < 0 || neighbourPosition >= order.Count)
            {
                return;
            }

            CutoutAssemblyPiece selected = assembly.pieces[selectedPieceIndex];
            CutoutAssemblyPiece neighbour = assembly.pieces[order[neighbourPosition]];
            RecordEdit("Change Cutout Draw Order");
            if (selected.drawOrder == neighbour.drawOrder)
            {
                selected.drawOrder += direction;
            }
            else
            {
                int selectedDrawOrder = selected.drawOrder;
                selected.drawOrder = neighbour.drawOrder;
                neighbour.drawOrder = selectedDrawOrder;
            }
            FinishEdit();
        }

        private void DeleteSelectedPiece()
        {
            int removedIndex = selectedPieceIndex;
            RecordEdit("Delete Cutout Piece");
            int removedParentIndex = assembly.pieces[removedIndex].parentPieceIndex;
            int inheritedParentIndex = removedParentIndex > removedIndex ? removedParentIndex - 1 : removedParentIndex;
            assembly.pieces.RemoveAt(removedIndex);
            for (int index = 0; index < assembly.pieces.Count; index++)
            {
                int parentIndex = assembly.pieces[index].parentPieceIndex;
                if (parentIndex == removedIndex)
                {
                    assembly.pieces[index].parentPieceIndex = inheritedParentIndex;
                }
                else if (parentIndex > removedIndex)
                {
                    assembly.pieces[index].parentPieceIndex = parentIndex - 1;
                }
            }
            selectedPieceIndex = -1;
            FinishEdit();
            PieceSelected?.Invoke(-1);
        }

        private void OnGenerateGrid(MeshGenerationContext context)
        {
            if (layout.width < 1f || layout.height < 1f)
            {
                return;
            }
            CutoutCanvasGrid.Paint(context, new Vector2(layout.width, layout.height), zoomPointsPerWorldUnit, WorldToElement, ElementToWorld);
        }

        private void OnGenerateArt(MeshGenerationContext context)
        {
            if (assembly == null)
            {
                return;
            }
            List<int> order = GetPiecesInDrawOrder();
            for (int orderIndex = 0; orderIndex < order.Count; orderIndex++)
            {
                CutoutAssemblyPiece piece = assembly.pieces[order[orderIndex]];
                if (IsDrawable(piece))
                {
                    PaintPiece(context, piece);
                }
            }
        }

        private void PaintPiece(MeshGenerationContext context, CutoutAssemblyPiece piece)
        {
            RectInt rect = piece.cutout.ResolveSourceRectPixels();
            if (rect.width <= 0 || rect.height <= 0)
            {
                return;
            }

            List<Vector2> polygon = BuildPolygon(piece, rect);
            triangleScratch.Clear();
            if (!PolygonTriangulator.TryEarClip(polygon, triangleScratch) || triangleScratch.Count < 3)
            {
                polygon = BuildRectPolygon(rect);
                triangleScratch.Clear();
                triangleScratch.AddRange(new int[] { 0, 1, 2, 0, 2, 3 });
            }

            // A Texture2DArray cannot be drawn by the UI mesh API; those pieces show as a flat tint.
            Texture2D drawTexture = piece.cutout.ResolveSourceTexture() as Texture2D;
            Color32 tint = drawTexture != null ? new Color32(255, 255, 255, 255) : new Color32(150, 160, 175, 200);
            MeshWriteData mesh = context.Allocate(polygon.Count, triangleScratch.Count, drawTexture);
            Vector2[] elementPoints = new Vector2[polygon.Count];
            for (int index = 0; index < polygon.Count; index++)
            {
                elementPoints[index] = WorldToElement(PixelToWorld(piece, polygon[index]));
                Vertex vertex = new Vertex();
                vertex.position = new Vector3(elementPoints[index].x, elementPoints[index].y, Vertex.nearZ);
                vertex.tint = tint;
                if (drawTexture != null)
                {
                    vertex.uv = new Vector2((rect.x + polygon[index].x) / drawTexture.width, (rect.y + polygon[index].y) / drawTexture.height);
                }
                mesh.SetNextVertex(vertex);
            }
            for (int index = 0; index + 2 < triangleScratch.Count; index += 3)
            {
                WriteTriangleClockwise(mesh, elementPoints, triangleScratch[index], triangleScratch[index + 1], triangleScratch[index + 2]);
            }
        }

        private static void WriteTriangleClockwise(MeshWriteData mesh, Vector2[] elementPoints, int first, int second, int third)
        {
            Vector2 edgeA = elementPoints[second] - elementPoints[first];
            Vector2 edgeB = elementPoints[third] - elementPoints[first];
            bool isClockwise = edgeA.x * edgeB.y - edgeA.y * edgeB.x >= 0f;
            mesh.SetNextIndex((ushort)first);
            mesh.SetNextIndex((ushort)(isClockwise ? second : third));
            mesh.SetNextIndex((ushort)(isClockwise ? third : second));
        }

        private static List<Vector2> BuildPolygon(CutoutAssemblyPiece piece, RectInt rect)
        {
            List<Vector2> outline = piece.cutout.outlinePixels;
            if (outline != null && outline.Count >= 3)
            {
                return new List<Vector2>(outline);
            }
            return BuildRectPolygon(rect);
        }

        private static List<Vector2> BuildRectPolygon(RectInt rect)
        {
            return new List<Vector2>
            {
                Vector2.zero,
                new Vector2(rect.width, 0f),
                new Vector2(rect.width, rect.height),
                new Vector2(0f, rect.height)
            };
        }

        private void OnGenerateOverlay(MeshGenerationContext context)
        {
            if (assembly == null)
            {
                return;
            }

            Painter2D painter = context.painter2D;
            PaintBoneLines(painter);
            if (IsValidIndex(selectedPieceIndex) && IsDrawable(assembly.pieces[selectedPieceIndex]))
            {
                PaintSelectedPieceOutline(painter, assembly.pieces[selectedPieceIndex]);
                painter.strokeColor = ToolkitPalette.Accent;
                painter.lineWidth = 1.5f;
                painter.BeginPath();
                painter.Arc(WorldToElement(assembly.pieces[selectedPieceIndex].positionWorld), RotationRingRadiusPoints, Angle.Degrees(0f), Angle.Degrees(360f), ArcDirection.Clockwise);
                painter.Stroke();
            }

            for (int index = 0; index < assembly.pieces.Count; index++)
            {
                if (IsDrawable(assembly.pieces[index]))
                {
                    PaintOriginDot(painter, assembly.pieces[index].positionWorld, index == selectedPieceIndex);
                }
            }

            if (dragKind == DragKind.Parent && dragHasMoved && IsValidIndex(dragPieceIndex))
            {
                painter.strokeColor = ToolkitPalette.Selected;
                painter.lineWidth = 1.5f;
                painter.BeginPath();
                painter.MoveTo(WorldToElement(assembly.pieces[dragPieceIndex].positionWorld));
                painter.LineTo(dragCurrentElement);
                painter.Stroke();
            }
        }

        private void PaintBoneLines(Painter2D painter)
        {
            painter.strokeColor = ToolkitPalette.MarkerPart;
            painter.lineWidth = 1.5f;
            for (int index = 0; index < assembly.pieces.Count; index++)
            {
                CutoutAssemblyPiece piece = assembly.pieces[index];
                int parentIndex = piece.parentPieceIndex;
                if (!IsDrawable(piece) || !IsValidIndex(parentIndex) || !IsDrawable(assembly.pieces[parentIndex]))
                {
                    continue;
                }
                painter.BeginPath();
                painter.MoveTo(WorldToElement(assembly.pieces[parentIndex].positionWorld));
                painter.LineTo(WorldToElement(piece.positionWorld));
                painter.Stroke();
            }
        }

        private void PaintSelectedPieceOutline(Painter2D painter, CutoutAssemblyPiece piece)
        {
            List<Vector2> polygon = BuildPolygon(piece, piece.cutout.ResolveSourceRectPixels());
            painter.strokeColor = ToolkitPalette.Accent;
            painter.lineWidth = 2f;
            painter.BeginPath();
            for (int index = 0; index < polygon.Count; index++)
            {
                Vector2 point = WorldToElement(PixelToWorld(piece, polygon[index]));
                if (index == 0)
                {
                    painter.MoveTo(point);
                }
                else
                {
                    painter.LineTo(point);
                }
            }
            painter.ClosePath();
            painter.Stroke();
        }

        private void PaintOriginDot(Painter2D painter, Vector2 positionWorld, bool isSelected)
        {
            painter.BeginPath();
            painter.Arc(WorldToElement(positionWorld), OriginDotRadiusPoints, Angle.Degrees(0f), Angle.Degrees(360f), ArcDirection.Clockwise);
            painter.fillColor = isSelected ? ToolkitPalette.Selected : ToolkitPalette.MarkerRoot;
            painter.Fill(FillRule.NonZero);
            painter.strokeColor = Color.white;
            painter.lineWidth = 1.5f;
            painter.Stroke();
        }
    }
}
