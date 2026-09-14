using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorktreeToolkit.Editor
{
    public static class WorktreeIcons
    {
        public const string Refresh = "Refresh";
        public const string Create = "Toolbar Plus";
        public const string Menu = "_Menu";
        public const string Branch = "UnityEditor.VersionControl";
        public const string SelectionEye = "animationvisibilitytoggleon";
        public const string ReturnStage = "back";
        public const string Merge = "Import";
        public const string Remove = "TreeEditor.Trash";
        public const string Reveal = "FolderOpened Icon";
        public const string BrokerOn = "PlayButton";
        public const string BrokerOff = "PauseButton";
        public const string Ready = "TestPassed";
        public const string Failed = "TestFailed";
        public const string Unclaimed = "Project";
        public const string Warning = "console.warnicon.sml";
        public const string Error = "console.erroricon.sml";
        public const string SpinnerFramePrefix = "WaitSpin";

        // T0 probe (2026-09-14): InspectorLock resolves directly on both skins, no fallback search needed.
        public const string LockIconName = "InspectorLock";

        private static readonly Dictionary<string, Texture2D> ResolveCache = new Dictionary<string, Texture2D>();

        public static Texture2D Resolve(string iconName)
        {
            if (ResolveCache.TryGetValue(iconName, out Texture2D cachedTexture))
            {
                return cachedTexture;
            }

            string primaryName = EditorGUIUtility.isProSkin ? "d_" + iconName : iconName;
            string fallbackName = EditorGUIUtility.isProSkin ? iconName : "d_" + iconName;

            bool previousLogEnabled = Debug.unityLogger.logEnabled;
            Texture2D resolvedTexture;
            try
            {
                // IconContent logs a console error for unknown names; both lookups here are speculative.
                Debug.unityLogger.logEnabled = false;
                resolvedTexture = EditorGUIUtility.IconContent(primaryName).image as Texture2D;
                if (resolvedTexture == null)
                {
                    resolvedTexture = EditorGUIUtility.IconContent(fallbackName).image as Texture2D;
                }
            }
            finally
            {
                Debug.unityLogger.logEnabled = previousLogEnabled;
            }

            ResolveCache[iconName] = resolvedTexture;
            return resolvedTexture;
        }

        public static Texture2D SpinnerFrame(double editorTimeSeconds)
        {
            int frameIndex = (int)(editorTimeSeconds * 10.0) % 12;
            string frameName = SpinnerFramePrefix + frameIndex.ToString("00");
            return Resolve(frameName);
        }

        public static Image MakeIcon(string iconName, float sizePixels)
        {
            Image iconImage = new Image
            {
                image = Resolve(iconName),
                pickingMode = PickingMode.Ignore,
                scaleMode = ScaleMode.ScaleToFit,
            };
            iconImage.style.width = sizePixels;
            iconImage.style.height = sizePixels;
            iconImage.AddToClassList("worktree-icon");
            return iconImage;
        }

        public static Button MakeIconButton(System.Action onClick, string iconName, string tooltip, string fallbackText)
        {
            Button iconButton = new Button(onClick)
            {
                tooltip = tooltip,
            };
            iconButton.AddToClassList("worktree-icon-button");

            Texture2D resolvedIcon = Resolve(iconName);
            if (resolvedIcon != null)
            {
                Image childImage = new Image
                {
                    image = resolvedIcon,
                    pickingMode = PickingMode.Ignore,
                    scaleMode = ScaleMode.ScaleToFit,
                };
                childImage.style.width = 16f;
                childImage.style.height = 16f;
                childImage.AddToClassList("worktree-icon");
                iconButton.Add(childImage);
            }
            else
            {
                iconButton.text = fallbackText;
            }

            return iconButton;
        }

        public static Button MakeIconTextButton(System.Action onClick, string iconName, string tooltip, string text)
        {
            Button iconTextButton = new Button(onClick)
            {
                tooltip = tooltip,
            };
            iconTextButton.AddToClassList("worktree-icon-button");
            iconTextButton.AddToClassList("worktree-icon-button--with-text");

            Texture2D resolvedIcon = Resolve(iconName);
            if (resolvedIcon != null)
            {
                Image childImage = new Image
                {
                    image = resolvedIcon,
                    pickingMode = PickingMode.Ignore,
                    scaleMode = ScaleMode.ScaleToFit,
                };
                childImage.style.width = 16f;
                childImage.style.height = 16f;
                childImage.AddToClassList("worktree-icon");
                iconTextButton.Add(childImage);
            }

            // button.text stays empty: a Button with a child element loses its own text measure.
            Label wordLabel = new Label(text)
            {
                pickingMode = PickingMode.Ignore,
            };
            wordLabel.AddToClassList("worktree-icon-button__label");
            iconTextButton.Add(wordLabel);

            return iconTextButton;
        }
    }
}
