using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule.Util;

public class ScreenSpaceOutlinesFeature : ScriptableRendererFeature
{
    [SerializeField] private Settings settings = new Settings();
    private DrawNormalsLayerPass drawNormalsLayerPass;
    private ScreenSpaceOutlinesPass outlinesPass;
    
    public class TextureRefData : ContextItem
    {
        public TextureHandle normalsTextureHandle = TextureHandle.nullHandle;
        public TextureHandle outlinesTextureHandle = TextureHandle.nullHandle;
        
        public override void Reset()
        {
            normalsTextureHandle = TextureHandle.nullHandle;
            outlinesTextureHandle = TextureHandle.nullHandle;
        }
    }
    
    [Serializable]
    public class Settings
    {
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
        public LayerMask layerMask = -1;
        
        public Material normalsMaterial;
        public Material outlineMaterial;
    }
    
    public override void Create()
    {
        drawNormalsLayerPass = new DrawNormalsLayerPass();
        outlinesPass = new ScreenSpaceOutlinesPass();
        
        drawNormalsLayerPass.Setup(settings);
        outlinesPass.Setup(settings);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        // Safety check
        renderer.EnqueuePass(drawNormalsLayerPass);
        renderer.EnqueuePass(outlinesPass);
    }
    
}



public class ScreenSpaceOutlinesPass : ScriptableRenderPass
{
    const string OutlinePassName = "ScreenSpaceOutlinesPass";
    ScreenSpaceOutlinesFeature.Settings settings;
    
    public void Setup(ScreenSpaceOutlinesFeature.Settings settings)
    {
        this.settings = settings;
        renderPassEvent = settings.renderPassEvent;
        requiresIntermediateTexture = true;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        if (SafetyChecks(frameData)) return;

        UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
        
        ScreenSpaceOutlinesFeature.TextureRefData texRef = 
            frameData.GetOrCreate<ScreenSpaceOutlinesFeature.TextureRefData>();
        
        OutlinesBlitExecution(renderGraph, ref texRef.normalsTextureHandle, ref texRef.outlinesTextureHandle);
        
        resourceData.cameraColor = texRef.normalsTextureHandle;
    }

    private bool SafetyChecks(ContextContainer frameData)
    {
        if (settings.outlineMaterial == null)
        {
            Debug.LogWarning($"Skipping render pass for unassigned Outlines material.");
            return true;
        }

        if (!frameData.Contains<ScreenSpaceOutlinesFeature.TextureRefData>())
        {
            Debug.LogWarning($"Skipping render pass, issue with frame data. {frameData}.");
            return true;
        }

        return false;
    }

    private void OutlinesBlitExecution(RenderGraph renderGraph, ref TextureHandle normalsTextureHandle,
        ref TextureHandle outlinesTextureHandle)
    {
        RenderGraphUtils.BlitMaterialParameters outLinesParameters = new(normalsTextureHandle, outlinesTextureHandle, settings.outlineMaterial, 0);
        renderGraph.AddBlitPass(outLinesParameters, passName: OutlinePassName);
    }
}

class DrawNormalsLayerPass : ScriptableRenderPass
    {
        ScreenSpaceOutlinesFeature.Settings settings;
        private List<ShaderTagId> m_ShaderTagIdList;

        public void Setup(ScreenSpaceOutlinesFeature.Settings settings)
        {
            this.settings = settings;
            m_ShaderTagIdList = new List<ShaderTagId>();
        }
        
        private class PassData
        {
            public RendererListHandle rendererListHandle;
        }
        
        private void InitRendererLists(ContextContainer frameData, ref PassData passData, RenderGraph renderGraph)
        {
            UniversalRenderingData universalRenderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();
            
            var sortFlags = cameraData.defaultOpaqueSortFlags;
            RenderQueueRange renderQueueRange = RenderQueueRange.opaque;
            FilteringSettings filterSettings = new FilteringSettings(renderQueueRange, settings.layerMask);
            
            ShaderTagId[] forwardOnlyShaderTagIds = new ShaderTagId[]
            {
                new ShaderTagId("UniversalForwardOnly"),
                new ShaderTagId("UniversalForward"),
                new ShaderTagId("SRPDefaultUnlit"), // Legacy shaders (do not have a gbuffer pass) are considered forward-only for backward compatibility.
                new ShaderTagId("LightweightForward") // Legacy shaders (do not have a gbuffer pass) are considered forward-only for backward compatibility.
            };
            
            m_ShaderTagIdList.Clear();
            
            foreach (ShaderTagId sid in forwardOnlyShaderTagIds)
                m_ShaderTagIdList.Add(sid);
            
            DrawingSettings drawSettings = RenderingUtils.CreateDrawingSettings(m_ShaderTagIdList, universalRenderingData, cameraData, lightData, sortFlags);

            var param = new RendererListParams(universalRenderingData.cullResults, drawSettings, filterSettings);
            passData.rendererListHandle = renderGraph.CreateRendererList(param);
        }
        
        static void ExecutePass(PassData data, RasterGraphContext context)
        {
            context.cmd.ClearRenderTarget(RTClearFlags.Color, Color.green, 1,0);
            
            context.cmd.DrawRendererList(data.rendererListHandle);
        }
        
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            string passName = "RenderList Render Pass";
            
            ScreenSpaceOutlinesFeature.TextureRefData texRef = 
                frameData.GetOrCreate<ScreenSpaceOutlinesFeature.TextureRefData>();

            using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName, out var passData))
            {
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                
                InitRendererLists(frameData, ref passData, renderGraph);
                
                if (!passData.rendererListHandle.IsValid())
                    return;
                
                builder.UseRendererList(passData.rendererListHandle);
                
                builder.SetRenderAttachment(texRef.normalsTextureHandle, 0);
                
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) => ExecutePass(data, context));
            }
        }
    }