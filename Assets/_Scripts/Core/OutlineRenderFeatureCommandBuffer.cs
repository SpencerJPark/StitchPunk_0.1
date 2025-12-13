using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Alternative implementation using command buffer injection
/// Useful if you need more control over the rendering pipeline
/// </summary>
public class OutlineRenderFeatureCommandBuffer : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public LayerMask outlineLayer = -1;
        public Color outlineColor = Color.white;
        [Range(1f, 10f)] public float outlineWidth = 2f;
        public Shader maskShader;
        public Shader outlineShader;
    }

    public Settings settings = new Settings();
    
    private OutlinePass outlinePass;
    private Material maskMaterial;
    private Material outlineMaterial;

    public override void Create()
    {
        if (settings.maskShader != null)
            maskMaterial = new Material(settings.maskShader);
        
        if (settings.outlineShader != null)
            outlineMaterial = new Material(settings.outlineShader);

        outlinePass = new OutlinePass(settings, maskMaterial, outlineMaterial);
        outlinePass.renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (maskMaterial == null || outlineMaterial == null)
        {
            Debug.LogWarning("Outline materials are not initialized!");
            return;
        }

        outlinePass.Setup(renderer.cameraColorTargetHandle);
        renderer.EnqueuePass(outlinePass);
    }

    protected override void Dispose(bool disposing)
    {
        if (maskMaterial != null)
            Object.DestroyImmediate(maskMaterial);
        if (outlineMaterial != null)
            Object.DestroyImmediate(outlineMaterial);
            
        outlinePass?.Dispose();
    }

    class OutlinePass : ScriptableRenderPass
    {
        private Settings settings;
        private Material maskMaterial;
        private Material outlineMaterial;
        private RTHandle maskRT;
        private RTHandle tempRT;
        private RTHandle colorTarget;
        
        private FilteringSettings filteringSettings;
        private readonly ShaderTagId[] shaderTagIds = new ShaderTagId[] 
        { 
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly"),
            new ShaderTagId("SRPDefaultUnlit")
        };

        public OutlinePass(Settings settings, Material maskMaterial, Material outlineMaterial)
        {
            this.settings = settings;
            this.maskMaterial = maskMaterial;
            this.outlineMaterial = outlineMaterial;
            
            filteringSettings = new FilteringSettings(RenderQueueRange.all, settings.outlineLayer);
        }

        public void Setup(RTHandle colorTarget)
        {
            this.colorTarget = colorTarget;
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            // Allocate render textures
            var maskDescriptor = cameraTextureDescriptor;
            maskDescriptor.colorFormat = RenderTextureFormat.R8;
            maskDescriptor.depthBufferBits = 0;
            maskDescriptor.msaaSamples = 1;

            RenderingUtils.ReAllocateIfNeeded(ref maskRT, maskDescriptor, FilterMode.Bilinear, 
                TextureWrapMode.Clamp, name: "_OutlineMaskRT");

            var tempDescriptor = cameraTextureDescriptor;
            tempDescriptor.depthBufferBits = 0;
            
            RenderingUtils.ReAllocateIfNeeded(ref tempRT, tempDescriptor, FilterMode.Bilinear, 
                TextureWrapMode.Clamp, name: "_OutlineTempRT");
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get("OutlineEffect");

            // Step 1: Render mask
            ConfigureTarget(maskRT);
            cmd.ClearRenderTarget(true, true, Color.clear);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            var sortingSettings = new SortingSettings(renderingData.cameraData.camera)
            {
                criteria = SortingCriteria.CommonOpaque
            };

            var drawingSettings = new DrawingSettings(shaderTagIds[0], sortingSettings)
            {
                perObjectData = PerObjectData.None,
                enableDynamicBatching = false,
                enableInstancing = true,
                overrideMaterial = maskMaterial,
                overrideMaterialPassIndex = 0
            };

            for (int i = 1; i < shaderTagIds.Length; i++)
            {
                drawingSettings.SetShaderPassName(i, shaderTagIds[i]);
            }

            var renderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);
            context.DrawRenderers(renderingData.cullResults, ref drawingSettings, 
                ref filteringSettings, ref renderStateBlock);

            // Step 2: Generate and composite outline
            outlineMaterial.SetTexture("_OutlineMask", maskRT);
            outlineMaterial.SetColor("_OutlineColor", settings.outlineColor);
            outlineMaterial.SetFloat("_OutlineWidth", settings.outlineWidth);

            Blitter.BlitCameraTexture(cmd, colorTarget, tempRT, outlineMaterial, 0);
            Blitter.BlitCameraTexture(cmd, tempRT, colorTarget);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public void Dispose()
        {
            maskRT?.Release();
            tempRT?.Release();
        }
    }
}