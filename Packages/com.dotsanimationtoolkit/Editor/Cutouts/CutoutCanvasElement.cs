// Copyright (c) 2026 Spencer Park. All rights reserved.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    public sealed class CutoutCanvasElement : VisualElement
    {
        public const float MinorGridWorldUnits = 0.1f;
        public const float MajorGridWorldUnits = 1f;

        private const float MinimumPointsPerWorldUnit = 4f;
        private const float MaximumPointsPerWorldUnit = 20000f;
        private const float MinimumGridLineSpacingPoints = 6f;
        private const float MinimumAxisLabelSpacingPoints = 48f;
        private const int AxisLabelPoolLimit = 64;
        private const float RailClearancePoints = 48f;
        private const float HandleSizePoints = 7f;
        private const float HoveredHandleSizePoints = 10f;
        private const float OriginCircleRadiusPoints = 7f;
        private const float FrameAllMarginFactor = 1.2f;
        private const float DefaultPointsPerWorldUnit = 200f;

        public event Action ShapeChanged;
        public event Action ViewChanged;

        private readonly VisualElement gridLayer;
        private readonly Image referenceImageElement;
        private readonly Image ghostImageElement;
        private readonly Image frameImageElement;
        private readonly VisualElement overlayLayer;
        private readonly List<Label> axisLabelPool = new List<Label>();

        private List<Vector2> outlinePixels;
        private Vector2 originPixels;
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
        }

        public void SetCutout(List<Vector2> outlinePixels, Vector2 originPixels, float pixelsPerUnit, Vector2Int frameSize)
        {
            this.outlinePixels = outlinePixels;
            this.originPixels = originPixels;
            this.pixelsPerUnit = Mathf.Max(0.0001f, pixelsPerUnit);
            this.frameSize = frameSize;
            hasCutout = true;
            selectedVertexIndex = -1;
            hoveredVertexIndex = -1;
            hoveredEdgeIndex = -1;
            Refresh();
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
            Vector2 worldDelta = (originPixels - newOriginPixels) / pixelsPerUnit;
            viewCentreWorld += worldDelta;
            originPixels = newOriginPixels;
            Refresh();
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
            return (pixel - originPixels) / pixelsPerUnit;
        }

        public Vector2 WorldToPixel(Vector2 world)
        {
            return world * pixelsPerUnit + originPixels;
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
                Vector2 bottomLeftElement = PixelToElement(Vector2.zero);
                Vector2 topRightElement = PixelToElement(new Vector2(frameSize.x, frameSize.y));
                frameTopLeft = new Vector2(bottomLeftElement.x, topRightElement.y);
                frameElementSize = new Vector2(topRightElement.x - bottomLeftElement.x, bottomLeftElement.y - topRightElement.y);
            }

            PlaceImage(ghostImageElement, frameTopLeft, frameElementSize, hasFrame && hasGhostTexture && isAllFramesGhostVisible);
            PlaceImage(frameImageElement, frameTopLeft, frameElementSize, hasFrame && hasFrameTexture);

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
            if (keyDownEvent.keyCode != KeyCode.F)
            {
                return;
            }

            FrameAll();
            keyDownEvent.StopPropagation();
        }

        private static Color WhiteWithAlpha(float alpha)
        {
            return new Color(1f, 1f, 1f, alpha);
        }

        private static void StrokeSegments(Painter2D painter, List<Vector2> segmentEndpoints, Color color, float width)
        {
            if (segmentEndpoints.Count == 0)
            {
                return;
            }

            painter.strokeColor = color;
            painter.lineWidth = width;
            painter.BeginPath();
            for (int index = 0; index + 1 < segmentEndpoints.Count; index += 2)
            {
                painter.MoveTo(segmentEndpoints[index]);
                painter.LineTo(segmentEndpoints[index + 1]);
            }

            painter.Stroke();
        }

        private void AddVerticalLine(List<Vector2> segmentEndpoints, float worldX)
        {
            float elementX = WorldToElement(new Vector2(worldX, 0f)).x;
            segmentEndpoints.Add(new Vector2(elementX, 0f));
            segmentEndpoints.Add(new Vector2(elementX, layout.height));
        }

        private void AddHorizontalLine(List<Vector2> segmentEndpoints, float worldY)
        {
            float elementY = WorldToElement(new Vector2(0f, worldY)).y;
            segmentEndpoints.Add(new Vector2(0f, elementY));
            segmentEndpoints.Add(new Vector2(layout.width, elementY));
        }

        private void OnGenerateGrid(MeshGenerationContext context)
        {
            if (!HasUsableLayout())
            {
                return;
            }

            Painter2D painter = context.painter2D;
            Vector2 worldMin = ElementToWorld(new Vector2(0f, layout.height));
            Vector2 worldMax = ElementToWorld(new Vector2(layout.width, 0f));

            List<Vector2> minorSegments = new List<Vector2>();
            List<Vector2> majorSegments = new List<Vector2>();
            bool drawMinor = MinorGridWorldUnits * zoomPointsPerWorldUnit >= MinimumGridLineSpacingPoints;

            if (drawMinor)
            {
                int firstMinorX = Mathf.FloorToInt(worldMin.x / MinorGridWorldUnits);
                int lastMinorX = Mathf.CeilToInt(worldMax.x / MinorGridWorldUnits);
                for (int index = firstMinorX; index <= lastMinorX; index++)
                {
                    if (index % 10 != 0)
                    {
                        AddVerticalLine(minorSegments, index * MinorGridWorldUnits);
                    }
                }

                int firstMinorY = Mathf.FloorToInt(worldMin.y / MinorGridWorldUnits);
                int lastMinorY = Mathf.CeilToInt(worldMax.y / MinorGridWorldUnits);
                for (int index = firstMinorY; index <= lastMinorY; index++)
                {
                    if (index % 10 != 0)
                    {
                        AddHorizontalLine(minorSegments, index * MinorGridWorldUnits);
                    }
                }
            }

            int firstMajorX = Mathf.FloorToInt(worldMin.x / MajorGridWorldUnits);
            int lastMajorX = Mathf.CeilToInt(worldMax.x / MajorGridWorldUnits);
            for (int index = firstMajorX; index <= lastMajorX; index++)
            {
                if (index != 0)
                {
                    AddVerticalLine(majorSegments, index * MajorGridWorldUnits);
                }
            }

            int firstMajorY = Mathf.FloorToInt(worldMin.y / MajorGridWorldUnits);
            int lastMajorY = Mathf.CeilToInt(worldMax.y / MajorGridWorldUnits);
            for (int index = firstMajorY; index <= lastMajorY; index++)
            {
                if (index != 0)
                {
                    AddHorizontalLine(majorSegments, index * MajorGridWorldUnits);
                }
            }

            List<Vector2> axisSegments = new List<Vector2>();
            AddVerticalLine(axisSegments, 0f);
            AddHorizontalLine(axisSegments, 0f);

            StrokeSegments(painter, minorSegments, WhiteWithAlpha(0.04f), 1f);
            StrokeSegments(painter, majorSegments, WhiteWithAlpha(0.09f), 1f);
            StrokeSegments(painter, axisSegments, WhiteWithAlpha(0.22f), 1f);
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
                Vector2 bottomLeftElement = PixelToElement(Vector2.zero);
                Vector2 topRightElement = PixelToElement(new Vector2(frameSize.x, frameSize.y));
                AppendRectPath(painter, new Vector2(bottomLeftElement.x, topRightElement.y), new Vector2(topRightElement.x, bottomLeftElement.y));
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
            if (hasCutout && isShapeVisible && vertexCount >= 2)
            {
                DrawOutline(painter, vertexCount);
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

            for (int index = 0; index < vertexCount; index++)
            {
                bool isHovered = index == hoveredVertexIndex;
                bool isSelected = index == selectedVertexIndex;
                StrokeHandleSquare(painter, elementPoints[index], isHovered ? HoveredHandleSizePoints : HandleSizePoints, isSelected);
            }
        }

        private void DrawOriginMarker(Painter2D painter)
        {
            Vector2 centre = WorldToElement(Vector2.zero);
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
