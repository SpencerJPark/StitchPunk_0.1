using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;

public class SilhouetteOutlineFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        [Header("Object Selection")]
        public LayerMask layerMask = -1;
        
        [Header("Outline Settings")]
        public Color outlineColor = Color.white;
        [Range(0.5f, 10f)]
        public float outlineThickness = 1.0f;
        [Range(0.01f, 1f)]
        public float edgeThreshold = 0.1f;
        
        [Header("Performance")]
        [Range(0.25f, 1f)]
        [Tooltip("Lower = faster but softer outlines")]
        public float silhouetteBufferScale = 0.5f;
        
        [Tooltip("Skip rendering in scene view")]
        public bool disableInSceneView = true;
        
        [Header("Rendering")]
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
        
        [Header("Debug")]
        public bool showSilhouetteBuffer = false;
    }

    public Settings settings = new Settings();

    public class OutlineData : ContextItem
    {
        public TextureHandle silhouetteTexture;
        public int scaledWidth;
        public int scaledHeight;
        
        public override void Reset()
        {
            silhouetteTexture = TextureHandle.nullHandle;
            scaledWidth = 0;
            scaledHeight = 0;
        }
    }

    class SilhouetteCapturePass : ScriptableRenderPass
    {
        private LayerMask m_LayerMask;
        private float m_RenderScale;
        private List<ShaderTagId> m_ShaderTagIdList = new List<ShaderTagId>();
        
        private static readonly ShaderTagId[] s_ShaderTags = new ShaderTagId[]
        {
            new ShaderTagId("UniversalForwardOnly"),
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("SRPDefaultUnlit"),
            new ShaderTagId("Universal2D")
        };

        private class PassData
        {
            public RendererListHandle opaqueListHandle;
            public RendererListHandle transparentListHandle;
        }

        public SilhouetteCapturePass(LayerMask layerMask, float renderScale)
        {
            m_LayerMask = layerMask;
            m_RenderScale = renderScale;
            m_ShaderTagIdList.AddRange(s_ShaderTags);
        }

        public void UpdateSettings(LayerMask layerMask, float renderScale)
        {
            m_LayerMask = layerMask;
            m_RenderScale = renderScale;
        }

        private RendererListHandle CreateRendererList(
            ContextContainer frameData, 
            RenderGraph renderGraph,
            RenderQueueRange queueRange,
            SortingCriteria sortingCriteria)
        {
            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();

            FilteringSettings filterSettings = new FilteringSettings(queueRange, m_LayerMask);

            DrawingSettings drawSettings = RenderingUtils.CreateDrawingSettings(
                m_ShaderTagIdList,
                renderingData,
                cameraData,
                lightData,
                sortingCriteria
            );

            var param = new RendererListParams(renderingData.cullResults, drawSettings, filterSettings);
            return renderGraph.CreateRendererList(param);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

            // Calculate scaled resolution
            int scaledWidth = Mathf.Max(64, Mathf.CeilToInt(cameraData.cameraTargetDescriptor.width * m_RenderScale));
            int scaledHeight = Mathf.Max(64, Mathf.CeilToInt(cameraData.cameraTargetDescriptor.height * m_RenderScale));

            // Single channel - we only need alpha for edge detection
            var silhouetteDesc = new TextureDesc(scaledWidth, scaledHeight)
            {
                colorFormat = GraphicsFormat.R8G8B8A8_UNorm,
                depthBufferBits = DepthBits.None,
                msaaSamples = MSAASamples.None,
                name = "SilhouetteTexture",
                clearBuffer = true,
                clearColor = Color.clear,
                filterMode = FilterMode.Bilinear
            };

            TextureHandle silhouetteTexture = renderGraph.CreateTexture(silhouetteDesc);

            var depthDesc = new TextureDesc(scaledWidth, scaledHeight)
            {
                colorFormat = GraphicsFormat.None,
                depthBufferBits = DepthBits.Depth16, // 16-bit is enough for silhouette sorting
                msaaSamples = MSAASamples.None,
                name = "SilhouetteDepth",
                clearBuffer = true
            };

            TextureHandle depthTexture = renderGraph.CreateTexture(depthDesc);

            var outlineData = frameData.Create<OutlineData>();
            outlineData.silhouetteTexture = silhouetteTexture;
            outlineData.scaledWidth = scaledWidth;
            outlineData.scaledHeight = scaledHeight;

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Silhouette Capture", out var passData))
            {
                passData.opaqueListHandle = CreateRendererList(
                    frameData, renderGraph,
                    RenderQueueRange.opaque,
                    SortingCriteria.CommonOpaque
                );
                
                passData.transparentListHandle = CreateRendererList(
                    frameData, renderGraph,
                    RenderQueueRange.transparent,
                    SortingCriteria.CommonTransparent
                );

                builder.UseRendererList(passData.opaqueListHandle);
                builder.UseRendererList(passData.transparentListHandle);
                builder.SetRenderAttachment(silhouetteTexture, 0);
                builder.SetRenderAttachmentDepth(depthTexture, AccessFlags.Write);

                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    context.cmd.DrawRendererList(data.opaqueListHandle);
                    context.cmd.DrawRendererList(data.transparentListHandle);
                });
            }
        }
    }

    class OutlineCompositePass : ScriptableRenderPass
    {
        private Material m_OutlineMaterial;
        private Settings m_Settings;
        
        // Cached property IDs
        private static readonly int s_SilhouetteTextureId = Shader.PropertyToID("_SilhouetteTexture");
        private static readonly int s_MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int s_OutlineColorId = Shader.PropertyToID("_OutlineColor");
        private static readonly int s_OutlineThicknessId = Shader.PropertyToID("_OutlineThickness");
        private static readonly int s_EdgeThresholdId = Shader.PropertyToID("_EdgeThreshold");
        private static readonly int s_DebugSilhouetteId = Shader.PropertyToID("_DebugSilhouette");
        private static readonly int s_SilhouetteTexelSizeId = Shader.PropertyToID("_SilhouetteTexture_TexelSize");

        private class PassData
        {
            public Material material;
            public TextureHandle silhouetteTexture;
            public TextureHandle cameraColorCopy;
            public Color outlineColor;
            public float outlineThickness;
            public float edgeThreshold;
            public float debugSilhouette;
            public Vector4 silhouetteTexelSize;
        }

        private class CopyPassData
        {
            public TextureHandle source;
        }

        public OutlineCompositePass(Material material, Settings settings)
        {
            m_OutlineMaterial = material;
            m_Settings = settings;
        }

        public void UpdateSettings(Settings settings)
        {
            m_Settings = settings;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

            var outlineData = frameData.GetOrCreate<OutlineData>();
            if (!outlineData.silhouetteTexture.IsValid())
                return;

            var copyDesc = new TextureDesc(
                cameraData.cameraTargetDescriptor.width,
                cameraData.cameraTargetDescriptor.height
            )
            {
                colorFormat = GraphicsFormat.R8G8B8A8_UNorm,
                depthBufferBits = DepthBits.None,
                name = "CameraColorCopy"
            };

            TextureHandle cameraColorCopy = renderGraph.CreateTexture(copyDesc);

            using (var builder = renderGraph.AddRasterRenderPass<CopyPassData>("Copy Camera Color", out var copyData))
            {
                copyData.source = resourceData.activeColorTexture;

                builder.UseTexture(resourceData.activeColorTexture, AccessFlags.Read);
                builder.SetRenderAttachment(cameraColorCopy, 0);

                builder.SetRenderFunc(static (CopyPassData data, RasterGraphContext context) =>
                {
                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), 0, false);
                });
            }

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Outline Composite", out var passData))
            {
                passData.material = m_OutlineMaterial;
                passData.silhouetteTexture = outlineData.silhouetteTexture;
                passData.cameraColorCopy = cameraColorCopy;
                passData.outlineColor = m_Settings.outlineColor;
                passData.outlineThickness = m_Settings.outlineThickness;
                passData.edgeThreshold = m_Settings.edgeThreshold;
                passData.debugSilhouette = m_Settings.showSilhouetteBuffer ? 1f : 0f;
                passData.silhouetteTexelSize = new Vector4(
                    1f / outlineData.scaledWidth,
                    1f / outlineData.scaledHeight,
                    outlineData.scaledWidth,
                    outlineData.scaledHeight
                );

                builder.UseTexture(outlineData.silhouetteTexture, AccessFlags.Read);
                builder.UseTexture(cameraColorCopy, AccessFlags.Read);
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0);

                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    data.material.SetTexture(s_SilhouetteTextureId, data.silhouetteTexture);
                    data.material.SetTexture(s_MainTexId, data.cameraColorCopy);
                    data.material.SetColor(s_OutlineColorId, data.outlineColor);
                    data.material.SetFloat(s_OutlineThicknessId, data.outlineThickness);
                    data.material.SetFloat(s_EdgeThresholdId, data.edgeThreshold);
                    data.material.SetFloat(s_DebugSilhouetteId, data.debugSilhouette);
                    data.material.SetVector(s_SilhouetteTexelSizeId, data.silhouetteTexelSize);

                    Blitter.BlitTexture(context.cmd, data.cameraColorCopy, new Vector4(1, 1, 0, 0), data.material, 0);
                });
            }
        }
    }

    private Material m_OutlineMaterial;
    private Shader m_OutlineShader;
    private SilhouetteCapturePass m_SilhouettePass;
    private OutlineCompositePass m_CompositePass;

    public override void Create()
    {
        m_OutlineShader = Shader.Find("Hidden/SilhouetteOutline");
        if (m_OutlineShader == null)
        {
            Debug.LogError("SilhouetteOutline: Could not find Hidden/SilhouetteOutline shader!");
            return;
        }

        m_OutlineMaterial = CoreUtils.CreateEngineMaterial(m_OutlineShader);

        m_SilhouettePass = new SilhouetteCapturePass(settings.layerMask, settings.silhouetteBufferScale);
        m_SilhouettePass.renderPassEvent = settings.renderPassEvent;

        m_CompositePass = new OutlineCompositePass(m_OutlineMaterial, settings);
        m_CompositePass.renderPassEvent = settings.renderPassEvent;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (m_OutlineMaterial == null)
            return;

        // Skip scene view if desired
        if (settings.disableInSceneView && renderingData.cameraData.isSceneViewCamera)
            return;
            
        // Skip preview cameras
        if (renderingData.cameraData.isPreviewCamera)
            return;

        m_SilhouettePass.UpdateSettings(settings.layerMask, settings.silhouetteBufferScale);
        m_CompositePass.UpdateSettings(settings);

        renderer.EnqueuePass(m_SilhouettePass);
        renderer.EnqueuePass(m_CompositePass);
    }

    protected override void Dispose(bool disposing)
    {
        if (m_OutlineMaterial != null)
        {
            CoreUtils.Destroy(m_OutlineMaterial);
            m_OutlineMaterial = null;
        }
    }
}