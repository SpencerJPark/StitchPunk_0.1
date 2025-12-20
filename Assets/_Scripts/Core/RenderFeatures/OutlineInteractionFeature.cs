// using UnityEngine;
// using UnityEngine.Rendering;
// using UnityEngine.Rendering.Universal;
// using UnityEngine.Rendering.RenderGraphModule;
// using Unity.Entities;
//
// /// <summary>
// /// Render feature that renders outline effect directly to screen
// /// Queries DOTS Player entity to determine if outline should render (early out optimization)
// /// </summary>
// public class OutlineInteractionFeature : ScriptableRendererFeature
// {
//     [System.Serializable]
//     public class OutlineSettings
//     {
//         [Header("Shader")]
//         [Tooltip("Your Shader Graph that processes the outline layer texture")]
//         public Shader outlineShader;
//         
//         [Header("Outline Settings")]
//         public Color outlineColor = Color.white;
//         [Range(1f, 10f)] public float outlineWidth = 2f;
//     }
//
//     public OutlineSettings settings = new OutlineSettings();
//
//     private OutlineRenderPass outlinePass;
//
//     public override void Create()
//     {
//         outlinePass = new OutlineRenderPass(settings);
//         outlinePass.renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
//     }
//
//     public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
//     {
//         // Early out check - only add passes if we should render outline
//         if (!outlinePass.ShouldRenderOutline())
//         {
//             return;
//         }
//         
//         renderer.EnqueuePass(outlinePass);
//     }
//
//     protected override void Dispose(bool disposing)
//     {
//         outlinePass?.Dispose();
//     }
//
//     class OutlineRenderPass : ScriptableRenderPass
//     {
//         private OutlineSettings settings;
//         private Material outlineMaterial;
//         private ProfilingSampler profilingSampler;
//         
//         private FilteringSettings outlineFilterSettings;
//         
//         // DOTS integration
//         private EntityManager entityManager;
//         private Entity playerEntity;
//         private bool dotsWorldInitialized;
//         
//         private readonly ShaderTagId[] shaderTagIds = new ShaderTagId[]
//         {
//             new ShaderTagId("UniversalForward"),
//             new ShaderTagId("UniversalForwardOnly"),
//             new ShaderTagId("SRPDefaultUnlit")
//         };
//
//         public OutlineRenderPass(OutlineSettings settings)
//         {
//             this.settings = settings;
//             profilingSampler = new ProfilingSampler("Outline Camera Feature");
//             
//             // Create material from your Shader Graph
//             if (settings.outlineShader != null)
//             {
//                 outlineMaterial = new Material(settings.outlineShader);
//             }
//             else
//             {
//                 Debug.LogError("[OutlineCameraFeature] Outline shader not assigned!");
//             }
//             
//             // Setup filter for outline layer only
//             int outlineLayerMask = 1 << GameAssets.OUTLINE_LAYER;
//             outlineFilterSettings = new FilteringSettings(RenderQueueRange.all, outlineLayerMask);
//             
//             // Initialize DOTS
//             TryInitializeDOTS();
//         }
//
//         private void TryInitializeDOTS()
//         {
//             World defaultWorld = World.DefaultGameObjectInjectionWorld;
//             if (defaultWorld != null && defaultWorld.IsCreated)
//             {
//                 entityManager = defaultWorld.EntityManager;
//                 dotsWorldInitialized = true;
//             }
//         }
//
//         public bool ShouldRenderOutline()
//         {
//             // Early out if material not ready
//             if (outlineMaterial == null)
//             {
//                 return false;
//             }
//             
//             // Early out if DOTS world not ready
//             if (!dotsWorldInitialized)
//             {
//                 TryInitializeDOTS();
//                 if (!dotsWorldInitialized)
//                 {
//                     return false;
//                 }
//             }
//             
//             // Find player entity if we don't have it
//             if (playerEntity == Entity.Null || !entityManager.Exists(playerEntity))
//             {
//                 EntityQuery playerQuery = entityManager.CreateEntityQuery(typeof(Player));
//                 
//                 if (playerQuery.IsEmpty)
//                 {
//                     return false;
//                 }
//                 
//                 playerEntity = playerQuery.GetSingletonEntity();
//             }
//             
//             // Check if player has an interactable entity assigned
//             if (entityManager.HasComponent<Player>(playerEntity))
//             {
//                 Player playerData = entityManager.GetComponentData<Player>(playerEntity);
//                 
//                 // Early out if no interactable
//                 if (playerData.interactableEntity == Entity.Null)
//                 {
//                     return false;
//                 }
//                 
//                 // Check if interactable entity still exists
//                 if (!entityManager.Exists(playerData.interactableEntity))
//                 {
//                     return false;
//                 }
//                 
//                 return true;
//             }
//             
//             return false;
//         }
//
//         public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
//         {
//             // This should never be called if ShouldRenderOutline returned false
//             // but double-check just in case
//             if (!ShouldRenderOutline())
//             {
//                 Debug.Log("[OutlineCameraFeature] ShouldRenderOutline returned false - skipping");
//                 return;
//             }
//
//             Debug.Log("[OutlineCameraFeature] Recording render graph - outline should render");
//
//             UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
//             UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
//             UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
//
//             // Create outline layer texture (with alpha channel for Shader Graph)
//             RenderTextureDescriptor outlineDesc = cameraData.cameraTargetDescriptor;
//             outlineDesc.colorFormat = RenderTextureFormat.ARGB32; // Need alpha channel
//             outlineDesc.depthBufferBits = 0;
//             outlineDesc.msaaSamples = 1;
//
//             TextureHandle outlineTexture = UniversalRenderer.CreateRenderGraphTexture(
//                 renderGraph, outlineDesc, "_OutlineLayerTexture", false, FilterMode.Bilinear);
//             
//             Debug.Log($"[OutlineCameraFeature] Created outline texture: {outlineDesc.width}x{outlineDesc.height}");
//
//             // Create separate depth texture
//             RenderTextureDescriptor depthDesc = cameraData.cameraTargetDescriptor;
//             depthDesc.colorFormat = RenderTextureFormat.Depth;
//             depthDesc.depthBufferBits = 24;
//             depthDesc.msaaSamples = 1;
//
//             TextureHandle depthTexture = UniversalRenderer.CreateRenderGraphTexture(
//                 renderGraph, depthDesc, "_OutlineDepth", false, FilterMode.Point);
//
//             // Pass 1: Render outline layer to texture
//             RenderOutlineLayerPass(renderGraph, outlineTexture, depthTexture, renderingData, cameraData);
//
//             // Pass 2: Apply shader and composite to screen
//             CompositeToScreenPass(renderGraph, outlineTexture, resourceData, cameraData);
//             
//             Debug.Log("[OutlineCameraFeature] Render graph recording complete");
//         }
//
//         private void RenderOutlineLayerPass(
//             RenderGraph renderGraph,
//             TextureHandle outlineTexture,
//             TextureHandle depthTexture,
//             UniversalRenderingData renderingData,
//             UniversalCameraData cameraData)
//         {
//             using (IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass<OutlineLayerPassData>(
//                 "Render Outline Layer", out OutlineLayerPassData passData, profilingSampler))
//             {
//                 // Setup pass data
//                 passData.outlineFilterSettings = outlineFilterSettings;
//                 passData.shaderTagIds = shaderTagIds;
//                 passData.camera = cameraData.camera;
//
//                 // Create renderer list for outline layer - render NORMALLY (not with override)
//                 SortingSettings sortingSettings = new SortingSettings(cameraData.camera)
//                 {
//                     criteria = SortingCriteria.CommonOpaque
//                 };
//
//                 DrawingSettings drawingSettings = new DrawingSettings(shaderTagIds[0], sortingSettings)
//                 {
//                     perObjectData = PerObjectData.None,
//                     enableDynamicBatching = false,
//                     enableInstancing = true
//                     // NO overrideMaterial - render with their actual materials!
//                 };
//
//                 for (int i = 1; i < shaderTagIds.Length; i++)
//                     drawingSettings.SetShaderPassName(i, shaderTagIds[i]);
//
//                 RendererListParams rendererParams = new RendererListParams(
//                     renderingData.cullResults, drawingSettings, outlineFilterSettings);
//                 passData.rendererList = renderGraph.CreateRendererList(rendererParams);
//
//                 builder.UseRendererList(passData.rendererList);
//                 builder.SetRenderAttachment(outlineTexture, 0, AccessFlags.Write);
//                 builder.SetRenderAttachmentDepth(depthTexture, AccessFlags.Write);
//                 builder.AllowPassCulling(false);
//
//                 builder.SetRenderFunc((OutlineLayerPassData data, RasterGraphContext context) =>
//                 {
//                     // Clear to black
//                     context.cmd.ClearRenderTarget(true, true, Color.black);
//
//                     // Draw outline layer objects with their ACTUAL materials
//                     context.cmd.DrawRendererList(data.rendererList);
//                 });
//             }
//         }
//
//         private void CompositeToScreenPass(
//             RenderGraph renderGraph,
//             TextureHandle outlineTexture,
//             UniversalResourceData resourceData,
//             UniversalCameraData cameraData)
//         {
//             // Create temporary texture for the shader pass
//             RenderTextureDescriptor tempDesc = cameraData.cameraTargetDescriptor;
//             tempDesc.depthBufferBits = 0;
//
//             TextureHandle tempTexture = UniversalRenderer.CreateRenderGraphTexture(
//                 renderGraph, tempDesc, "_OutlineTemp", false);
//
//             TextureHandle cameraColor = resourceData.activeColorTexture;
//
//             // First pass: Apply your Shader Graph to temp
//             using (IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass<CompositePassData>(
//                 "Outline Shader Pass", out CompositePassData passData, profilingSampler))
//             {
//                 passData.outlineMaterial = outlineMaterial;
//                 passData.outlineColor = settings.outlineColor;
//                 passData.outlineWidth = settings.outlineWidth;
//                 passData.outlineTexture = outlineTexture;
//                 passData.source = cameraColor;
//
//                 builder.UseTexture(outlineTexture, AccessFlags.Read);
//                 builder.UseTexture(cameraColor, AccessFlags.Read);
//                 builder.SetRenderAttachment(tempTexture, 0, AccessFlags.Write);
//                 builder.AllowPassCulling(false);
//
//                 builder.SetRenderFunc((CompositePassData data, RasterGraphContext context) =>
//                 {
//                     Debug.Log("[OutlineCameraFeature] Composite pass executing");
//                     Debug.Log($"[OutlineCameraFeature] Outline material: {data.outlineMaterial != null}");
//                     
//                     // Set your Shader Graph properties
//                     
//                     // _MainTex = outline layer texture (objects with alpha)
//                     data.outlineMaterial.SetTexture("_MainTex", data.outlineTexture);
//                     
//                     // _CameraTexture = the actual game view
//                     data.outlineMaterial.SetTexture("_CameraTexture", data.source);
//                     
//                     // _OutlineColor = outline color setting
//                     data.outlineMaterial.SetColor("_OutlineColor", data.outlineColor);
//                     
//                     // _OutlineWidth = outline width setting
//                     data.outlineMaterial.SetFloat("_OutlineWidth", data.outlineWidth);
//                     
//                     Debug.Log($"[OutlineCameraFeature] Set textures and properties, blitting now...");
//
//                     // Blit through your Shader Graph
//                     // Source doesn't matter since shader uses _MainTex and _CameraTexture directly
//                     Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), 
//                         data.outlineMaterial, 0);
//                         
//                     Debug.Log("[OutlineCameraFeature] Blit complete");
//                 });
//             }
//
//             // Second pass: Copy temp back to camera
//             using (IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass<CopyPassData>(
//                 "Outline Copy To Screen", out CopyPassData passData, profilingSampler))
//             {
//                 passData.source = tempTexture;
//
//                 builder.UseTexture(tempTexture, AccessFlags.Read);
//                 builder.SetRenderAttachment(cameraColor, 0, AccessFlags.Write);
//                 builder.AllowPassCulling(false);
//
//                 builder.SetRenderFunc((CopyPassData data, RasterGraphContext context) =>
//                 {
//                     // Simple copy to screen
//                     Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), 0, false);
//                 });
//             }
//         }
//
//         public void Dispose()
//         {
//             if (outlineMaterial != null)
//                 Object.DestroyImmediate(outlineMaterial);
//         }
//
//         private class OutlineLayerPassData
//         {
//             internal FilteringSettings outlineFilterSettings;
//             internal ShaderTagId[] shaderTagIds;
//             internal Camera camera;
//             internal RendererListHandle rendererList;
//         }
//
//         private class CompositePassData
//         {
//             internal Material outlineMaterial;
//             internal Color outlineColor;
//             internal float outlineWidth;
//             internal TextureHandle outlineTexture;
//             internal TextureHandle source;
//         }
//
//         private class CopyPassData
//         {
//             internal TextureHandle source;
//         }
//     }
// }