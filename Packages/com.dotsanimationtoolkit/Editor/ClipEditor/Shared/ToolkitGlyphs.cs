using System;
using System.Collections.Generic;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Identifies each Clip Editor tab's drawn glyph.</summary>
    public enum ToolkitGlyphId
    {
        TexturePacker, Flipbooks, ClipSets, Rigs, Materials, Events, ClipEditor, Retarget,
        VatBake, ActorProfiles, Ragdoll, Cutscenes, Capture, Stats, Health
    }

    // Signed-distance-field glyph framework. Wave B supplies the shapes in five sibling partial
    // files, each implementing one RegisterXShapes hook; this file only builds, caches and
    // rasterizes whatever has been registered.
    public static partial class ToolkitGlyphs
    {
        /// <summary>Half the shared 0.075 unit stroke width, so every glyph's lines weigh the same.</summary>
        public const float StrokeHalfWidth = 0.0375f;

        private const int GlyphSize = 32;

        private static readonly Dictionary<ToolkitGlyphId, Func<Vector2, float>> RegisteredShapes =
            new Dictionary<ToolkitGlyphId, Func<Vector2, float>>();

        private static readonly Dictionary<ToolkitGlyphId, Texture2D> ResolvedGlyphCache =
            new Dictionary<ToolkitGlyphId, Texture2D>();

        // The five hooks are erasable static partials so this file compiles today, before wave B's
        // sibling files exist — an unimplemented static partial void is legal and its call site is
        // erased by the compiler.
        static ToolkitGlyphs()
        {
            RegisterAssetShapes();
            RegisterSurfaceShapes();
            RegisterRigShapes();
            RegisterMotionShapes();
            RegisterBodyShapes();
        }

        static partial void RegisterAssetShapes();
        static partial void RegisterSurfaceShapes();
        static partial void RegisterRigShapes();
        static partial void RegisterMotionShapes();
        static partial void RegisterBodyShapes();

        public static Texture2D Resolve(ToolkitGlyphId glyphId)
        {
            if (ResolvedGlyphCache.TryGetValue(glyphId, out Texture2D cachedGlyph) && cachedGlyph != null)
            {
                return cachedGlyph;
            }

            if (!RegisteredShapes.TryGetValue(glyphId, out Func<Vector2, float> signedDistance))
            {
                return null;
            }

            Texture2D rasterizedGlyph = Rasterize(signedDistance);
            ResolvedGlyphCache[glyphId] = rasterizedGlyph;
            return rasterizedGlyph;
        }

        internal static void RegisterShape(ToolkitGlyphId glyphId, Func<Vector2, float> signedDistance)
        {
            RegisteredShapes[glyphId] = signedDistance;
        }

        // Ink is white at full RGB with the distance field carried only in alpha — tone comes later
        // from the caller's Image.tintColor, so there is no light/dark-skin variant baked in here.
        internal static Texture2D Rasterize(Func<Vector2, float> signedDistance)
        {
            Texture2D glyph = new Texture2D(GlyphSize, GlyphSize, TextureFormat.RGBA32, false);
            glyph.hideFlags = HideFlags.HideAndDontSave;
            glyph.filterMode = FilterMode.Bilinear;
            glyph.wrapMode = TextureWrapMode.Clamp;

            float edgeSoftnessUv = 1.2f / GlyphSize;
            Color[] pixels = new Color[GlyphSize * GlyphSize];

            for (int rowIndex = 0; rowIndex < GlyphSize; rowIndex++)
            {
                for (int columnIndex = 0; columnIndex < GlyphSize; columnIndex++)
                {
                    // Half a texel in, and y counted upwards — the order SetPixels expects.
                    Vector2 samplePoint = new Vector2(
                        (columnIndex + 0.5f) / GlyphSize,
                        (rowIndex + 0.5f) / GlyphSize);

                    float sampleDistance = signedDistance(samplePoint);
                    Color pixel = Color.white;
                    pixel.a = Mathf.Clamp01(0.5f - sampleDistance / (2f * edgeSoftnessUv));
                    pixels[rowIndex * GlyphSize + columnIndex] = pixel;
                }
            }

            glyph.SetPixels(pixels);
            glyph.Apply(false, false);
            return glyph;
        }

        internal static float CircleDistance(Vector2 samplePoint, Vector2 center, float radius)
        {
            return Vector2.Distance(samplePoint, center) - radius;
        }

        internal static float BoxDistance(Vector2 samplePoint, Vector2 center, Vector2 halfExtents)
        {
            float offsetX = Mathf.Abs(samplePoint.x - center.x) - halfExtents.x;
            float offsetY = Mathf.Abs(samplePoint.y - center.y) - halfExtents.y;
            float outsideDistance = new Vector2(Mathf.Max(offsetX, 0f), Mathf.Max(offsetY, 0f)).magnitude;
            return outsideDistance + Mathf.Min(Mathf.Max(offsetX, offsetY), 0f);
        }

        internal static float RoundedBoxDistance(
            Vector2 samplePoint, Vector2 center, Vector2 halfExtents, float cornerRadius)
        {
            Vector2 insetHalfExtents = halfExtents - new Vector2(cornerRadius, cornerRadius);
            return BoxDistance(samplePoint, center, insetHalfExtents) - cornerRadius;
        }

        internal static float SegmentDistance(Vector2 samplePoint, Vector2 start, Vector2 end, float halfWidth)
        {
            Vector2 segmentOffset = end - start;
            float segmentLengthSquared = Vector2.Dot(segmentOffset, segmentOffset);
            float projectedFraction = segmentLengthSquared > 0f
                ? Mathf.Clamp01(Vector2.Dot(samplePoint - start, segmentOffset) / segmentLengthSquared)
                : 0f;
            Vector2 closestPoint = start + segmentOffset * projectedFraction;
            return Vector2.Distance(samplePoint, closestPoint) - halfWidth;
        }

        internal static float Union(float firstDistance, float secondDistance)
        {
            return Mathf.Min(firstDistance, secondDistance);
        }

        internal static float Subtract(float shapeDistance, float holeDistance)
        {
            return Mathf.Max(shapeDistance, -holeDistance);
        }

        internal static float Intersect(float firstDistance, float secondDistance)
        {
            return Mathf.Max(firstDistance, secondDistance);
        }
    }
}
