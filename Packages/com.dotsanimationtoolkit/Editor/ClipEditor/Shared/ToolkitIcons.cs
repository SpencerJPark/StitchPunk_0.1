// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// The one place every tab resolves a built-in editor icon and builds an icon button, with a
    /// text fallback for the day a name stops resolving.
    /// </summary>
    public static class ToolkitIcons
    {
        public const string Play = "PlayButton";
        public const string Pause = "PauseButton";
        public const string Stop = "StopButton";
        public const string JumpToStart = "Animation.FirstKey";
        public const string StepBack = "Animation.PrevKey";
        public const string StepForward = "Animation.NextKey";
        public const string JumpToEnd = "Animation.LastKey";
        public const string Loop = "preAudioLoopOff";
        public const string Record = "Animation.Record";
        public const string AddEvent = "Animation.AddEvent";
        public const string EyeOpen = "animationvisibilitytoggleon";
        public const string EyeClosed = "animationvisibilitytoggleoff";
        public const string Plus = "Toolbar Plus";
        public const string Trash = "TreeEditor.Trash";
        public const string Frame = "ViewToolZoom";
        public const string ResetCamera = "FrameCapture";
        public const string Link = "Linked";
        public const string ShotCamera = "SceneViewCamera";
        public const string Move = "MoveTool";
        public const string Rotate = "RotateTool";
        public const string Scale = "ScaleTool";

        // Drawn rather than resolved: the editor ships nothing that reads as "translucent overlay of
        // the source", and every near-miss (an eye, an avatar) already means something else in the
        // same rail.
        private static Texture2D ghostGlyphTexture;

        private const int GhostGlyphSize = 32;

        private const string IconButtonClassName = "toolkit-icon-button";
        private const string IconButtonIconClassName = "toolkit-icon-button__icon";
        private const string IconButtonTextClassName = "toolkit-icon-button--text";
        private const string IconButtonWithTextClassName = "toolkit-icon-button--with-text";
        private const string IconButtonLabelClassName = "toolkit-icon-button__label";

        // Not every built-in icon ships a dark-skin variant, and asking IconContent for one that
        // does not exist yields nothing rather than the light original.
        public static Texture2D Resolve(string iconName)
        {
            if (string.IsNullOrEmpty(iconName))
            {
                return null;
            }

            string primaryIconName = EditorGUIUtility.isProSkin ? "d_" + iconName : iconName;
            string fallbackIconName = EditorGUIUtility.isProSkin ? iconName : "d_" + iconName;

            Texture2D primaryTexture = ResolveExact(primaryIconName);
            if (primaryTexture != null)
            {
                return primaryTexture;
            }

            return ResolveExact(fallbackIconName);
        }

        /// <summary>A drawn ghost silhouette for the overlay toggles the editor has no icon for.</summary>
        public static Texture2D GhostGlyph
        {
            get
            {
                if (ghostGlyphTexture == null)
                {
                    ghostGlyphTexture = BuildGhostGlyph();
                }
                return ghostGlyphTexture;
            }
        }

        // Drawn from a distance field rather than a pixel grid so the one texture stays clean at
        // both 1x and the 2x+ the editor asks for on a scaled display.
        private static Texture2D BuildGhostGlyph()
        {
            Texture2D glyph = new Texture2D(GhostGlyphSize, GhostGlyphSize, TextureFormat.RGBA32, false);
            glyph.hideFlags = HideFlags.HideAndDontSave;
            glyph.filterMode = FilterMode.Bilinear;
            glyph.wrapMode = TextureWrapMode.Clamp;

            // Unity's dark-skin icon grey, so a drawn glyph sits at the same weight as a resolved one.
            Color glyphColor = new Color(0.77f, 0.77f, 0.77f, 1f);
            float edgeSoftnessUv = 1.2f / GhostGlyphSize;
            Color[] pixels = new Color[GhostGlyphSize * GhostGlyphSize];

            for (int rowIndex = 0; rowIndex < GhostGlyphSize; rowIndex++)
            {
                for (int columnIndex = 0; columnIndex < GhostGlyphSize; columnIndex++)
                {
                    // Half a texel in, and y counted upwards — the order SetPixels expects.
                    Vector2 samplePoint = new Vector2(
                        (columnIndex + 0.5f) / GhostGlyphSize,
                        (rowIndex + 0.5f) / GhostGlyphSize);

                    float signedDistance = GhostSignedDistance(samplePoint);
                    Color pixel = glyphColor;
                    pixel.a = Mathf.Clamp01(0.5f - signedDistance / (2f * edgeSoftnessUv));
                    pixels[rowIndex * GhostGlyphSize + columnIndex] = pixel;
                }
            }

            glyph.SetPixels(pixels);
            glyph.Apply(false, false);
            return glyph;
        }

        // A domed head over a square trunk with three round feet, the two eyes cut back out of it.
        private static float GhostSignedDistance(Vector2 samplePoint)
        {
            float headDistance = Vector2.Distance(samplePoint, new Vector2(0.5f, 0.60f)) - 0.34f;
            float trunkDistance = BoxSignedDistance(
                samplePoint, new Vector2(0.5f, 0.39f), new Vector2(0.34f, 0.21f));
            float leftFootDistance = Vector2.Distance(samplePoint, new Vector2(0.27f, 0.18f)) - 0.115f;
            float middleFootDistance = Vector2.Distance(samplePoint, new Vector2(0.5f, 0.18f)) - 0.115f;
            float rightFootDistance = Vector2.Distance(samplePoint, new Vector2(0.73f, 0.18f)) - 0.115f;

            float bodyDistance = Mathf.Min(
                Mathf.Min(headDistance, trunkDistance),
                Mathf.Min(leftFootDistance, Mathf.Min(middleFootDistance, rightFootDistance)));

            float leftEyeDistance = Vector2.Distance(samplePoint, new Vector2(0.375f, 0.63f)) - 0.085f;
            float rightEyeDistance = Vector2.Distance(samplePoint, new Vector2(0.625f, 0.63f)) - 0.085f;

            return Mathf.Max(bodyDistance, -Mathf.Min(leftEyeDistance, rightEyeDistance));
        }

        private static float BoxSignedDistance(Vector2 samplePoint, Vector2 center, Vector2 halfExtents)
        {
            float offsetX = Mathf.Abs(samplePoint.x - center.x) - halfExtents.x;
            float offsetY = Mathf.Abs(samplePoint.y - center.y) - halfExtents.y;
            float outsideDistance = new Vector2(Mathf.Max(offsetX, 0f), Mathf.Max(offsetY, 0f)).magnitude;
            return outsideDistance + Mathf.Min(Mathf.Max(offsetX, offsetY), 0f);
        }

        public static Button MakeIconButton(Action onClick, string iconName, string tooltip, string fallbackText)
        {
            Button iconButton = new Button(onClick) { tooltip = tooltip };
            iconButton.AddToClassList(IconButtonClassName);

            Image iconImage = new Image { pickingMode = PickingMode.Ignore };
            iconImage.AddToClassList(IconButtonIconClassName);
            iconButton.Add(iconImage);

            ApplyIcon(iconButton, iconImage, iconName, fallbackText);
            return iconButton;
        }

        public static Button MakeIconTextButton(Action onClick, string iconName, string tooltip, string text)
        {
            Button iconTextButton = MakeIconButton(onClick, iconName, tooltip, text);
            SetButtonIconAndText(iconTextButton, iconName, text);
            return iconTextButton;
        }

        // The word is a Label child, not button.text: a Button with children gets no text measure
        // from the layout engine, so its own text would wrap one letter per line.
        public static void SetButtonIconAndText(Button button, string iconName, string text)
        {
            if (button == null)
            {
                return;
            }

            SetButtonIcon(button, iconName, text);
            button.text = string.Empty;
            button.RemoveFromClassList(IconButtonTextClassName);

            Label wordLabel = button.Q<Label>(className: IconButtonLabelClassName);
            if (wordLabel == null)
            {
                wordLabel = new Label { pickingMode = PickingMode.Ignore };
                wordLabel.AddToClassList(IconButtonLabelClassName);
                button.Add(wordLabel);
            }
            wordLabel.text = text;
            button.AddToClassList(IconButtonWithTextClassName);
        }

        public static void SetButtonIcon(Button button, string iconName, string fallbackText)
        {
            if (button == null)
            {
                return;
            }

            Image iconImage = button.Q<Image>(className: IconButtonIconClassName);
            if (iconImage == null)
            {
                iconImage = new Image { pickingMode = PickingMode.Ignore };
                iconImage.AddToClassList(IconButtonIconClassName);
                button.Insert(0, iconImage);
            }

            ApplyIcon(button, iconImage, iconName, fallbackText);
        }

        // Mirrors ClipEditorWindow's private SetOverlayToolIcon(Button, Image, ...): the caller already
        // built the icon Image with its own CSS class, so this only swaps the image or falls back to text —
        // unlike the 3-arg SetButtonIcon above, it does not create or reclass the Image itself, and it
        // resolves the icon name literally (no "d_" auto-prefixing) since callers pass the exact editor
        // icon name they want, including the "d_" prefix when they need it.
        public static void SetButtonIcon(Button button, Image icon, string iconName, string fallbackText)
        {
            if (button == null)
            {
                return;
            }

            Texture iconTexture = ResolveExactIconTexture(iconName);
            if (iconTexture != null && icon != null)
            {
                icon.image = iconTexture;
                return;
            }
            if (icon != null)
            {
                icon.RemoveFromHierarchy();
            }
            button.text = fallbackText;
        }

        public static void SetToggleIcon(Toggle toggle, Image icon, string iconName, string fallbackText)
        {
            SetToggleIcon(toggle, icon, ResolveExactIconTexture(iconName), fallbackText);
        }

        // For a glyph this class drew rather than one the editor resolved by name.
        public static void SetToggleIcon(Toggle toggle, Image icon, Texture iconTexture, string fallbackText)
        {
            if (toggle == null)
            {
                return;
            }

            if (iconTexture != null && icon != null)
            {
                icon.image = iconTexture;
                return;
            }
            if (icon != null)
            {
                icon.RemoveFromHierarchy();
            }
            toggle.text = fallbackText;
        }

        private static Texture ResolveExactIconTexture(string iconName)
        {
            GUIContent iconContent = EditorGUIUtility.IconContent(iconName);
            return iconContent != null ? iconContent.image : null;
        }

        private static void ApplyIcon(Button button, Image iconImage, string iconName, string fallbackText)
        {
            Texture2D iconTexture = Resolve(iconName);
            if (iconTexture != null)
            {
                iconImage.image = iconTexture;
                button.text = string.Empty;
                button.RemoveFromClassList(IconButtonTextClassName);
                return;
            }

            iconImage.RemoveFromHierarchy();
            button.text = fallbackText;
            button.AddToClassList(IconButtonTextClassName);
        }

        private static Texture2D ResolveExact(string iconName)
        {
            GUIContent iconContent = EditorGUIUtility.IconContent(iconName);
            if (iconContent == null || iconContent.image == null)
            {
                return null;
            }

            return iconContent.image as Texture2D;
        }
    }
}
