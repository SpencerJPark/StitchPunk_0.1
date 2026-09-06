// Copyright (c) 2026 Spencer Park. All rights reserved.

using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Creates the material every line-drawn preview overlay uses — grid, selection box, bone
    /// handles.
    /// </summary>
    public static class PreviewLineMaterial
    {
        /// <summary>Creates a hidden, unsaved line material, or null if no shader resolves.</summary>
        public static Material Create(string materialName)
        {
            // Hidden/Internal-Colored has no LightMode tag, which URP renders as SRPDefaultUnlit;
            // the fallbacks mean a missing shader degrades to flat-coloured rather than magenta.
            Shader lineShader = Shader.Find("Hidden/Internal-Colored");
            if (lineShader == null)
            {
                lineShader = Shader.Find("Universal Render Pipeline/Unlit");
            }
            if (lineShader == null)
            {
                lineShader = Shader.Find("Sprites/Default");
            }
            if (lineShader == null)
            {
                return null;
            }

            Material lineMaterial = new Material(lineShader);
            lineMaterial.hideFlags = HideFlags.HideAndDontSave;
            lineMaterial.name = materialName;
            return lineMaterial;
        }
    }
}
