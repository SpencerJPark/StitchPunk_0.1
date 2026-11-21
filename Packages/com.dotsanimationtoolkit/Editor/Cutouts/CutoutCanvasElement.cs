// Copyright (c) 2026 Spencer Park. All rights reserved.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    public sealed class CutoutCanvasElement : VisualElement
    {
        public const float MinorGridWorldUnits = CutoutCanvasGrid.MinorGridWorldUnits;
        public const float MajorGridWorldUnits = CutoutCanvasGrid.MajorGridWorldUnits;

        private const float MinimumPointsPerWorldUnit = 4f;
        private const float MaximumPointsPerWorldUnit = 20000f;
        private const float MinimumAxisLabelSpacingPoints = 48f;
        private const int AxisLabelPoolLimit = 64;
        private const float RailClearancePoints = 48f;
        private const float HandleSizePoints = 7f;
        private const float HoveredHandleSizePoints = 10f;
        private const float OriginCircleRadiusPoints = 7f;
        public const float RotationRingRadiusPoints = 16f;
        public const float RotationRingPickBandPoints = 4f;
        private const float FrameAllMarginFactor = 1.2f;
        private const float DefaultPointsPerWorldUnit = 200f;

        public event Action ShapeChanged;
        public event Action ViewChanged;
        public event Action ModeChanged;
        public event Action<int> FrameStepKeyPressed;

        private readonly VisualElement gridLayer;
        private readonly Image referenceImageElement;
        private readonly Image ghostImageElement;
        private readonly Image frameImageElement;
        private readonly VisualElement overlayLayer;
        private readonly List<Label> axisLabelPool = new List<Label>();

        private List<Vector2> outlinePixels;
        private Vector2 originPixels;
        private Vector2 artPositionWorld;
        private float rotationDegrees;
        private bool isRotationRingHovered;
        private List<Vector2Int> innerEdges = new List<Vector2Int>();
        private readonly List<int> cachedTriangles = new List<int>();
        private readonly List<int> cachedSkippedEdgeIndices = new List<int>();
        private CutoutCanvasMode mode = CutoutCanvasMode.Object;
        private CutoutCanvasMode lastEditMode = CutoutCanvasMode.EditVertices;
        private int selectedInnerEdgeIndex = -1;
        private int hoveredInnerEdgeIndex = -1;
        private int rubberBandFromVertexIndex = -1;
        private Vector2 rubberBandToElementPoint;
        private float pixelsPerUnit = 100f;
        private Vector2Int frameSize;
        private bool hasCutout;
        private Rect referenceRectWorld = new Rect(-0.5f, 0f, 1f, 1f);
        private bool hasReference;
        private Vector2 viewCentreWorld;
        private float zoomPointsPerWorldUnit = DefaultPointsPerWorldUnit;
        private bool needsFrameAllOnLayout = true;
        private bool isPanning;
        private int panPointerId = -1;
        private int selectedVertexIndex = -1;
        private int hoveredVertexIndex = -1;
        private int hoveredEdgeIndex = -1;
        private bool isShapeVisible = true;
        private bool isOriginVisible = true;
        private bool isReferenceVisible = true;
        private bool isAllFramesGhostVisible = true;
        private bool isOutlineInvalid;
        private bool hasFrameTexture;
        private bool hasGhostTexture;

        public List<Vector2> OutlinePixels => outlinePixels;
        public Vector2 OriginPixels => originPixels;
        public float PixelsPerUnit => pixelsPerUnit;
        public Vector2Int FrameSize => frameSize;
        public bool HasCutout => hasCutout;
        public bool HasReference => hasReference;
        public float ElementPointsPerWorldUnit => zoomPointsPerWorldUnit;

        public Vector2 ArtPositionWorld => artPositionWorld;
        public List<Vector2Int> InnerEdges => innerEdges;
        public IReadOnlyList<int> SkippedInnerEdgeIndices => cachedSkippedEdgeIndices;

        public float RotationDegrees => rotationDegrees;

        public bool IsRotationRingHovered
        {
            get => isRotationRingHovered;
            set
            {
                if (isRotationRingHovered == value)
                {
                    return;
                }

                isRotationRingHovered = value;
                overlayLayer.MarkDirtyRepaint();
            }
        }

        public Rect ArtRectWorld
        {
            get
            {
                Vector2 firstWorld = PixelToWorld(Vector2.zero);
                Vector2 secondWorld = PixelToWorld(new Vector2(frameSize.x, 0f));
                Vector2 thirdWorld = PixelToWorld(new Vector2(frameSize.x, frameSize.y));
                Vector2 fourthWorld = PixelToWorld(new Vector2(0f, frameSize.y));
                float minX = Mathf.Min(Mathf.Min(firstWorld.x, secondWorld.x), Mathf.Min(thirdWorld.x, fourthWorld.x));
                float maxX = Mathf.Max(Mathf.Max(firstWorld.x, secondWorld.x), Mathf.Max(thirdWorld.x, fourthWorld.x));
                float minY = Mathf.Min(Mathf.Min(firstWorld.y, secondWorld.y), Mathf.Min(thirdWorld.y, fourthWorld.y));
                float maxY = Mathf.Max(Mathf.Max(firstWorld.y, secondWorld.y), Mathf.Max(thirdWorld.y, fourthWorld.y));
                return Rect.MinMaxRect(minX, minY, maxX, maxY);
            }
        }

        public bool ArtContainsWorldPoint(Vector2 world)
        {
            Vector2 pixel = WorldToPixel(world);
            return pixel.x >= 0f && pixel.y >= 0f && pixel.x <= frameSize.x && pixel.y <= frameSize.y;
        }

        public CutoutCanvasMode Mode
        {
            get => mode;
            set
            {
                if (value != CutoutCanvasMode.Object)
                {
                    lastEditMode = value;
                }

                bool modeChanged = value != mode;
                mode = value;
                selectedVertexIndex = -1;
                selectedInnerEdgeIndex = -1;
                hoveredInnerEdgeIndex = -1;
                rubberBandFromVertexIndex = -1;
                Refresh();
                if (modeChanged)
                {
                    ModeChanged?.Invoke();
                }
            }
        }

        public int SelectedInnerEdgeIndex
        {
            get => selectedInnerEdgeIndex;
            set
            {
                selectedInnerEdgeIndex = value;
                overlayLayer.MarkDirtyRepaint();
            }
        }

        public int HoveredInnerEdgeIndex
        {
            get => hoveredInnerEdgeIndex;
            set
            {
                hoveredInnerEdgeIndex = value;
                overlayLayer.MarkDirtyRepaint();
            }
        }

        public Rect ReferenceRectWorld
        {
            get => referenceRectWorld;
            set
            {
                referenceRectWorld = value;
                Refresh();
            }
        }

        public int SelectedVertexIndex
        {
            get => selectedVertexIndex;
            set
            {
                selectedVertexIndex = value;
                overlayLayer.MarkDirtyRepaint();
            }
        }

        public int HoveredVertexIndex
        {
            get => hoveredVertexIndex;
            set
            {
                hoveredVertexIndex = value;
                overlayLayer.MarkDirtyRepaint();
            }
        }

        public int HoveredEdgeIndex
        {
            get => hoveredEdgeIndex;
            set
            {
                hoveredEdgeIndex = value;
                overlayLayer.MarkDirtyRepaint();
            }
        }

        public bool IsShapeVisible
        {
            get => isShapeVisible;
            set
            {
                isShapeVisible = value;
                overlayLayer.MarkDirtyRepaint();
            }
        }

        public bool IsOriginVisible
        {
            get => isOriginVisible;
            set
            {
                isOriginVisible = value;
                overlayLayer.MarkDirtyRepaint();
            }
        }

        public bool IsReferenceVisible
        {
            get => isReferenceVisible;
            set
            {
                isReferenceVisible = value;
                Refresh();
            }
        }

        public bool IsAllFramesGhostVisible
        {
            get => isAllFramesGhostVisible;
            set
            {
                isAllFramesGhostVisible = value;
                Refresh();
            }
        }

        public bool IsOutlineInvalid
        {
            get => isOutlineInvalid;
            set
            {
                isOutlineInvalid = value;
                overlayLayer.MarkDirtyRepaint();
            }
        }

        public CutoutCanvasElement()
        {
            AddToClassList("cutout-canvas");
            focusable = true;
            pickingMode = PickingMode.Position;
            style.flexGrow = 1f;
            style.overflow = Overflow.Hidden;

            gridLayer = MakeFullSizeLayer();
            gridLayer.generateVisualContent += OnGenerateGrid;
            Add(gridLayer);

            referenceImageElement = MakeImage();
            Add(referenceImageElement);
            ghostImageElement = MakeImage();
            Add(ghostImageElement);
            frameImageElement = MakeImage();
            Add(frameImageElement);

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
            RegisterCallback<NavigationMoveEvent>(OnNavigationMove);
        }

        public void SetCutout(List<Vector2> outlinePixels, List<Vector2Int> innerEdges, Vector2 originPixels, Vector2 artPositionWorld, float pixelsPerUnit, Vector2Int frameSize, float rotationDegrees = 0f)
        {
            this.rotationDegrees = rotationDegrees;
            this.outlinePixels = outlinePixels;
            this.innerEdges = innerEdges ?? new List<Vector2Int>();
            this.originPixels = originPixels;
            this.artPositionWorld = artPositionWorld;
            this.pixelsPerUnit = Mathf.Max(0.0001f, pixelsPerUnit);
            this.frameSize = frameSize;
            hasCutout = true;
            selectedVertexIndex = -1;
            hoveredVertexIndex = -1;
            hoveredEdgeIndex = -1;
            selectedInnerEdgeIndex = -1;
            hoveredInnerEdgeIndex = -1;
            rubberBandFromVertexIndex = -1;
            Refresh();
        }

        public void SetArtPositionWorld(Vector2 newArtPositionWorld)
        {
            artPositionWorld = newArtPositionWorld;
            Refresh();
        }

        public void SetPixelsPerUnitHoldingPixel(float newPixelsPerUnit, Vector2 heldPixel)
        {
            Vector2 heldWorld = PixelToWorld(heldPixel);
            pixelsPerUnit = Mathf.Max(1f, newPixelsPerUnit);
            artPositionWorld = heldWorld - RotateDegrees((heldPixel - originPixels) / pixelsPerUnit, rotationDegrees);
            Refresh();
        }

        public void SetEdgeRubberBand(int fromVertexIndex, Vector2 toElementPoint)
        {
            rubberBandFromVertexIndex = fromVertexIndex;
            rubberBandToElementPoint = toElementPoint;
            overlayLayer.MarkDirtyRepaint();
        }

        public void ClearEdgeRubberBand()
        {
            rubberBandFromVertexIndex = -1;
            overlayLayer.MarkDirtyRepaint();
        }

        public void ClearCutout()
        {
            outlinePixels = null;
            hasCutout = false;
            frameSize = Vector2Int.zero;
            selectedVertexIndex = -1;
            hoveredVertexIndex = -1;
            hoveredEdgeIndex = -1;
            Refresh();
        }

        public void SetOriginPixels(Vector2 newOriginPixels)
        {
            artPositionWorld += RotateDegrees((newOriginPixels - originPixels) / pixelsPerUnit, rotationDegrees);
            originPixels = newOriginPixels;
            Refresh();
        }

        public void SetRotationDegrees(float newRotationDegrees)
        {
            rotationDegrees = newRotationDegrees;
            Refresh();
        }

        public static Vector2 RotateDegrees(Vector2 vector, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float cosine = Mathf.Cos(radians);
            float sine = Mathf.Sin(radians);
            return new Vector2(vector.x * cosine - vector.y * sine, vector.x * sine + vector.y * cosine);
        }

        // Element-space corners (top-left, top-right, bottom-right, bottom-left of the art frame) after rotation.
        public void GetFrameCornersElement(Vector2[] cornersOut)
        {
            cornersOut[0] = PixelToElement(new Vector2(0f, frameSize.y));
            cornersOut[1] = PixelToElement(new Vector2(frameSize.x, frameSize.y));
            cornersOut[2] = PixelToElement(new Vector2(frameSize.x, 0f));
            cornersOut[3] = PixelToElement(Vector2.zero);
        }

        private static void AppendQuadPath(Painter2D painter, Vector2[] corners)
        {
            painter.BeginPath();
            painter.MoveTo(corners[0]);
            painter.LineTo(corners[1]);
            painter.LineTo(corners[2]);
            painter.LineTo(corners[3]);
            painter.ClosePath();
        }

        public void SetFrameTexture(Texture frameTexture)
        {
            frameImageElement.image = frameTexture;
            hasFrameTexture = frameTexture != null;
            Refresh();
        }

        public void SetAllFramesGhost(Texture2D ghostTexture)
        {
            ghostImageElement.image = ghostTexture;
            ghostImageElement.tintColor = new Color(1f, 1f, 1f, 0.5f);
            hasGhostTexture = ghostTexture != null;
            Refresh();
        }

        public void SetReference(Texture2D referenceImage, Rect referenceRectWorld, float opacity)
        {
            referenceImageElement.image = referenceImage;
            referenceImageElement.tintColor = new Color(1f, 1f, 1f, Mathf.Clamp01(opacity));
            hasReference = referenceImage != null;
            this.referenceRectWorld = referenceRectWorld;
            Refresh();
        }

        public void FrameAll()
        {
            if (!HasUsableLayout())
            {
                needsFrameAllOnLayout = true;
                return;
            }

            needsFrameAllOnLayout = false;
            Rect boundsWorld;
            if (hasCutout && frameSize.x > 0 && frameSize.y > 0)
            {
                Vector2 frameMinWorld = PixelToWorld(Vector2.zero);
                Vector2 frameMaxWorld = PixelToWorld(new Vector2(frameSize.x, frameSize.y));
                boundsWorld = Rect.MinMaxRect(frameMinWorld.x, frameMinWorld.y, frameMaxWorld.x, frameMaxWorld.y);
                if (hasReference && isReferenceVisible)
                {
                    boundsWorld = Rect.MinMaxRect(
                        Mathf.Min(boundsWorld.xMin, referenceRectWorld.xMin),
                        Mathf.Min(boundsWorld.yMin, referenceRectWorld.yMin),
                        Mathf.Max(boundsWorld.xMax, referenceRectWorld.xMax),
                        Mathf.Max(boundsWorld.yMax, referenceRectWorld.yMax));
                }
            }
            else
            {
                boundsWorld = Rect.MinMaxRect(-1f, -1f, 1f, 1f);
            }

            float fitWidth = Mathf.Max(boundsWorld.width, 0.0001f) * FrameAllMarginFactor;
            float fitHeight = Mathf.Max(boundsWorld.height, 0.0001f) * FrameAllMarginFactor;
            float fittedZoom = Mathf.Min(layout.width / fitWidth, layout.height / fitHeight);
            zoomPointsPerWorldUnit = Mathf.Clamp(fittedZoom, MinimumPointsPerWorldUnit, MaximumPointsPerWorldUnit);
            viewCentreWorld = boundsWorld.center;
            Refresh();
            ViewChanged?.Invoke();
        }

        public void Refresh()
        {
            cachedTriangles.Clear();
            cachedSkippedEdgeIndices.Clear();
            if (hasCutout && mode != CutoutCanvasMode.Object && outlinePixels != null && outlinePixels.Count >= 3)
            {
                PolygonTriangulator.TryTriangulateWithEdges(outlinePixels, innerEdges, cachedTriangles, cachedSkippedEdgeIndices);
            }

            LayoutImages();
            gridLayer.MarkDirtyRepaint();
            overlayLayer.MarkDirtyRepaint();
            UpdateAxisLabels();
        }

        public void RaiseShapeChanged()
        {
            ShapeChanged?.Invoke();
        }

        public Vector2 PixelToWorld(Vector2 pixel)
        {
            return artPositionWorld + RotateDegrees((pixel - originPixels) / pixelsPerUnit, rotationDegrees);
        }

        public Vector2 WorldToPixel(Vector2 world)
        {
            return RotateDegrees(world - artPositionWorld, -rotationDegrees) * pixelsPerUnit + originPixels;
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

        public Vector2 PixelToElement(Vector2 pixel)
        {
            return WorldToElement(PixelToWorld(pixel));
        }

        public Vector2 ElementToPixel(Vector2 elementPoint)
        {
            return WorldToPixel(ElementToWorld(elementPoint));
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

        private static Image MakeImage()
        {
            Image image = new Image();
            image.pickingMode = PickingMode.Ignore;
            image.scaleMode = ScaleMode.StretchToFill;
            image.style.position = Position.Absolute;
            image.style.display = DisplayStyle.None;
            return image;
        }

        private bool HasUsableLayout()
        {
            return !float.IsNaN(layout.width) && !float.IsNaN(layout.height) && layout.width > 1f && layout.height > 1f;
        }

        private void OnGeometryChanged(GeometryChangedEvent geometryChangedEvent)
        {
            if (needsFrameAllOnLayout && HasUsableLayout())
            {
                FrameAll();
                return;
            }

            Refresh();
        }

        private void RotateImageAboutOrigin(Image image, Vector2 imageTopLeftElement)
        {
            Vector2 originElement = WorldToElement(artPositionWorld);
            image.style.transformOrigin = new TransformOrigin(originElement.x - imageTopLeftElement.x, originElement.y - imageTopLeftElement.y, 0f);
            image.style.rotate = new Rotate(Angle.Degrees(-rotationDegrees));
        }

        private void PlaceImage(Image image, Vector2 topLeftElement, Vector2 sizeElement, bool shouldShow)
        {
            image.style.display = shouldShow ? DisplayStyle.Flex : DisplayStyle.None;
            image.style.left = topLeftElement.x;
            image.style.top = topLeftElement.y;
            image.style.width = sizeElement.x;
            image.style.height = sizeElement.y;
        }

        private void LayoutImages()
        {
            bool hasFrame = hasCutout && frameSize.x > 0 && frameSize.y > 0;
            Vector2 frameTopLeft = Vector2.zero;
            Vector2 frameElementSize = Vector2.zero;
            if (hasFrame)
            {
                Vector2 bottomLeftElement = WorldToElement(artPositionWorld + (Vector2.zero - originPixels) / pixelsPerUnit);
                Vector2 topRightElement = WorldToElement(artPositionWorld + (new Vector2(frameSize.x, frameSize.y) - originPixels) / pixelsPerUnit);
                frameTopLeft = new Vector2(bottomLeftElement.x, topRightElement.y);
                frameElementSize = new Vector2(topRightElement.x - bottomLeftElement.x, bottomLeftElement.y - topRightElement.y);
            }

            PlaceImage(ghostImageElement, frameTopLeft, frameElementSize, hasFrame && hasGhostTexture && isAllFramesGhostVisible);
            PlaceImage(frameImageElement, frameTopLeft, frameElementSize, hasFrame && hasFrameTexture);
            if (hasFrame)
            {
                RotateImageAboutOrigin(ghostImageElement, frameTopLeft);
                RotateImageAboutOrigin(frameImageElement, frameTopLeft);
            }

            Vector2 referenceTopLeft = WorldToElement(new Vector2(referenceRectWorld.xMin, referenceRectWorld.yMax));
            Vector2 referenceBottomRight = WorldToElement(new Vector2(referenceRectWorld.xMax, referenceRectWorld.yMin));
            PlaceImage(referenceImageElement, referenceTopLeft, referenceBottomRight - referenceTopLeft, hasReference && isReferenceVisible);
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

            Refresh();
            ViewChanged?.Invoke();
            wheelEvent.StopPropagation();
        }

        private void OnPointerDown(PointerDownEvent pointerDownEvent)
        {
            Focus();
            bool isMiddleButton = pointerDownEvent.button == 2;
            bool isAltLeftButton = pointerDownEvent.button == 0 && pointerDownEvent.altKey;
            if (!isMiddleButton && !isAltLeftButton)
            {
                return;
            }

            isPanning = true;
            panPointerId = pointerDownEvent.pointerId;
            this.CapturePointer(panPointerId);
            pointerDownEvent.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent pointerMoveEvent)
        {
            if (!isPanning || pointerMoveEvent.pointerId != panPointerId)
            {
                return;
            }

            Vector2 deltaElement = new Vector2(pointerMoveEvent.deltaPosition.x, pointerMoveEvent.deltaPosition.y);
            viewCentreWorld += new Vector2(-deltaElement.x, deltaElement.y) / zoomPointsPerWorldUnit;
            Refresh();
            ViewChanged?.Invoke();
            pointerMoveEvent.StopPropagation();
        }

        private void OnPointerUp(PointerUpEvent pointerUpEvent)
        {
            if (!isPanning || pointerUpEvent.pointerId != panPointerId)
            {
                return;
            }

            EndPan();
            pointerUpEvent.StopPropagation();
        }

        private void OnPointerCaptureOut(PointerCaptureOutEvent captureOutEvent)
        {
            isPanning = false;
            panPointerId = -1;
        }

        private void EndPan()
        {
            int releasedPointerId = panPointerId;
            isPanning = false;
            panPointerId = -1;
            if (this.HasPointerCapture(releasedPointerId))
            {
                this.ReleasePointer(releasedPointerId);
            }
        }

        private void OnKeyDown(KeyDownEvent keyDownEvent)
        {
            switch (keyDownEvent.keyCode)
            {
                case KeyCode.F:
                    FrameAll();
                    break;
                case KeyCode.LeftArrow:
                    FrameStepKeyPressed?.Invoke(-1);
                    break;
                case KeyCode.RightArrow:
                    FrameStepKeyPressed?.Invoke(1);
                    break;
                case KeyCode.Tab:
                    Mode = mode == CutoutCanvasMode.Object ? lastEditMode : CutoutCanvasMode.Object;
                    break;
                case KeyCode.Alpha1:
                case KeyCode.Keypad1:
                    if (mode == CutoutCanvasMode.Object)
                    {
                        return;
                    }

                    Mode = CutoutCanvasMode.EditVertices;
                    break;
                case KeyCode.Alpha2:
                case KeyCode.Keypad2:
                    if (mode == CutoutCanvasMode.Object)
                    {
                        return;
                    }

                    Mode = CutoutCanvasMode.EditEdges;
                    break;
                default:
                    return;
            }

            keyDownEvent.StopPropagation();
        }

        private void OnNavigationMove(NavigationMoveEvent navigationMoveEvent)
        {
            panel?.focusController?.IgnoreEvent(navigationMoveEvent);
            navigationMoveEvent.StopPropagation();
        }

        private static Color WhiteWithAlpha(float alpha)
        {
            return new Color(1f, 1f, 1f, alpha);
        }

        private void OnGenerateGrid(MeshGenerationContext context)
        {
            if (!HasUsableLayout())
            {
                return;
            }

            CutoutCanvasGrid.Paint(context, new Vector2(layout.width, layout.height), zoomPointsPerWorldUnit, WorldToElement, ElementToWorld);
        }

        private void AppendRectPath(Painter2D painter, Vector2 topLeft, Vector2 bottomRight)
        {
            painter.BeginPath();
            painter.MoveTo(topLeft);
            painter.LineTo(new Vector2(bottomRight.x, topLeft.y));
            painter.LineTo(bottomRight);
            painter.LineTo(new Vector2(topLeft.x, bottomRight.y));
            painter.ClosePath();
        }

        private void StrokeHandleSquare(Painter2D painter, Vector2 centre, float size, bool isFilled)
        {
            float half = size * 0.5f;
            AppendRectPath(painter, centre - new Vector2(half, half), centre + new Vector2(half, half));
            if (isFilled)
            {
                painter.fillColor = ToolkitPalette.Selected;
                painter.Fill(FillRule.NonZero);
            }

            painter.strokeColor = Color.white;
            painter.lineWidth = 1.5f;
            painter.Stroke();
        }

        private void OnGenerateOverlay(MeshGenerationContext context)
        {
            if (!HasUsableLayout())
            {
                return;
            }

            Painter2D painter = context.painter2D;
            bool hasFrame = hasCutout && frameSize.x > 0 && frameSize.y > 0;

            if (hasFrame)
            {
                Vector2[] frameCorners = new Vector2[4];
                GetFrameCornersElement(frameCorners);
                AppendQuadPath(painter, frameCorners);
                painter.strokeColor = WhiteWithAlpha(0.2f);
                painter.lineWidth = 1f;
                painter.Stroke();
            }

            if (hasReference && isReferenceVisible)
            {
                Vector2 referenceTopLeft = WorldToElement(new Vector2(referenceRectWorld.xMin, referenceRectWorld.yMax));
                Vector2 referenceBottomRight = WorldToElement(new Vector2(referenceRectWorld.xMax, referenceRectWorld.yMin));
                AppendRectPath(painter, referenceTopLeft, referenceBottomRight);
                painter.strokeColor = WhiteWithAlpha(0.35f);
                painter.lineWidth = 1f;
                painter.Stroke();
                StrokeHandleSquare(painter, new Vector2(referenceBottomRight.x, referenceTopLeft.y), HandleSizePoints, false);
            }

            int vertexCount = outlinePixels != null ? outlinePixels.Count : 0;
            if (hasFrame && mode == CutoutCanvasMode.Object)
            {
                DrawObjectModeFrame(painter);
            }

            if (hasCutout && isShapeVisible && vertexCount >= 2)
            {
                if (mode == CutoutCanvasMode.Object)
                {
                    DrawOutlineThin(painter, vertexCount);
                }
                else
                {
                    DrawWireframe(painter);
                    DrawOutline(painter, vertexCount);
                }
            }

            if (hasCutout && isOriginVisible)
            {
                DrawOriginMarker(painter);
            }
        }

        private void DrawOutline(Painter2D painter, int vertexCount)
        {
            List<Vector2> elementPoints = new List<Vector2>(vertexCount);
            for (int index = 0; index < vertexCount; index++)
            {
                elementPoints.Add(PixelToElement(outlinePixels[index]));
            }

            painter.BeginPath();
            painter.MoveTo(elementPoints[0]);
            for (int index = 1; index < vertexCount; index++)
            {
                painter.LineTo(elementPoints[index]);
            }

            painter.ClosePath();
            if (vertexCount >= 3)
            {
                painter.fillColor = WhiteWithAlpha(0.06f);
                painter.Fill(FillRule.NonZero);
            }

            painter.strokeColor = isOutlineInvalid ? ToolkitPalette.Error : WhiteWithAlpha(0.85f);
            painter.lineWidth = 1.5f;
            painter.Stroke();

            if (hoveredEdgeIndex >= 0 && hoveredEdgeIndex < vertexCount)
            {
                painter.BeginPath();
                painter.MoveTo(elementPoints[hoveredEdgeIndex]);
                painter.LineTo(elementPoints[(hoveredEdgeIndex + 1) % vertexCount]);
                painter.strokeColor = ToolkitPalette.Accent;
                painter.lineWidth = 2f;
                painter.Stroke();
            }

            DrawInnerEdges(painter, elementPoints);

            for (int index = 0; index < vertexCount; index++)
            {
                bool isHovered = index == hoveredVertexIndex;
                bool isSelected = index == selectedVertexIndex;
                if (mode == CutoutCanvasMode.EditEdges)
                {
                    DrawEdgeModeVertexDot(painter, elementPoints[index], isHovered);
                }
                else
                {
                    StrokeHandleSquare(painter, elementPoints[index], isHovered ? HoveredHandleSizePoints : HandleSizePoints, isSelected);
                }
            }

            if (mode == CutoutCanvasMode.EditEdges && rubberBandFromVertexIndex >= 0 && rubberBandFromVertexIndex < vertexCount)
            {
                painter.BeginPath();
                painter.MoveTo(elementPoints[rubberBandFromVertexIndex]);
                painter.LineTo(rubberBandToElementPoint);
                painter.strokeColor = ToolkitPalette.Accent;
                painter.lineWidth = 1.5f;
                painter.Stroke();
            }
        }

        private void DrawEdgeModeVertexDot(Painter2D painter, Vector2 centre, bool isHovered)
        {
            painter.BeginPath();
            painter.Arc(centre, 2f, Angle.Degrees(0f), Angle.Degrees(360f), ArcDirection.Clockwise);
            painter.fillColor = Color.white;
            painter.Fill(FillRule.NonZero);

            if (isHovered)
            {
                painter.BeginPath();
                painter.Arc(centre, 6f, Angle.Degrees(0f), Angle.Degrees(360f), ArcDirection.Clockwise);
                painter.strokeColor = ToolkitPalette.Accent;
                painter.lineWidth = 1.5f;
                painter.Stroke();
            }
        }

        private void StrokeInnerEdge(Painter2D painter, List<Vector2> elementPoints, int edgeIndex, Color color, float width)
        {
            Vector2Int edge = innerEdges[edgeIndex];
            if (edge.x < 0 || edge.y < 0 || edge.x >= elementPoints.Count || edge.y >= elementPoints.Count)
            {
                return;
            }

            painter.BeginPath();
            painter.MoveTo(elementPoints[edge.x]);
            painter.LineTo(elementPoints[edge.y]);
            painter.strokeColor = color;
            painter.lineWidth = width;
            painter.Stroke();
        }

        private void DrawInnerEdges(Painter2D painter, List<Vector2> elementPoints)
        {
            for (int edgeIndex = 0; edgeIndex < innerEdges.Count; edgeIndex++)
            {
                bool isSkipped = cachedSkippedEdgeIndices.Contains(edgeIndex);
                if (isSkipped)
                {
                    StrokeInnerEdge(painter, elementPoints, edgeIndex, ToolkitPalette.Warning, 1f);
                }
                else
                {
                    StrokeInnerEdge(painter, elementPoints, edgeIndex, ToolkitPalette.Accent, 1.5f);
                }
            }

            if (hoveredInnerEdgeIndex >= 0 && hoveredInnerEdgeIndex < innerEdges.Count)
            {
                StrokeInnerEdge(painter, elementPoints, hoveredInnerEdgeIndex, ToolkitPalette.Accent, 2.5f);
            }

            if (selectedInnerEdgeIndex >= 0 && selectedInnerEdgeIndex < innerEdges.Count)
            {
                StrokeInnerEdge(painter, elementPoints, selectedInnerEdgeIndex, ToolkitPalette.Selected, 2.5f);
            }
        }

        private void DrawWireframe(Painter2D painter)
        {
            int triangleIndexCount = cachedTriangles.Count - cachedTriangles.Count % 3;
            if (triangleIndexCount == 0)
            {
                return;
            }

            painter.BeginPath();
            for (int index = 0; index < triangleIndexCount; index += 3)
            {
                Vector2 first = PixelToElement(outlinePixels[cachedTriangles[index]]);
                Vector2 second = PixelToElement(outlinePixels[cachedTriangles[index + 1]]);
                Vector2 third = PixelToElement(outlinePixels[cachedTriangles[index + 2]]);
                painter.MoveTo(first);
                painter.LineTo(second);
                painter.LineTo(third);
                painter.LineTo(first);
            }

            painter.strokeColor = WhiteWithAlpha(0.25f);
            painter.lineWidth = 1f;
            painter.Stroke();
        }

        private void DrawOutlineThin(Painter2D painter, int vertexCount)
        {
            painter.BeginPath();
            painter.MoveTo(PixelToElement(outlinePixels[0]));
            for (int index = 1; index < vertexCount; index++)
            {
                painter.LineTo(PixelToElement(outlinePixels[index]));
            }

            painter.ClosePath();
            painter.strokeColor = isOutlineInvalid ? ToolkitPalette.Error : WhiteWithAlpha(0.6f);
            painter.lineWidth = 1f;
            painter.Stroke();
        }

        private void DrawObjectModeFrame(Painter2D painter)
        {
            Vector2[] frameCorners = new Vector2[4];
            GetFrameCornersElement(frameCorners);
            AppendQuadPath(painter, frameCorners);
            painter.strokeColor = WhiteWithAlpha(0.6f);
            painter.lineWidth = 1f;
            painter.Stroke();

            for (int cornerIndex = 0; cornerIndex < frameCorners.Length; cornerIndex++)
            {
                StrokeHandleSquare(painter, frameCorners[cornerIndex], HandleSizePoints, false);
            }
        }

        private void DrawOriginMarker(Painter2D painter)
        {
            Vector2 centre = WorldToElement(artPositionWorld);
            if (mode == CutoutCanvasMode.Object)
            {
                painter.strokeColor = isRotationRingHovered ? ToolkitPalette.Selected : ToolkitPalette.MarkerRoot;
                painter.lineWidth = isRotationRingHovered ? 2.5f : 1f;
                painter.BeginPath();
                painter.Arc(centre, RotationRingRadiusPoints, Angle.Degrees(0f), Angle.Degrees(360f), ArcDirection.Clockwise);
                painter.Stroke();
            }

            painter.strokeColor = ToolkitPalette.MarkerRoot;
            painter.lineWidth = 1.5f;
            painter.BeginPath();
            painter.Arc(centre, OriginCircleRadiusPoints, Angle.Degrees(0f), Angle.Degrees(360f), ArcDirection.Clockwise);
            painter.Stroke();

            float reach = OriginCircleRadiusPoints + 3f;
            painter.BeginPath();
            painter.MoveTo(centre + new Vector2(-reach, 0f));
            painter.LineTo(centre + new Vector2(reach, 0f));
            painter.MoveTo(centre + new Vector2(0f, -reach));
            painter.LineTo(centre + new Vector2(0f, reach));
            painter.Stroke();
        }

        private Label GetAxisLabel(int poolIndex)
        {
            while (axisLabelPool.Count <= poolIndex)
            {
                Label label = new Label();
                label.AddToClassList("cutout-canvas__axis-label");
                label.pickingMode = PickingMode.Ignore;
                label.style.position = Position.Absolute;
                Add(label);
                axisLabelPool.Add(label);
            }

            return axisLabelPool[poolIndex];
        }

        private void UpdateAxisLabels()
        {
            int usedCount = 0;
            if (HasUsableLayout())
            {
                float labelStepUnits = MajorGridWorldUnits;
                int[] stepMultipliers = { 2, 5, 10 };
                int multiplierIndex = 0;
                int decadeScale = 1;
                while (labelStepUnits * zoomPointsPerWorldUnit < MinimumAxisLabelSpacingPoints)
                {
                    labelStepUnits = MajorGridWorldUnits * stepMultipliers[multiplierIndex] * decadeScale;
                    multiplierIndex++;
                    if (multiplierIndex >= stepMultipliers.Length)
                    {
                        multiplierIndex = 0;
                        decadeScale *= 10;
                    }
                }

                Vector2 worldMin = ElementToWorld(new Vector2(0f, layout.height));
                Vector2 worldMax = ElementToWorld(new Vector2(layout.width, 0f));

                int firstX = Mathf.CeilToInt(worldMin.x / labelStepUnits);
                int lastX = Mathf.FloorToInt(worldMax.x / labelStepUnits);
                for (int index = firstX; index <= lastX && usedCount < AxisLabelPoolLimit; index++)
                {
                    // The origin is where the axes cross; a "0" there would sit on the origin gizmo.
                    if (index == 0)
                    {
                        continue;
                    }

                    float value = index * labelStepUnits;
                    Label label = GetAxisLabel(usedCount);
                    usedCount++;
                    label.text = value.ToString("0.##");
                    label.style.display = DisplayStyle.Flex;
                    label.style.left = WorldToElement(new Vector2(value, 0f)).x + 3f;
                    label.style.top = Mathf.Clamp(WorldToElement(Vector2.zero).y + 2f, 2f, layout.height - 16f);
                }

                int firstY = Mathf.CeilToInt(worldMin.y / labelStepUnits);
                int lastY = Mathf.FloorToInt(worldMax.y / labelStepUnits);
                for (int index = firstY; index <= lastY && usedCount < AxisLabelPoolLimit; index++)
                {
                    if (index == 0)
                    {
                        continue;
                    }

                    float value = index * labelStepUnits;
                    Label label = GetAxisLabel(usedCount);
                    usedCount++;
                    label.text = value.ToString("0.##");
                    label.style.display = DisplayStyle.Flex;
                    // Past the rail's 40pt column so a label never sits under its buttons.
                    label.style.left = Mathf.Clamp(WorldToElement(Vector2.zero).x + 3f, RailClearancePoints, layout.width - 24f);
                    label.style.top = WorldToElement(new Vector2(0f, value)).y - 14f;
                }
            }

            for (int index = usedCount; index < axisLabelPool.Count; index++)
            {
                axisLabelPool[index].style.display = DisplayStyle.None;
            }
        }
    }
}
