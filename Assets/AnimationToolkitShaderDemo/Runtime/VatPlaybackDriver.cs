// Copyright (c) 2026 Stitch Punk. All rights reserved.

using UnityEngine;

namespace StitchPunk.AnimationToolkitShaderDemo
{
    /// <summary>
    /// Advances <c>_VatFrameA</c> on a material so a VAT mesh plays back — the GameObject-side
    /// equivalent of what <c>VatMaterialSystem</c> does per entity.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Demo scaffolding. The shipped path is <c>VatMaterialSystem</c> writing
    /// <c>VatFrameAProperty</c> per entity; this exists so the VAT shader can be seen working
    /// without standing up a whole entity scene, and so the non-instanced fallback branch of
    /// <c>ToolkitInstancing.hlsl</c> gets exercised.
    /// </para>
    /// <para>
    /// It writes a <em>fractional</em> frame on purpose. The shader lerps between floor and ceil,
    /// so playback is smooth at any framerate rather than stepping at the bake rate — and driving
    /// it with a rounded frame would hide whether that lerp works at all.
    /// </para>
    /// </remarks>
    [ExecuteAlways]
    [AddComponentMenu("DOTS Animation Toolkit/VAT Playback Driver")]
    public sealed class VatPlaybackDriver : MonoBehaviour
    {
        private static readonly int VatFrameAId = Shader.PropertyToID("_VatFrameA");
        private static readonly int VatFrameBId = Shader.PropertyToID("_VatFrameB");
        private static readonly int VatBlendId = Shader.PropertyToID("_VatBlend");

        [Tooltip("First global frame of the clip in the VAT texture.")]
        public int frameStart;

        [Tooltip("Frames the clip owns, including any loop-safe duplicate.")]
        public int frameCount = 61;

        [Tooltip("Playback rate. The bake rate reproduces the source timing.")]
        public float framesPerSecond = 30f;

        private MaterialPropertyBlock propertyBlock;
        private Renderer targetRenderer;
        private float elapsedFrames;

        private void OnEnable()
        {
            targetRenderer = GetComponent<Renderer>();
            propertyBlock = new MaterialPropertyBlock();
            elapsedFrames = 0f;
        }

        private void Update()
        {
            if (targetRenderer == null || frameCount <= 0)
            {
                return;
            }

            elapsedFrames += Time.deltaTime * framesPerSecond;

            // The loop-safe duplicate is the last frame, so wrapping over frameCount - 1 lands back
            // on a frame identical to the first and the seam is invisible.
            float loopLength = Mathf.Max(1f, frameCount - 1f);
            float localFrame = Mathf.Repeat(elapsedFrames, loopLength);

            targetRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetFloat(VatFrameAId, frameStart + localFrame);
            propertyBlock.SetFloat(VatFrameBId, frameStart + localFrame);
            propertyBlock.SetFloat(VatBlendId, 0f);
            targetRenderer.SetPropertyBlock(propertyBlock);
        }
    }
}
