using System;
using UnityEngine;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule.Util;

public class ScreenSpaceOutlines : ScriptableRendererFeature
{
    [SerializeField] private Settings settings = new Settings();
    private NormalsPrepassSetup normalsSetup;

    public override void Create()
    {
        normalsSetup = new NormalsPrepassSetup(settings);
        normalsSetup.renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(normalsSetup);
    }


    [Serializable]
    public class Settings
    {
        public LayerMask layerMask = -1;
    }

    // This pass requests URP to render a normals prepass
    class NormalsPrepassSetup : ScriptableRenderPass
    {
        private Settings settings;

        public NormalsPrepassSetup(Settings settings)
        {
            this.settings = settings;
            
            // CRITICAL: This tells URP "I need normals texture"
            // URP will automatically render a DepthNormals prepass
            ConfigureInput(ScriptableRenderPassInput.Normal);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            // By requesting normals via ConfigureInput, URP's DepthNormalOnlyPass will run
            // and populate resourceData.cameraNormalsTexture with view-space normals
            
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            
            // Check if normals texture exists
            if (resourceData.cameraNormalsTexture.IsValid())
            {
                Debug.Log("Normals texture is valid! URP rendered it for us.");
                
                // Create destination texture
                var source = resourceData.cameraNormalsTexture;
                var destDesc = renderGraph.GetTextureDesc(resourceData.activeColorTexture);
                destDesc.name = "NormalsCopy";
                destDesc.clearBuffer = false;
                var destination = renderGraph.CreateTexture(destDesc);
                
                // Blit normals to destination (no material = direct copy)
                var blitParams = new RenderGraphUtils.BlitMaterialParameters(source, destination, null, 0);
                renderGraph.AddBlitPass(blitParams, passName: "Copy Normals to Screen");
                
                // Swap camera color to show the normals
                resourceData.cameraColor = destination;
            }
            else
            {
                Debug.LogWarning("Normals texture not valid! URP didn't generate it.");
            }
        }

    }
}