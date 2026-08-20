// Copyright (c) 2026 Stitch Punk. All rights reserved.

using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Creates the material every line-drawn preview overlay uses — grid, selection box, bone
    /// handles.
    /// </summary>
    /// <remarks>
    /// One place for the shader choice because the fallback chain is the interesting part.
    /// <c>Hidden/Internal-Colored</c> is the editor's own line shader: it multiplies vertex colour
    /// by <c>_Color</c> and declares no <c>LightMode</c> pass tag, which URP renders as
    /// <c>SRPDefaultUnlit</c>. The fallbacks mean a missing shader degrades to a flat-coloured
    /// overlay rather than a magenta one — an overlay is never worth throwing over.
    /// </remarks>
    public static class PreviewLineMaterial
    {
        /// <summary>Creates a hidden, unsaved line material, or null if no shader resolves.</summary>
        public static Material Create(string materialName)
        {
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
