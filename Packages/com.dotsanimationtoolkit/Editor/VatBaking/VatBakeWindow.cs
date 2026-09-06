// Copyright (c) 2026 Spencer Park. All rights reserved.

using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Standalone VAT bake window: a host for one <see cref="VatBakePanel"/> and nothing else.
    /// </summary>
    public sealed class VatBakeWindow : EditorWindow
    {
        [MenuItem("Window/DOTS Animation Toolkit/VAT Bake")]
        public static void ShowWindow()
        {
            VatBakeWindow window = GetWindow<VatBakeWindow>();
            window.titleContent = new GUIContent("VAT Bake");
            window.minSize = new Vector2(420f, 380f);
        }

        private void CreateGUI()
        {
            rootVisualElement.Add(new VatBakePanel());
        }
    }
}
