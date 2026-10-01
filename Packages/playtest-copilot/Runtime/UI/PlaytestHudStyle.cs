using UnityEngine;

namespace PlaytestCopilot
{
    /// The on-screen surfaces follow Docs/AnimationToolkit/EditorStyleGuide.md: shadcn-leaning,
    /// Unity's own neutrals, colour only for status or selection.
    ///
    /// The guide's R15 says neutrals come from `var(--unity-colors-*)` and never from literal greys.
    /// That is not reachable here: these overlays are runtime IMGUI drawn into the game view, with no
    /// UI Toolkit panel and therefore no theme variables to resolve. The tokens below are the guide's
    /// Pro-skin values, named after the guide's roles so the two stay in step by hand. This is the one
    /// deliberate departure, and it is confined to this file.
    public static class PlaytestHudStyle
    {
        public static readonly Color Surface = Hex(0x282828, 0.94f);
        public static readonly Color Raised = Hex(0x3E3E3E, 1f);
        public static readonly Color Divider = Hex(0x232323, 1f);
        public static readonly Color FieldBackground = Hex(0x2A2A2A, 1f);
        public static readonly Color ButtonBackground = Hex(0x585858, 1f);
        public static readonly Color ButtonHover = Hex(0x676767, 1f);
        public static readonly Color TextPrimary = Hex(0xD2D2D2, 1f);
        public static readonly Color TextLabel = Hex(0xC4C4C4, 1f);
        public static readonly Color TextMuted = Hex(0x8F8F8F, 1f);
        public static readonly Color Selection = Hex(0x2C5D87, 1f);

        /// Status hues. Recording is a status, not decoration, which is why it is a tone dot and not
        /// the full-width red banner this replaced.
        public static readonly Color StatusRecording = Hex(0xE5484D, 1f);
        public static readonly Color StatusIdle = Hex(0x8F8F8F, 1f);
        public static readonly Color MeterFill = Hex(0x6FBF73, 1f);

        // The guide's scale: 2 / 4 / 8 / 12 / 16 / 24, 12px column inset, and its control heights.
        public const float SpaceTiny = 4f;
        public const float SpaceSmall = 8f;
        public const float Inset = 12f;
        public const float ControlHeight = 24f;
        public const float PrimaryHeight = 28f;
        public const float MeterHeight = 6f;

        public const int TypeMeta = 11;
        public const int TypeBody = 12;
        public const int TypePaneTitle = 13;

        private static Texture2D solidTexture;
        private static GUIStyle metaLabelStyle;
        private static GUIStyle bodyLabelStyle;
        private static GUIStyle paneTitleStyle;
        private static GUIStyle primaryButtonStyle;
        private static GUIStyle secondaryButtonStyle;
        private static GUIStyle fieldStyle;

        public static Texture2D SolidTexture
        {
            get
            {
                if (solidTexture == null)
                {
                    solidTexture = Texture2D.whiteTexture;
                }

                return solidTexture;
            }
        }

        public static void DrawSolid(Rect rect, Color color)
        {
            Color previousColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, SolidTexture);
            GUI.color = previousColor;
        }

        /// A flush surface with a 1px border, which is the guide's card: no radius here because IMGUI
        /// has no rounded rect without shipping a sprite, and a hairline border reads closer than a
        /// fake one would.
        public static void DrawSurface(Rect rect)
        {
            DrawSolid(rect, Surface);
            DrawBorder(rect, Divider);
        }

