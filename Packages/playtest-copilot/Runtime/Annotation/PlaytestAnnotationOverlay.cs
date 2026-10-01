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

        // Matches the capture HUD so both on-screen surfaces are sized by one rule. A fixed-pixel
        // toolbar draws at a third of its intended size on a high-DPI game view.
        private const float ReferenceScreenHeight = 900f;
        private const float MaximumScale = 3f;

        private static Texture2D solidWhiteTexture;

        private bool isOpen;
        private string openMarkerId = string.Empty;
        private string typedNote = string.Empty;
        private float savedTimeScale = 1f;
        private Texture2D capturedFrameTexture;
        private readonly List<PlaytestStroke> completedStrokes = new List<PlaytestStroke>();
        private PlaytestStroke activeStroke;
        private Texture2D previewTexture;
        private bool previewTextureDirty;

        public bool IsOpen => isOpen;
        public PlaytestAnnotationTool ActiveTool { get; set; } = PlaytestAnnotationTool.Circle;
        public string TypedNote => typedNote;

        public void Open(string markerId)
        {
            openMarkerId = markerId;
            typedNote = string.Empty;
            completedStrokes.Clear();
            activeStroke = null;
            previewTextureDirty = true;
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
            previewTextureDirty = true;
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

            // The stroke preview is a full-screen texture rebuilt on every committed stroke; leaving
            // it behind leaks one screen's worth of RGBA per annotation for the rest of the session.
            if (previewTexture != null)
            {
                Destroy(previewTexture);
                previewTexture = null;
            }

            previewTextureDirty = true;
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

            Rect toolbarRect = ComputeToolbarRect(ToolbarScale);
            HandleToolInput(toolbarRect);
            DrawStrokesScreenSpace();
            DrawToolbar(toolbarRect, ToolbarScale);
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
                previewTextureDirty = true;
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
                default:
                    return Color.magenta;
            }
        }

        /// Committed strokes are previewed through the very texture the saved PNG is rendered from,
        /// so the eraser actually erases on screen. Drawing them as GUI lines instead is what made the
        /// eraser look like a white pen: it painted over the ink rather than removing it, and nothing
        /// on screen matched the file that came out.
        ///
        /// Only the in-progress stroke is drawn as cheap GUI lines, and the eraser's is a hollow ring
        /// cursor rather than a filled line, because an eraser has no colour of its own.
        private void DrawStrokesScreenSpace()
        {
            RebuildPreviewTextureIfDirty();

            if (previewTexture != null)
            {
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), previewTexture);
            }

            if (activeStroke == null)
            {
                return;
            }

            DrawStroke(activeStroke);
        }

        private void RebuildPreviewTextureIfDirty()
        {
            if (!previewTextureDirty)
            {
                return;
            }

            previewTextureDirty = false;
            if (previewTexture != null)
            {
                Destroy(previewTexture);
                previewTexture = null;
            }

            if (completedStrokes.Count == 0)
            {
                return;
            }

            previewTexture = PlaytestAnnotationStrokes.RenderToTexture(
                completedStrokes, Screen.width, Screen.height);
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

        private static float ToolbarScale
        {
            get { return Mathf.Clamp(Screen.height / ReferenceScreenHeight, 1f, MaximumScale); }
        }

        private static Rect ComputeToolbarRect(float scale)
        {
            float padding = ToolbarPaddingPixels * scale;
            float buttonWidth = ToolbarButtonWidthPixels * scale;
            float width = padding
                + (3 * (buttonWidth + padding))
                + ToolbarNoteFieldWidthPixels * scale + padding
                + (4 * (buttonWidth + padding));
            float height = ToolbarButtonHeightPixels * scale + (2f * padding);
            return new Rect(padding, padding, width, height);
        }

        /// Same surface language as the capture card: one flush card, Unity's neutrals, the active
        /// tool filled with the selection blue, and Done as the single primary action.
        private void DrawToolbar(Rect toolbarRect, float scale)
        {
            PlaytestHudStyle.DrawSurface(toolbarRect, scale);

            float padding = ToolbarPaddingPixels * scale;
            float buttonWidth = ToolbarButtonWidthPixels * scale;
            float buttonHeight = ToolbarButtonHeightPixels * scale;
            float cursorX = toolbarRect.x + padding;
            float cursorY = toolbarRect.y + padding;

            DrawToolButton(ref cursorX, cursorY, "Circle", PlaytestAnnotationTool.Circle, scale);
            DrawToolButton(ref cursorX, cursorY, "Pen", PlaytestAnnotationTool.Pen, scale);
            DrawToolButton(ref cursorX, cursorY, "Arrow", PlaytestAnnotationTool.Arrow, scale);

            Rect noteFieldRect = new Rect(cursorX, cursorY, ToolbarNoteFieldWidthPixels * scale, buttonHeight);
            int fieldRadius = PlaytestHudStyle.ScaledRadius(PlaytestHudStyle.RadiusField, scale);
            PlaytestHudStyle.DrawRounded(noteFieldRect, PlaytestHudStyle.FieldBackground, fieldRadius);
            PlaytestHudStyle.DrawRoundedBorder(noteFieldRect, PlaytestHudStyle.Divider, fieldRadius);
            typedNote = GUI.TextField(noteFieldRect, typedNote, PlaytestHudStyle.Field(scale));
            if (string.IsNullOrEmpty(typedNote))
            {
                // An empty unlabelled box reads as a gap in the toolbar rather than as a field.
                GUI.Label(noteFieldRect, "  Type a note (optional)", PlaytestHudStyle.MetaLabel(scale));
            }
            cursorX += ToolbarNoteFieldWidthPixels * scale + padding;

            bool hasStrokes = completedStrokes.Count > 0;
            Rect undoButtonRect = new Rect(cursorX, cursorY, buttonWidth, buttonHeight);
            if (DrawActionButton(undoButtonRect, "Undo", scale, hasStrokes))
            {
                UndoLastStroke();
            }

            cursorX += buttonWidth + padding;

            Rect clearButtonRect = new Rect(cursorX, cursorY, buttonWidth, buttonHeight);
            if (DrawActionButton(clearButtonRect, "Clear", scale, hasStrokes))
            {
                ClearAllStrokes();
            }

            cursorX += buttonWidth + padding;

            Rect doneButtonRect = new Rect(cursorX, cursorY, buttonWidth, buttonHeight);
            PlaytestHudStyle.DrawRounded(doneButtonRect, PlaytestHudStyle.TextLabel,
                PlaytestHudStyle.ScaledRadius(PlaytestHudStyle.RadiusButton, scale));
            GUI.Label(doneButtonRect, "Done", PlaytestHudStyle.PrimaryButton(scale));
            if (GUI.Button(doneButtonRect, GUIContent.none, GUIStyle.none))
            {
                CloseAndCapture();
                return;
            }

            cursorX += buttonWidth + padding;

            Rect cancelButtonRect = new Rect(cursorX, cursorY, buttonWidth, buttonHeight);
            if (PlaytestHudStyle.DrawFlatButton(cancelButtonRect, "Cancel",
                PlaytestHudStyle.SecondaryButton(scale), PlaytestHudStyle.ButtonBackground,
                PlaytestHudStyle.Divider, scale))
            {
                Cancel();
            }
        }

        /// Undo and Clear are actions, not tools, so they never take the selected-blue fill. Disabled
        /// at 40% with no hover when there is nothing to undo, per the style guide's state rule.
        private bool DrawActionButton(Rect buttonRect, string label, float scale, bool isEnabled)
        {
            if (!isEnabled)
            {
                Color disabledFill = PlaytestHudStyle.ButtonBackground;
                disabledFill.a = 0.4f;
                PlaytestHudStyle.DrawRounded(buttonRect, disabledFill,
                    PlaytestHudStyle.ScaledRadius(PlaytestHudStyle.RadiusButton, scale));
                GUIStyle disabledLabelStyle = PlaytestHudStyle.SecondaryButton(scale);
                Color previousColor = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, 0.4f);
                GUI.Label(buttonRect, label, disabledLabelStyle);
                GUI.color = previousColor;
                return false;
            }

            return PlaytestHudStyle.DrawFlatButton(buttonRect, label,
                PlaytestHudStyle.SecondaryButton(scale), PlaytestHudStyle.ButtonBackground,
                PlaytestHudStyle.Divider, scale);
        }

        public void UndoLastStroke()
        {
            if (completedStrokes.Count == 0)
            {
                return;
            }

            completedStrokes.RemoveAt(completedStrokes.Count - 1);
            previewTextureDirty = true;
        }

        public void ClearAllStrokes()
        {
            if (completedStrokes.Count == 0)
            {
                return;
            }

            completedStrokes.Clear();
            previewTextureDirty = true;
        }

        private void DrawToolButton(ref float cursorX, float cursorY, string label,
            PlaytestAnnotationTool tool, float scale)
        {
            Rect buttonRect = new Rect(cursorX, cursorY,
                ToolbarButtonWidthPixels * scale, ToolbarButtonHeightPixels * scale);

            // Blue means selected, per the style guide; a toggle that is on is filled, not tinted.
            bool isActiveTool = ActiveTool == tool;
            Color fill = isActiveTool ? PlaytestHudStyle.Selection : PlaytestHudStyle.ButtonBackground;
            if (PlaytestHudStyle.DrawFlatButton(buttonRect, label,
                PlaytestHudStyle.SecondaryButton(scale), fill, PlaytestHudStyle.Divider, scale))
            {
                ActiveTool = tool;
            }

            cursorX += ToolbarButtonWidthPixels * scale + ToolbarPaddingPixels * scale;
        }

        private void OnDestroy()
        {
            DestroyCapturedFrame();
        }
    }
}
