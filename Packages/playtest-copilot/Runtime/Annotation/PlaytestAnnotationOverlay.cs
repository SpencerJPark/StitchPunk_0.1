using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PlaytestCopilot
{
    /// Pause-and-draw: freezes Time.timeScale, grabs the frame already on screen, and lets the
    /// developer circle/pen/arrow/erase over it before handing the composited PNGs to the bus.
    public sealed class PlaytestAnnotationOverlay : MonoBehaviour
    {
        private const float ToolbarPaddingPixels = 8f;
        private const float ToolbarButtonWidthPixels = 64f;
        private const float ToolbarButtonHeightPixels = 24f;
        private const float ToolbarNoteFieldWidthPixels = 220f;
        private const int DefaultStrokeThicknessPixels = 4;

        private static Texture2D solidWhiteTexture;

        private bool isOpen;
        private string openMarkerId = string.Empty;
        private string typedNote = string.Empty;
        private float savedTimeScale = 1f;
        private Texture2D capturedFrameTexture;
        private readonly List<PlaytestStroke> completedStrokes = new List<PlaytestStroke>();
        private PlaytestStroke activeStroke;

        public bool IsOpen => isOpen;
        public PlaytestAnnotationTool ActiveTool { get; set; } = PlaytestAnnotationTool.Circle;
        public string TypedNote => typedNote;

        public void Open(string markerId)
        {
            openMarkerId = markerId;
            typedNote = string.Empty;
            completedStrokes.Clear();
            activeStroke = null;
            ActiveTool = PlaytestAnnotationTool.Circle;

            savedTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            isOpen = true;

            DestroyCapturedFrame();
            StartCoroutine(CaptureFrameAtEndOfFrame());
        }

        public void CloseAndCapture()
        {
            if (!isOpen)
            {
                return;
            }

            int frameWidth = capturedFrameTexture != null ? capturedFrameTexture.width : Screen.width;
            int frameHeight = capturedFrameTexture != null ? capturedFrameTexture.height : Screen.height;

            Texture2D annotationTexture = PlaytestAnnotationStrokes.RenderToTexture(completedStrokes, frameWidth, frameHeight);
            Texture2D combinedTexture = PlaytestAnnotationStrokes.Combine(capturedFrameTexture, annotationTexture);

            PlaytestAnnotationCapture capture = new PlaytestAnnotationCapture
            {
                MarkerId = openMarkerId,
                Time = PlaytestSessionClock.Now,
                FramePng = capturedFrameTexture != null ? ImageConversion.EncodeToPNG(capturedFrameTexture) : new byte[0],
                AnnotationPng = ImageConversion.EncodeToPNG(annotationTexture),
                CombinedPng = ImageConversion.EncodeToPNG(combinedTexture),
                CircledRegions = PlaytestAnnotationStrokes.ToRegions(completedStrokes)
            };

            PlaytestCaptureBus.RaiseAnnotationCaptured(capture);

            Destroy(annotationTexture);
            Destroy(combinedTexture);
            FinishAndRestoreTimeScale();
        }

        public void Cancel()
        {
            if (!isOpen)
            {
                return;
            }

            FinishAndRestoreTimeScale();
        }

        private void FinishAndRestoreTimeScale()
        {
            DestroyCapturedFrame();
            completedStrokes.Clear();
            activeStroke = null;
            Time.timeScale = savedTimeScale;
            isOpen = false;
        }

        private void DestroyCapturedFrame()
        {
            if (capturedFrameTexture != null)
            {
                Destroy(capturedFrameTexture);
                capturedFrameTexture = null;
            }
        }

        // ScreenCapture.CaptureScreenshotAsTexture only returns a complete frame when called after
        // the frame has finished rendering; anywhere else it comes back black or torn.
        private IEnumerator CaptureFrameAtEndOfFrame()
        {
            yield return new WaitForEndOfFrame();
            capturedFrameTexture = ScreenCapture.CaptureScreenshotAsTexture();
        }

        private void OnGUI()
        {
            if (!isOpen)
            {
                return;
            }

            HandleKeyboardShortcuts();
            if (!isOpen)
            {
                return;
            }

            Rect toolbarRect = ComputeToolbarRect();
            HandleToolInput(toolbarRect);
            DrawStrokesScreenSpace();
            DrawToolbar(toolbarRect);
        }

        private void HandleKeyboardShortcuts()
        {
            Event currentEvent = Event.current;
            if (currentEvent == null || currentEvent.type != EventType.KeyDown)
            {
                return;
            }

            if (currentEvent.keyCode == KeyCode.Return || currentEvent.keyCode == KeyCode.KeypadEnter)
            {
                currentEvent.Use();
                CloseAndCapture();
            }
            else if (currentEvent.keyCode == KeyCode.Escape)
            {
                currentEvent.Use();
                Cancel();
            }
        }

        private void HandleToolInput(Rect toolbarRect)
        {
            Event currentEvent = Event.current;
            if (currentEvent == null || toolbarRect.Contains(currentEvent.mousePosition))
            {
                return;
            }

            // IMGUI mouse coordinates are top-left origin; PlaytestStroke.ScreenPoints must be
            // bottom-left, matching Camera.ScreenPointToRay, or every circled region lands wrong.
            Vector2 bottomLeftOriginPoint = new Vector2(currentEvent.mousePosition.x, Screen.height - currentEvent.mousePosition.y);

            if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0)
            {
                activeStroke = new PlaytestStroke
                {
                    Tool = ActiveTool,
                    Color = ColorForTool(ActiveTool),
                    ThicknessPixels = DefaultStrokeThicknessPixels
                };
                activeStroke.ScreenPoints.Add(bottomLeftOriginPoint);
                currentEvent.Use();
            }
            else if (currentEvent.type == EventType.MouseDrag && currentEvent.button == 0 && activeStroke != null)
            {
                activeStroke.ScreenPoints.Add(bottomLeftOriginPoint);
                currentEvent.Use();
            }
            else if (currentEvent.type == EventType.MouseUp && currentEvent.button == 0 && activeStroke != null)
            {
                activeStroke.ScreenPoints.Add(bottomLeftOriginPoint);
                completedStrokes.Add(activeStroke);
                activeStroke = null;
                currentEvent.Use();
            }
        }

        private static Color ColorForTool(PlaytestAnnotationTool tool)
        {
            switch (tool)
            {
                case PlaytestAnnotationTool.Circle:
                    return Color.yellow;
                case PlaytestAnnotationTool.Pen:
                    return Color.red;
                case PlaytestAnnotationTool.Arrow:
                    return Color.cyan;
                case PlaytestAnnotationTool.Eraser:
                    return Color.white;
                default:
                    return Color.magenta;
            }
        }

        private void DrawStrokesScreenSpace()
        {
            for (int strokeIndex = 0; strokeIndex < completedStrokes.Count; strokeIndex++)
            {
                DrawStroke(completedStrokes[strokeIndex]);
            }

            if (activeStroke != null)
            {
                DrawStroke(activeStroke);
            }
        }

        private static void DrawStroke(PlaytestStroke stroke)
        {
            if (stroke == null || stroke.ScreenPoints == null || stroke.ScreenPoints.Count == 0)
            {
                return;
            }

            if (stroke.ScreenPoints.Count == 1)
            {
                Vector2 singlePointGuiSpace = FlipToGuiSpace(stroke.ScreenPoints[0]);
                DrawScreenLine(singlePointGuiSpace, singlePointGuiSpace, stroke.Color, stroke.ThicknessPixels);
                return;
            }

            for (int pointIndex = 1; pointIndex < stroke.ScreenPoints.Count; pointIndex++)
            {
                Vector2 fromGuiSpace = FlipToGuiSpace(stroke.ScreenPoints[pointIndex - 1]);
                Vector2 toGuiSpace = FlipToGuiSpace(stroke.ScreenPoints[pointIndex]);
                DrawScreenLine(fromGuiSpace, toGuiSpace, stroke.Color, stroke.ThicknessPixels);
            }
        }

        private static Vector2 FlipToGuiSpace(Vector2 bottomLeftOriginPoint)
        {
            return new Vector2(bottomLeftOriginPoint.x, Screen.height - bottomLeftOriginPoint.y);
        }

        private static void DrawScreenLine(Vector2 fromPoint, Vector2 toPoint, Color color, int thicknessPixels)
        {
            EnsureSolidWhiteTexture();

            Matrix4x4 savedMatrix = GUI.matrix;
            Color savedColor = GUI.color;

            float lineLength = Mathf.Max(Vector2.Distance(fromPoint, toPoint), 1f);
            float angleDegrees = Mathf.Atan2(toPoint.y - fromPoint.y, toPoint.x - fromPoint.x) * Mathf.Rad2Deg;

            GUI.color = color;
            GUIUtility.RotateAroundPivot(angleDegrees, fromPoint);
            GUI.DrawTexture(new Rect(fromPoint.x, fromPoint.y - thicknessPixels * 0.5f, lineLength, thicknessPixels), solidWhiteTexture);

            GUI.matrix = savedMatrix;
            GUI.color = savedColor;
        }

        private static void EnsureSolidWhiteTexture()
        {
            if (solidWhiteTexture != null)
            {
                return;
            }

            solidWhiteTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            solidWhiteTexture.SetPixel(0, 0, Color.white);
            solidWhiteTexture.Apply();
        }

        private static Rect ComputeToolbarRect()
        {
            float width = ToolbarPaddingPixels
                + (4 * (ToolbarButtonWidthPixels + ToolbarPaddingPixels))
                + ToolbarNoteFieldWidthPixels + ToolbarPaddingPixels
                + (2 * (ToolbarButtonWidthPixels + ToolbarPaddingPixels));
            float height = ToolbarButtonHeightPixels + (2f * ToolbarPaddingPixels);
            return new Rect(ToolbarPaddingPixels, ToolbarPaddingPixels, width, height);
        }

        private void DrawToolbar(Rect toolbarRect)
        {
            GUI.Box(toolbarRect, GUIContent.none);

            float cursorX = toolbarRect.x + ToolbarPaddingPixels;
            float cursorY = toolbarRect.y + ToolbarPaddingPixels;

            DrawToolButton(ref cursorX, cursorY, "Circle", PlaytestAnnotationTool.Circle);
            DrawToolButton(ref cursorX, cursorY, "Pen", PlaytestAnnotationTool.Pen);
            DrawToolButton(ref cursorX, cursorY, "Arrow", PlaytestAnnotationTool.Arrow);
            DrawToolButton(ref cursorX, cursorY, "Eraser", PlaytestAnnotationTool.Eraser);

            Rect noteFieldRect = new Rect(cursorX, cursorY, ToolbarNoteFieldWidthPixels, ToolbarButtonHeightPixels);
            typedNote = GUI.TextField(noteFieldRect, typedNote);
            cursorX += ToolbarNoteFieldWidthPixels + ToolbarPaddingPixels;

            Rect doneButtonRect = new Rect(cursorX, cursorY, ToolbarButtonWidthPixels, ToolbarButtonHeightPixels);
            if (GUI.Button(doneButtonRect, "Done"))
            {
                CloseAndCapture();
                return;
            }

            cursorX += ToolbarButtonWidthPixels + ToolbarPaddingPixels;

            Rect cancelButtonRect = new Rect(cursorX, cursorY, ToolbarButtonWidthPixels, ToolbarButtonHeightPixels);
            if (GUI.Button(cancelButtonRect, "Cancel"))
            {
                Cancel();
            }
        }

        private void DrawToolButton(ref float cursorX, float cursorY, string label, PlaytestAnnotationTool tool)
        {
            Rect buttonRect = new Rect(cursorX, cursorY, ToolbarButtonWidthPixels, ToolbarButtonHeightPixels);
            Color previousBackgroundColor = GUI.backgroundColor;
            GUI.backgroundColor = ActiveTool == tool ? Color.cyan : previousBackgroundColor;

            if (GUI.Button(buttonRect, label))
            {
                ActiveTool = tool;
            }

            GUI.backgroundColor = previousBackgroundColor;
            cursorX += ToolbarButtonWidthPixels + ToolbarPaddingPixels;
        }

        private void OnDestroy()
        {
            DestroyCapturedFrame();
        }
    }
}
