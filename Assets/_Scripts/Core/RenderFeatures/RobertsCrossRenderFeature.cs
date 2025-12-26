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
        public LayerMask layerMask = -1;
        public Material robertsCrossMaterial;
        public Material normalCaptureMaterial;
        public float robertsCrossMultiplier = 1.0f;
        public Color outlineColor = Color.white;
        [Range(0.01f, 10f)]
        public float outlineThickness = 1.0f;
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
    }

    public Settings settings = new Settings();

    // Custom data container to pass textures between passes
    public class CustomData : ContextItem
    {
        public TextureHandle normalsTexture;

        public override void Reset()
        {
            normalsTexture = TextureHandle.nullHandle;
        }
    }

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
        }

        private RendererListHandle CreateRendererList(ContextContainer frameData, RenderGraph renderGraph)
        {
            UniversalRenderingData universalRenderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();

            var sortFlags = cameraData.defaultOpaqueSortFlags;
            FilteringSettings filterSettings = new FilteringSettings(RenderQueueRange.opaque, m_LayerMask);

            m_ShaderTagIdList.Clear();
            m_ShaderTagIdList.Add(new ShaderTagId("UniversalForwardOnly"));
            m_ShaderTagIdList.Add(new ShaderTagId("UniversalForward"));
            m_ShaderTagIdList.Add(new ShaderTagId("SRPDefaultUnlit"));
            m_ShaderTagIdList.Add(new ShaderTagId("LightweightForward"));

            DrawingSettings drawSettings = RenderingUtils.CreateDrawingSettings(
                m_ShaderTagIdList,
                universalRenderingData,
                cameraData,
                lightData,
                sortFlags
            );

            drawSettings.overrideMaterial = m_NormalMaterial;
            drawSettings.overrideMaterialPassIndex = 0;

            var param = new RendererListParams(universalRenderingData.cullResults, drawSettings, filterSettings);
            return renderGraph.CreateRendererList(param);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            
            // Create the normals texture OUTSIDE the pass so it persists
            var normalsDescriptor = new TextureDesc(
                cameraData.cameraTargetDescriptor.width,
                cameraData.cameraTargetDescriptor.height
            );
            normalsDescriptor.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            normalsDescriptor.depthBufferBits = DepthBits.None;
            normalsDescriptor.msaaSamples = MSAASamples.None;
            normalsDescriptor.name = "ViewSpaceNormalsTexture";
            normalsDescriptor.clearBuffer = true;
            normalsDescriptor.clearColor = Color.clear;

            TextureHandle normalsTexture = renderGraph.CreateTexture(normalsDescriptor);

            // Create depth texture for this pass
            var depthDescriptor = new TextureDesc(
                cameraData.cameraTargetDescriptor.width,
                cameraData.cameraTargetDescriptor.height
            );
            depthDescriptor.colorFormat = GraphicsFormat.None;
            depthDescriptor.depthBufferBits = DepthBits.Depth24;
            depthDescriptor.msaaSamples = MSAASamples.None;
            depthDescriptor.name = "NormalCaptureDepth";
            depthDescriptor.clearBuffer = true;

            TextureHandle depthTexture = renderGraph.CreateTexture(depthDescriptor);

            // Store in custom data BEFORE the pass
            var customData = frameData.Create<CustomData>();
            customData.normalsTexture = normalsTexture;

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Normal Capture Pass", out var passData))
            {
                passData.rendererListHandle = CreateRendererList(frameData, renderGraph);

                if (!passData.rendererListHandle.IsValid())
                    return;

                builder.UseRendererList(passData.rendererListHandle);
                builder.SetRenderAttachment(normalsTexture, 0);
                builder.SetRenderAttachmentDepth(depthTexture, AccessFlags.Write);

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
        private float m_RobertsCrossMultiplier;
        private Color m_OutlineColor;
        private float m_OutlineThickness;

        private class PassData
        {
            public Material material;
            public TextureHandle normalsTexture;
            public TextureHandle cameraColorCopy;
            public float robertsCrossMultiplier;
            public Color outlineColor;
            public float outlineThickness;
        }

        public RobertsCrossPass(Material material, float multiplier, Color color, float thickness)
        {
            m_Material = material;
            m_RobertsCrossMultiplier = multiplier;
            m_OutlineColor = color;
            m_OutlineThickness = thickness;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

            var customData = frameData.GetOrCreate<CustomData>();
            if (!customData.normalsTexture.IsValid())
            {
                Debug.LogWarning("Roberts Cross: Normals texture is invalid, skipping pass");
                return;
            }

            // Create a copy of camera color to read from
            var copyDesc = new TextureDesc(
                cameraData.cameraTargetDescriptor.width,
                cameraData.cameraTargetDescriptor.height
            );
            copyDesc.colorFormat = GraphicsFormat.R8G8B8A8_UNorm;
            copyDesc.depthBufferBits = DepthBits.None;
            copyDesc.name = "CameraColorCopy";

            TextureHandle cameraColorCopy = renderGraph.CreateTexture(copyDesc);

            // First, copy the current camera color
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

            // Now apply the Roberts Cross effect
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Roberts Cross Outline Pass", out var passData))
            {
                passData.material = m_Material;
                passData.normalsTexture = customData.normalsTexture;
                passData.cameraColorCopy = cameraColorCopy;
                passData.robertsCrossMultiplier = m_RobertsCrossMultiplier;
                passData.outlineColor = m_OutlineColor;
                passData.outlineThickness = m_OutlineThickness;

                builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
                builder.UseTexture(customData.normalsTexture, AccessFlags.Read);
                builder.UseTexture(cameraColorCopy, AccessFlags.Read);
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0);

                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    data.material.SetTexture("_NormalsTexture", data.normalsTexture);
                    data.material.SetTexture("_MainTex", data.cameraColorCopy);
                    data.material.SetFloat("_RobertsCrossMultiplier", data.robertsCrossMultiplier);
                    data.material.SetColor("_OutlineColor", data.outlineColor);
                    data.material.SetFloat("_OutlineThickness", data.outlineThickness);

                    Blitter.BlitTexture(context.cmd, data.cameraColorCopy, new Vector4(1, 1, 0, 0), data.material, 0);
                });
            }
        }

        private class CopyPassData
        {
            public TextureHandle source;
        }
    }

    NormalCapturePass m_NormalCapturePass;
    RobertsCrossPass m_RobertsCrossPass;

    public override void Create()
    {
        if (settings.normalCaptureMaterial == null || settings.robertsCrossMaterial == null)
        {
            Debug.LogWarning("Roberts Cross Render Feature: Materials not assigned!");
            return;
        }

        m_NormalCapturePass = new NormalCapturePass(settings.layerMask, settings.normalCaptureMaterial);
        m_NormalCapturePass.renderPassEvent = settings.renderPassEvent;

        m_RobertsCrossPass = new RobertsCrossPass(
            settings.robertsCrossMaterial,
            settings.robertsCrossMultiplier,
            settings.outlineColor,
            settings.outlineThickness
        );
        m_RobertsCrossPass.renderPassEvent = settings.renderPassEvent;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (settings.normalCaptureMaterial == null || settings.robertsCrossMaterial == null)
            return;

        renderer.EnqueuePass(m_NormalCapturePass);
        renderer.EnqueuePass(m_RobertsCrossPass);
    }

    protected override void Dispose(bool disposing)
    {
    }
}