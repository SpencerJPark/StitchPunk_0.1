// Copyright (c) 2026 Spencer Park. All rights reserved.

using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Creates the neutral grey material preview proxies draw with; the caller owns and destroys it.</summary>
    public static class PreviewSurfaceMaterialResolver
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        // No static cache, because a cached material would survive a domain reload as a leaked object.
        public static Material CreateNeutralSurfaceMaterial()
        {
            Shader surfaceShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (surfaceShader == null)
            {
                surfaceShader = Shader.Find("Universal Render Pipeline/Lit");
            }
            if (surfaceShader == null)
            {
                surfaceShader = Shader.Find("Sprites/Default");
            }
            if (surfaceShader == null)
            {
                return null;
            }

            Material surfaceMaterial = new Material(surfaceShader);
            surfaceMaterial.hideFlags = HideFlags.HideAndDontSave;
            surfaceMaterial.name = "PreviewNeutralSurface";

            Color neutralColor = new Color(0.55f, 0.55f, 0.58f, 1f);
            if (surfaceMaterial.HasProperty(BaseColorId))
            {
                surfaceMaterial.SetColor(BaseColorId, neutralColor);
            }
            if (surfaceMaterial.HasProperty(ColorId))
            {
                surfaceMaterial.SetColor(ColorId, neutralColor);
            }

            return surfaceMaterial;
        }
    }
}
