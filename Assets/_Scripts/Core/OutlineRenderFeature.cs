using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Render Feature that creates outline effect by:
/// 1. Rendering outlined entities to a separate texture
/// 2. Processing that texture to create an outline
/// 3. Compositing the outline back onto the screen
/// </summary>
public class OutlineRenderFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class OutlineSettings
    {
        public LayerMask outlineLayer = -1;
        public Color outlineColor = Color.white;
        [Range(1f, 10f)] public float outlineWidth = 2f;
        public Material outlineMaterial;
    }

    public OutlineSettings settings = new OutlineSettings();

    private OutlineRenderPass outlinePass;
    private OutlineBlitPass blitPass;

    public override void Create()
    {
        outlinePass = new OutlineRenderPass(settings);
        outlinePass.renderPassEvent = RenderPassEvent.AfterRenderingTransparents;

        blitPass = new OutlineBlitPass(settings);
        blitPass.renderPassEvent = RenderPassEvent.AfterRenderingTransparents + 1;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (settings.outlineMaterial == null)
        {
            Debug.LogWarning("Outline material is not assigned in OutlineRenderFeature settings!");
            return;
        }

        outlinePass.Setup(renderer.cameraColorTargetHandle);
        renderer.EnqueuePass(outlinePass);
        
        blitPass.Setup(renderer.cameraColorTargetHandle);
        renderer.EnqueuePass(blitPass);
    }

    protected override void Dispose(bool disposing)
    {
        outlinePass?.Dispose();
        blitPass?.Dispose();
    }

    /// <summary>
    /// Pass that renders entities with OutlinedTag to a separate texture
    /// </summary>
    class OutlineRenderPass : ScriptableRenderPass
    {
        private OutlineSettings settings;
        private RTHandle maskRTHandle;
        private FilteringSettings filteringSettings;
        private RenderStateBlock renderStateBlock;
        
        private readonly ShaderTagId shaderTagId = new ShaderTagId("UniversalForward");
        private readonly ShaderTagId[] shaderTagIds = new ShaderTagId[] 
        { 
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly"),
            new ShaderTagId("SRPDefaultUnlit")
        };

        public OutlineRenderPass(OutlineSettings settings)
        {
            this.settings = settings;
            
            filteringSettings = new FilteringSettings(RenderQueueRange.all, settings.outlineLayer);
            renderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);
        }

        public void Setup(RTHandle colorTarget)
        {
            // We'll allocate the mask texture in Configure
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            // Create mask texture
            RenderTextureDescriptor maskDescriptor = cameraTextureDescriptor;
            maskDescriptor.colorFormat = RenderTextureFormat.R8;
            maskDescriptor.depthBufferBits = 0;
            maskDescriptor.msaaSamples = 1;

            RenderingUtils.ReAllocateIfNeeded(ref maskRTHandle, maskDescriptor, FilterMode.Bilinear, 
                TextureWrapMode.Clamp, name: "_OutlineMask");

            ConfigureTarget(maskRTHandle);
            ConfigureClear(ClearFlag.Color, Color.clear);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get("OutlineMask");

            // Clear the mask
            cmd.ClearRenderTarget(true, true, Color.clear);

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            // Setup drawing settings
            var sortingSettings = new SortingSettings(renderingData.cameraData.camera)
            {
                criteria = SortingCriteria.CommonOpaque
            };

            var drawingSettings = new DrawingSettings(shaderTagId, sortingSettings)
            {
                perObjectData = PerObjectData.None,
                enableDynamicBatching = false,
                enableInstancing = true
            };

            // Add all shader tags
            for (int i = 1; i < shaderTagIds.Length; i++)
            {
                drawingSettings.SetShaderPassName(i, shaderTagIds[i]);
            }

            // Override to render white color to mask
            drawingSettings.overrideMaterial = new Material(Shader.Find("Hidden/OutlineMask"));
            drawingSettings.overrideMaterialPassIndex = 0;

            // Draw only entities on the outline layer (those with OutlinedTag enabled)
            context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref filteringSettings, ref renderStateBlock);

            // Set global texture for next pass
            cmd.SetGlobalTexture("_OutlineMask", maskRTHandle);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public void Dispose()
        {
            maskRTHandle?.Release();
        }
    }

    /// <summary>
    /// Pass that creates and composites the outline onto the final image
    /// </summary>
    class OutlineBlitPass : ScriptableRenderPass
    {
        private OutlineSettings settings;
        private RTHandle cameraColorTarget;
        private RTHandle tempRTHandle;
        private Material outlineMaterial;

        public OutlineBlitPass(OutlineSettings settings)
        {
            this.settings = settings;
            this.outlineMaterial = settings.outlineMaterial;
        }

        public void Setup(RTHandle colorTarget)
        {
            this.cameraColorTarget = colorTarget;
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            // Create temporary texture for processing
            RenderTextureDescriptor tempDescriptor = cameraTextureDescriptor;
            tempDescriptor.depthBufferBits = 0;

            RenderingUtils.ReAllocateIfNeeded(ref tempRTHandle, tempDescriptor, FilterMode.Bilinear, 
                TextureWrapMode.Clamp, name: "_OutlineTemp");
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (outlineMaterial == null) return;

            CommandBuffer cmd = CommandBufferPool.Get("OutlineComposite");

            // Set shader properties
            outlineMaterial.SetColor("_OutlineColor", settings.outlineColor);
            outlineMaterial.SetFloat("_OutlineWidth", settings.outlineWidth);

            // Blit with outline shader
            Blitter.BlitCameraTexture(cmd, cameraColorTarget, tempRTHandle, outlineMaterial, 0);
            Blitter.BlitCameraTexture(cmd, tempRTHandle, cameraColorTarget);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public void Dispose()
        {
            tempRTHandle?.Release();
        }
    }
}