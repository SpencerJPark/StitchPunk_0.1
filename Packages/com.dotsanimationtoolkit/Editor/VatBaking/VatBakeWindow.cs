// Copyright (c) 2026 Spencer Park. All rights reserved.

using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// The standalone VAT bake window (architecture section 7.1): a host for one
    /// <see cref="VatBakePanel"/> and nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The UI and the bake it drives moved into the panel.</strong> The Clip Editor shows
    /// the same panel on its VAT Bake tab, and a bake produced from either is the same bake — which
    /// only stays true while there is one implementation. What is left here is the menu entry and
    /// the window chrome, which is all a second host needs to not exist.
    /// </para>
    /// <para>
    /// Kept rather than folded into the Clip Editor outright: baking a set does not require having
    /// a clip open to edit, and a batch job or a second monitor is a reason to want it on its own.
    /// </para>
    /// </remarks>
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
