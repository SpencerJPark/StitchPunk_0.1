using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;

public class RobertsCrossRenderFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        [Header("Outline Target")]
        public LayerMask outlineLayerMask = -1;
        
        [Header("Materials")]
        public Material robertsCrossMaterial;
        public Shader normalCaptureShader;
        
        [Header("Outline Settings")]
        public float robertsCrossMultiplier = 1.0f;
        public Color outlineColor = Color.white;
        [Range(0.01f, 10f)]
        public float outlineThickness = 1.0f;
        [Range(0f, 1f)]
        public float depthThreshold = 0.1f;
        [Range(0f, 1f)]
        public float normalThreshold = 0.3f;
        
        [Header("Rendering")]
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
    }

    public Settings settings = new Settings();

    public class RobertsCrossData : ContextItem
    {
        public TextureHandle normalsTexture;
        public override void Reset()
        {
            normalsTexture = TextureHandle.nullHandle;
        }
    }

    // Normal capture pass that uses scene depth for culling
    class NormalCapturePass : ScriptableRenderPass
    {
        private LayerMask m_LayerMask;
        private Material m_NormalMaterial;
        private List<ShaderTagId> m_ShaderTagIdList = new List<ShaderTagId>();

        private class PassData
        {
            public RendererListHandle rendererListHandle;
        }

        public NormalCapturePass(LayerMask layerMask, Material normalMaterial)
        {
            m_LayerMask = layerMask;
            m_NormalMaterial = normalMaterial;
            
            m_ShaderTagIdList.Add(new ShaderTagId("UniversalForwardOnly"));
            m_ShaderTagIdList.Add(new ShaderTagId("UniversalForward"));
            m_ShaderTagIdList.Add(new ShaderTagId("SRPDefaultUnlit"));
            m_ShaderTagIdList.Add(new ShaderTagId("LightweightForward"));
        }

        public void UpdateSettings(LayerMask layerMask)
        {
            m_LayerMask = layerMask;
        }

        private RendererListHandle CreateRendererList(ContextContainer frameData, RenderGraph renderGraph)
        {
            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();

            FilteringSettings filterSettings = new FilteringSettings(RenderQueueRange.all, m_LayerMask);

            DrawingSettings drawSettings = RenderingUtils.CreateDrawingSettings(
                m_ShaderTagIdList,
                renderingData,
                cameraData,
                lightData,
                cameraData.defaultOpaqueSortFlags
            );

            drawSettings.overrideMaterial = m_NormalMaterial;
            drawSettings.overrideMaterialPassIndex = 0;

            var param = new RendererListParams(renderingData.cullResults, drawSettings, filterSettings);
            return renderGraph.CreateRendererList(param);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

            // Create normals texture
            var normalsDesc = new TextureDesc(
                cameraData.cameraTargetDescriptor.width,
                cameraData.cameraTargetDescriptor.height
            )
            {
                colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
                depthBufferBits = DepthBits.None,
                msaaSamples = MSAASamples.None,
                name = "ViewSpaceNormalsTexture",
                clearBuffer = true,
                clearColor = Color.clear
            };

            TextureHandle normalsTexture = renderGraph.CreateTexture(normalsDesc);

            var customData = frameData.GetOrCreate<RobertsCrossData>();
            customData.normalsTexture = normalsTexture;

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Normal Capture Pass", out var passData))
            {
                passData.rendererListHandle = CreateRendererList(frameData, renderGraph);

                if (!passData.rendererListHandle.IsValid())
                    return;

                builder.UseRendererList(passData.rendererListHandle);
                builder.SetRenderAttachment(normalsTexture, 0);
                
                // USE THE SCENE'S DEPTH BUFFER - this is the key!
                // Read-only so we test against it but don't write to it
                builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.Read);

                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    context.cmd.DrawRendererList(data.rendererListHandle);
                });
            }
        }
    }

    class RobertsCrossPass : ScriptableRenderPass
    {
        private Material m_Material;
        private Settings m_Settings;

        private class PassData
        {
            public Material material;
            public TextureHandle normalsTexture;
            public TextureHandle cameraColorCopy;
            public Settings settings;
        }

        private class CopyPassData
        {
            public TextureHandle source;
        }

        public RobertsCrossPass(Material material, Settings settings)
        {
            m_Material = material;
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

            var customData = frameData.GetOrCreate<RobertsCrossData>();
            if (!customData.normalsTexture.IsValid())
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

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Roberts Cross Outline Pass", out var passData))
            {
                passData.material = m_Material;
                passData.normalsTexture = customData.normalsTexture;
                passData.cameraColorCopy = cameraColorCopy;
                passData.settings = m_Settings;

                builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
                builder.UseTexture(customData.normalsTexture, AccessFlags.Read);
                builder.UseTexture(cameraColorCopy, AccessFlags.Read);
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0);

                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    data.material.SetTexture("_NormalsTexture", data.normalsTexture);
                    data.material.SetTexture("_MainTex", data.cameraColorCopy);
                    data.material.SetFloat("_RobertsCrossMultiplier", data.settings.robertsCrossMultiplier);
                    data.material.SetColor("_OutlineColor", data.settings.outlineColor);
                    data.material.SetFloat("_OutlineThickness", data.settings.outlineThickness);
                    data.material.SetFloat("_DepthThreshold", data.settings.depthThreshold);
                    data.material.SetFloat("_NormalThreshold", data.settings.normalThreshold);

                    Blitter.BlitTexture(context.cmd, data.cameraColorCopy, new Vector4(1, 1, 0, 0), data.material, 0);
                });
            }
        }
    }

    private Material m_NormalCaptureMaterial;
    private NormalCapturePass m_NormalCapturePass;
    private RobertsCrossPass m_RobertsCrossPass;

    public override void Create()
    {
        if (settings.robertsCrossMaterial == null)
        {
            Debug.LogWarning("Roberts Cross: Material not assigned!");
            return;
        }

        if (settings.normalCaptureShader == null)
            settings.normalCaptureShader = Shader.Find("Custom/ViewSpaceNormalsCapture");

        if (settings.normalCaptureShader == null)
        {
            Debug.LogError("Roberts Cross: Could not find normal capture shader!");
            return;
        }

        m_NormalCaptureMaterial = CoreUtils.CreateEngineMaterial(settings.normalCaptureShader);

        m_NormalCapturePass = new NormalCapturePass(settings.outlineLayerMask, m_NormalCaptureMaterial);
        m_NormalCapturePass.renderPassEvent = settings.renderPassEvent;

        m_RobertsCrossPass = new RobertsCrossPass(settings.robertsCrossMaterial, settings);
        m_RobertsCrossPass.renderPassEvent = settings.renderPassEvent;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (settings.robertsCrossMaterial == null || m_NormalCaptureMaterial == null)
            return;

        m_NormalCapturePass.UpdateSettings(settings.outlineLayerMask);
        m_RobertsCrossPass.UpdateSettings(settings);

        renderer.EnqueuePass(m_NormalCapturePass);
        renderer.EnqueuePass(m_RobertsCrossPass);
    }

    protected override void Dispose(bool disposing)
    {
        if (m_NormalCaptureMaterial != null)
        {
            CoreUtils.Destroy(m_NormalCaptureMaterial);
            m_NormalCaptureMaterial = null;
        }
    }
}