        public static void DrawBorder(Rect rect, Color color)
        {
            DrawSolid(new Rect(rect.x, rect.y, rect.width, 1f), color);
            DrawSolid(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), color);
            DrawSolid(new Rect(rect.x, rect.y, 1f, rect.height), color);
            DrawSolid(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), color);
        }

        /// Status row dot, per the guide's "tone dot plus one line" footer.
        public static void DrawStatusDot(Rect rect, Color tone)
        {
            DrawSolid(rect, tone);
        }

        public static GUIStyle MetaLabel(float scale)
        {
            metaLabelStyle = EnsureLabel(metaLabelStyle, TextMuted);
            metaLabelStyle.fontSize = Mathf.RoundToInt(TypeMeta * scale);
            return metaLabelStyle;
        }

        public static GUIStyle BodyLabel(float scale)
        {
            bodyLabelStyle = EnsureLabel(bodyLabelStyle, TextLabel);
            bodyLabelStyle.fontSize = Mathf.RoundToInt(TypeBody * scale);
            return bodyLabelStyle;
        }

        public static GUIStyle PaneTitle(float scale)
        {
            paneTitleStyle = EnsureLabel(paneTitleStyle, TextPrimary);
            paneTitleStyle.fontStyle = FontStyle.Bold;
            paneTitleStyle.fontSize = Mathf.RoundToInt(TypePaneTitle * scale);
            return paneTitleStyle;
        }

        /// The guide's primary: a light neutral fill with inverted text, one per surface.
        public static GUIStyle PrimaryButton(float scale)
        {
            if (primaryButtonStyle == null)
            {
                // Built from the label skin, never the button skin. GUI.Label draws its style's
                // background, so a button-derived style paints Unity's default chrome straight over
                // the flat fill underneath it — which is why the first restyle changed nothing on
                // screen even though every token was correct.
                primaryButtonStyle = NewBackgroundlessStyle();
                primaryButtonStyle.alignment = TextAnchor.MiddleCenter;
                primaryButtonStyle.fontStyle = FontStyle.Bold;
                SetTextColorForAllStates(primaryButtonStyle, Hex(0x1A1A1A, 1f));
            }

            primaryButtonStyle.fontSize = Mathf.RoundToInt(TypeBody * scale);
            return primaryButtonStyle;
        }

        public static GUIStyle SecondaryButton(float scale)
        {
            if (secondaryButtonStyle == null)
            {
                secondaryButtonStyle = NewBackgroundlessStyle();
                secondaryButtonStyle.alignment = TextAnchor.MiddleCenter;
                SetTextColorForAllStates(secondaryButtonStyle, TextLabel);
            }

            secondaryButtonStyle.fontSize = Mathf.RoundToInt(TypeBody * scale);
            return secondaryButtonStyle;
        }

        public static GUIStyle Field(float scale)
        {
            if (fieldStyle == null)
            {
                fieldStyle = new GUIStyle(GUI.skin.textField);
                ClearStyleBackgrounds(fieldStyle);
                SetTextColorForAllStates(fieldStyle, TextPrimary);
                fieldStyle.alignment = TextAnchor.MiddleLeft;
                fieldStyle.padding = new RectOffset(6, 6, 0, 0);
            }

            fieldStyle.fontSize = Mathf.RoundToInt(TypeBody * scale);
            return fieldStyle;
        }

        /// Draws a button as a flat filled rect plus a label, so the game's GUI skin cannot impose its
        /// own chrome on top of the guide's. Returns true on click.
        public static bool DrawFlatButton(Rect rect, string label, GUIStyle labelStyle, Color fill, Color border)
        {
            bool isHovered = rect.Contains(Event.current.mousePosition);
            DrawSolid(rect, isHovered ? Lighten(fill, 0.06f) : fill);
            if (border.a > 0f)
            {
                DrawBorder(rect, border);
            }

            GUI.Label(rect, label, labelStyle);
            return GUI.Button(rect, GUIContent.none, GUIStyle.none);
        }

        public static Color Lighten(Color color, float amount)
        {
            return new Color(
                Mathf.Clamp01(color.r + amount),
                Mathf.Clamp01(color.g + amount),
                Mathf.Clamp01(color.b + amount),
                color.a);
        }

        private static GUIStyle EnsureLabel(GUIStyle existingStyle, Color textColor)
        {
            if (existingStyle != null)
            {
                return existingStyle;
            }

            GUIStyle style = NewBackgroundlessStyle();
            style.alignment = TextAnchor.MiddleLeft;
            SetTextColorForAllStates(style, textColor);
            return style;
        }

        /// A label style carrying no chrome of its own, so the only pixels drawn are the glyphs over
        /// whatever this file painted underneath.
        private static GUIStyle NewBackgroundlessStyle()
        {
            GUIStyle style = new GUIStyle(GUI.skin.label);
            ClearStyleBackgrounds(style);
            style.wordWrap = false;
            style.padding = new RectOffset(0, 0, 0, 0);
            style.margin = new RectOffset(0, 0, 0, 0);
            style.border = new RectOffset(0, 0, 0, 0);
            return style;
        }

        private static void ClearStyleBackgrounds(GUIStyle style)
        {
            style.normal.background = null;
            style.hover.background = null;
            style.active.background = null;
            style.focused.background = null;
            style.onNormal.background = null;
            style.onHover.background = null;
            style.onActive.background = null;
            style.onFocused.background = null;
        }

        private static void SetTextColorForAllStates(GUIStyle style, Color textColor)
        {
            style.normal.textColor = textColor;
            style.hover.textColor = textColor;
            style.active.textColor = textColor;
            style.focused.textColor = textColor;
            style.onNormal.textColor = textColor;
            style.onHover.textColor = textColor;
            style.onActive.textColor = textColor;
            style.onFocused.textColor = textColor;
        }

        private static Color Hex(int rgb, float alpha)
        {
            return new Color(
                ((rgb >> 16) & 0xFF) / 255f,
                ((rgb >> 8) & 0xFF) / 255f,
                (rgb & 0xFF) / 255f,
                alpha);
        }
    }
}
