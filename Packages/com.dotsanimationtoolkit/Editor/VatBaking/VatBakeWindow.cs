// Copyright (c) 2026 Spencer Park. All rights reserved.

using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Standalone VAT bake window: a host for one <see cref="VatBakePanel"/>, its transport keys, and
    /// the shared stylesheet the panel's Preview pane chrome needs.
    /// </summary>
    public sealed class VatBakeWindow : EditorWindow
    {
        private VatBakePanel panel;
        private bool hasRegisteredRootKeyDown;

        // The panel follows a shared selection wherever it is hosted; standalone, this window is
        // the only writer, so the selection is its own and the panel's fields are the only pickers.
        private readonly ActiveAssetSelection standaloneSelection = new ActiveAssetSelection();

        [MenuItem("Window/DOTS Animation Toolkit/VAT Bake")]
        public static void ShowWindow()
        {
            VatBakeWindow window = GetWindow<VatBakeWindow>();
            window.titleContent = new GUIContent("VAT Bake");
            window.minSize = new Vector2(960f, 560f);
        }

        // Unity calls this again after any domain reload that recreates the window. rootVisualElement
        // itself can survive that and keep whatever was registered on it directly, so the KeyDownEvent
        // registration below is guarded — a second registration would double every keystroke.
        private void CreateGUI()
        {
            rootVisualElement.Clear();

            StyleSheet styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(ClipEditorWindow.StyleSheetPath);
            if (styleSheet != null && !rootVisualElement.styleSheets.Contains(styleSheet))
            {
                rootVisualElement.styleSheets.Add(styleSheet);
            }
            rootVisualElement.AddToClassList("clip-editor__root");

            panel = new VatBakePanel();
            panel.Bind(standaloneSelection);
            rootVisualElement.Add(panel);

            if (!hasRegisteredRootKeyDown)
            {
                rootVisualElement.RegisterCallback<KeyDownEvent>(OnRootKeyDown);
                hasRegisteredRootKeyDown = true;
            }
        }

        private void OnRootKeyDown(KeyDownEvent keyEvent)
        {
            ITransportTarget target = panel != null ? panel.TransportTarget : null;
            if (target == null)
            {
                return;
            }

            int stepAmount = keyEvent.shiftKey ? 10 : 1;
            switch (keyEvent.keyCode)
            {
                case KeyCode.Space:
                    target.TogglePlay();
                    keyEvent.StopPropagation();
                    break;
                case KeyCode.Home:
                    target.JumpToStart();
                    keyEvent.StopPropagation();
                    break;
                case KeyCode.End:
                    target.JumpToEnd();
                    keyEvent.StopPropagation();
                    break;
                case KeyCode.LeftArrow:
                    target.Step(-stepAmount);
                    keyEvent.StopPropagation();
                    break;
                case KeyCode.RightArrow:
                    target.Step(stepAmount);
                    keyEvent.StopPropagation();
                    break;
            }
        }
    }
}
