using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

/// <summary>
/// Renders a specific layer to a texture, exposes that as a global texture (_LayerTexture),
/// then runs a fullscreen material that samples the camera source via _BlitTexture and the
/// layer mask via _LayerTexture.
/// </summary>
public class LayerScreenSpaceFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class LayerScreenSpaceSettings
    {
        [Header("Layer")]
        [Tooltip("Which layer to render separately")]
        public LayerMask targetLayer = -1;

        [Header("Material")]
        [Tooltip("Full-screen material to apply (processes the layer render)")]
        public Material screenSpaceMaterial;

        [Header("Pass")]
        [Tooltip("Material pass index to use (-1 = all passes)")]
        public int passMaterialIndex = 0;

        [Header("Debug")]
        [Tooltip("Debug mode to visualize textures")]
        public DebugMode debugMode = DebugMode.None;
    }

    public enum DebugMode
    {
        None,
        ShowLayerOnly,
        ShowCameraOnly,
        SplitScreen
    }

    public LayerScreenSpaceSettings settings = new LayerScreenSpaceSettings();

    private LayerScreenSpacePass renderPass;

    public override void Create()
    {
        renderPass = new LayerScreenSpacePass(settings);

        // Game-view safe injection point for fullscreen effects
        renderPass.renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (renderingData.cameraData.isPreviewCamera)
            return;

        if (settings.screenSpaceMaterial == null)
        {
            Debug.LogWarning("[LayerScreenSpaceFeature] Screen space material not assigned!");
            return;
        }

        renderer.EnqueuePass(renderPass);
    }

    protected override void Dispose(bool disposing)
    {
        renderPass?.Dispose();
    }

    class LayerScreenSpacePass : ScriptableRenderPass
    {
        private static readonly int LayerTextureId = Shader.PropertyToID("_LayerTexture");

        private readonly LayerScreenSpaceSettings settings;

        private FilteringSettings filteringSettings;

        private readonly ShaderTagId[] shaderTagIds = new ShaderTagId[]
        {
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly"),
            new ShaderTagId("SRPDefaultUnlit")
        };

        public LayerScreenSpacePass(LayerScreenSpaceSettings settings)
        {
            this.settings = settings;

            // Use inherited profilingSampler to avoid CS0108 warning
            this.profilingSampler = new ProfilingSampler("Layer Screen Space");

            filteringSettings = new FilteringSettings(RenderQueueRange.all, settings.targetLayer);

            // Critical for Game view: ensures URP uses an intermediate color texture
            requiresIntermediateTexture = true;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (settings.screenSpaceMaterial == null)
                return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();

            // Create layer render texture
            RenderTextureDescriptor layerDesc = cameraData.cameraTargetDescriptor;
            layerDesc.depthBufferBits = 0;
            layerDesc.msaaSamples = 1;

            TextureHandle layerTexture = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph, layerDesc, "_LayerTexture_Internal", false, FilterMode.Bilinear);

            // Create depth texture for proper rendering
            RenderTextureDescriptor depthDesc = cameraData.cameraTargetDescriptor;
            depthDesc.colorFormat = RenderTextureFormat.Depth;
            depthDesc.depthBufferBits = 24;
            depthDesc.msaaSamples = 1;

            TextureHandle depthTexture = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph, depthDesc, "_LayerDepth", false, FilterMode.Point);

            // Pass 1: Render target layer to layerTexture, then publish it as global _LayerTexture
            RenderLayerPass(renderGraph, layerTexture, depthTexture, renderingData, cameraData);

            // Pass 2: Fullscreen material composites to cameraColor using _BlitTexture + global _LayerTexture
            CompositeToScreenPass(renderGraph, resourceData, cameraData);
        }

        private void RenderLayerPass(
            RenderGraph renderGraph,
            TextureHandle layerTexture,
            TextureHandle depthTexture,
            UniversalRenderingData renderingData,
            UniversalCameraData cameraData)
        {
            using (IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass<LayerPassData>(
                "Render Target Layer", out LayerPassData passData, profilingSampler))
            {
                SortingSettings sortingSettings = new SortingSettings(cameraData.camera)
                {
                    criteria = SortingCriteria.CommonOpaque
                };

                DrawingSettings drawingSettings = new DrawingSettings(shaderTagIds[0], sortingSettings)
                {
                    perObjectData = PerObjectData.None,
                    enableDynamicBatching = false,
                    enableInstancing = true
                };

                for (int i = 1; i < shaderTagIds.Length; i++)
                    drawingSettings.SetShaderPassName(i, shaderTagIds[i]);

                RendererListParams rendererParams = new RendererListParams(
                    renderingData.cullResults, drawingSettings, filteringSettings);

                passData.rendererList = renderGraph.CreateRendererList(rendererParams);

                builder.UseRendererList(passData.rendererList);

                builder.SetRenderAttachment(layerTexture, 0, AccessFlags.Write);
                builder.SetRenderAttachmentDepth(depthTexture, AccessFlags.Write);

                // ✅ URP 17 correct way to make the texture accessible to later passes/shaders
                builder.SetGlobalTextureAfterPass(layerTexture, LayerTextureId);

                builder.AllowPassCulling(false);

                builder.SetRenderFunc((LayerPassData data, RasterGraphContext context) =>
                {
                    context.cmd.ClearRenderTarget(true, true, Color.clear);
                    context.cmd.DrawRendererList(data.rendererList);
                });
            }
        }

        private void CompositeToScreenPass(
            RenderGraph renderGraph,
            UniversalResourceData resourceData,
            UniversalCameraData cameraData)
        {
            TextureHandle cameraColor = resourceData.activeColorTexture;

            // Debug path not updated here; keep normal path focused.
            if (settings.debugMode != DebugMode.None)
            {
                // If you want, I can update your debug shader path to also use global textures + _BlitTexture.
                return;
            }

            RenderTextureDescriptor tempDesc = cameraData.cameraTargetDescriptor;
            tempDesc.depthBufferBits = 0;

            TextureHandle tempTexture = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph, tempDesc, "_LayerScreenSpaceTemp", false);

            // Pass 1: Apply fullscreen material to temp
            using (IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass<CompositePassData>(
                "Apply Layer Screen Space Material", out CompositePassData passData, profilingSampler))
            {
                passData.material = settings.screenSpaceMaterial;
                passData.passIndex = settings.passMaterialIndex;
                passData.cameraTexture = cameraColor;

                // We read:
                builder.UseTexture(cameraColor, AccessFlags.Read);

                // ✅ We also read the global _LayerTexture that was published in the previous pass
                builder.UseGlobalTexture(LayerTextureId, AccessFlags.Read);

                builder.SetRenderAttachment(tempTexture, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((CompositePassData data, RasterGraphContext context) =>
                {
                    // IMPORTANT:
                    // - Blitter binds source as _BlitTexture
                    // - Shader should sample camera from _BlitTexture
                    // - Shader should sample layer from global _LayerTexture

                    Blitter.BlitTexture(
                        context.cmd,
                        data.cameraTexture,
                        new Vector4(1, 1, 0, 0),
                        data.material,
                        data.passIndex
                    );
                });
            }

            // Pass 2: Copy temp back to camera color
            using (IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass<CopyPassData>(
                "Copy To Camera", out CopyPassData passData, profilingSampler))
            {
                passData.source = tempTexture;

                builder.UseTexture(tempTexture, AccessFlags.Read);
                builder.SetRenderAttachment(cameraColor, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((CopyPassData data, RasterGraphContext context) =>
                {
                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), 0, false);
                });
            }
        }

        public void Dispose()
        {
            // User owns materials; nothing to destroy.
        }

        private class LayerPassData
        {
            internal RendererListHandle rendererList;
        }

        private class CompositePassData
        {
            internal Material material;
            internal int passIndex;
            internal TextureHandle cameraTexture;
        }

        private class CopyPassData
        {
            internal TextureHandle source;
        }
    }
}
