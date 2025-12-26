using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;

public class CelShadingFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        [Header("Shading Bands")]
        [Range(2, 8)]
        [Tooltip("Number of discrete shading levels")]
        public int shadingSteps = 3;
        
        [Range(0f, 1f)]
        [Tooltip("Smoothness of transitions between bands")]
        public float bandSmoothness = 0.05f;
        
        [Header("Lighting")]
        [Range(0f, 1f)]
        public float shadowIntensity = 0.5f;
        
        [Range(0f, 1f)]
        public float litThreshold = 0.5f;
        
        [ColorUsage(false, false)]
        public Color shadowTint = new Color(0.4f, 0.4f, 0.6f);
        
        [Header("Specular")]
        public bool enableSpecular = true;
        
        [Range(0f, 1f)]
        public float specularThreshold = 0.9f;
        
        [Range(0f, 1f)]
        public float specularSmoothness = 0.05f;
        
        [Range(0f, 1f)]
        public float specularIntensity = 0.5f;
        
        [Header("Rim Light")]
        public bool enableRimLight = true;
        
        [Range(0f, 1f)]
        public float rimThreshold = 0.6f;
        
        [Range(0f, 1f)]
        public float rimSmoothness = 0.1f;
        
        [Range(0f, 1f)]
        public float rimIntensity = 0.3f;
        
        [ColorUsage(false, false)]
        public Color rimColor = Color.white;
        
        [Header("Color Adjustments")]
        [Range(1, 32)]
        [Tooltip("Reduce color palette for more stylized look")]
        public int colorSteps = 32;
        
        public bool enableColorQuantization = false;
        
        [Range(0f, 2f)]
        public float saturationBoost = 1.1f;
        
        [Header("Rendering")]
        public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
        
        public bool disableInSceneView = false;
        
        [Header("Debug")]
        public DebugMode debugMode = DebugMode.None;
        
        public enum DebugMode
        {
            None,
            ShadingOnly,
            NormalsOnly,
            DepthOnly,
            LightDirection
        }
    }

    public Settings settings = new Settings();

    class CelShadingPass : ScriptableRenderPass
    {
        private Material m_Material;
        private Settings m_Settings;
        
        private static readonly int s_ShadingStepsId = Shader.PropertyToID("_ShadingSteps");
        private static readonly int s_BandSmoothnessId = Shader.PropertyToID("_BandSmoothness");
        private static readonly int s_ShadowIntensityId = Shader.PropertyToID("_ShadowIntensity");
        private static readonly int s_LitThresholdId = Shader.PropertyToID("_LitThreshold");
        private static readonly int s_ShadowTintId = Shader.PropertyToID("_ShadowTint");
        private static readonly int s_SpecularThresholdId = Shader.PropertyToID("_SpecularThreshold");
        private static readonly int s_SpecularSmoothnessId = Shader.PropertyToID("_SpecularSmoothness");
        private static readonly int s_SpecularIntensityId = Shader.PropertyToID("_SpecularIntensity");
        private static readonly int s_EnableSpecularId = Shader.PropertyToID("_EnableSpecular");
        private static readonly int s_RimThresholdId = Shader.PropertyToID("_RimThreshold");
        private static readonly int s_RimSmoothnessId = Shader.PropertyToID("_RimSmoothness");
        private static readonly int s_RimIntensityId = Shader.PropertyToID("_RimIntensity");
        private static readonly int s_RimColorId = Shader.PropertyToID("_RimColor");
        private static readonly int s_EnableRimId = Shader.PropertyToID("_EnableRim");
        private static readonly int s_ColorStepsId = Shader.PropertyToID("_ColorSteps");
        private static readonly int s_EnableColorQuantId = Shader.PropertyToID("_EnableColorQuant");
        private static readonly int s_SaturationBoostId = Shader.PropertyToID("_SaturationBoost");
        private static readonly int s_DebugModeId = Shader.PropertyToID("_DebugMode");

        private class PassData
        {
            public Material material;
            public TextureHandle cameraColor;
            public TextureHandle cameraDepth;
            public TextureHandle cameraNormals;
            public Settings settings;
        }

        private class CopyPassData
        {
            public TextureHandle source;
        }

        public CelShadingPass(Material material, Settings settings)
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

            // Copy camera color
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

            // Apply cel shading
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Cel Shading Pass", out var passData))
            {
                passData.material = m_Material;
                passData.cameraColor = cameraColorCopy;
                passData.cameraDepth = resourceData.cameraDepthTexture;
                passData.cameraNormals = resourceData.cameraNormalsTexture;
                passData.settings = m_Settings;

                builder.UseTexture(cameraColorCopy, AccessFlags.Read);
                builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
                
                // Normals texture might not be available
                if (resourceData.cameraNormalsTexture.IsValid())
                    builder.UseTexture(resourceData.cameraNormalsTexture, AccessFlags.Read);
                
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0);

                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    var mat = data.material;
                    var s = data.settings;
                    
                    mat.SetFloat(s_ShadingStepsId, s.shadingSteps);
                    mat.SetFloat(s_BandSmoothnessId, s.bandSmoothness);
                    mat.SetFloat(s_ShadowIntensityId, s.shadowIntensity);
                    mat.SetFloat(s_LitThresholdId, s.litThreshold);
                    mat.SetColor(s_ShadowTintId, s.shadowTint);
                    
                    mat.SetFloat(s_EnableSpecularId, s.enableSpecular ? 1 : 0);
                    mat.SetFloat(s_SpecularThresholdId, s.specularThreshold);
                    mat.SetFloat(s_SpecularSmoothnessId, s.specularSmoothness);
                    mat.SetFloat(s_SpecularIntensityId, s.specularIntensity);
                    
                    mat.SetFloat(s_EnableRimId, s.enableRimLight ? 1 : 0);
                    mat.SetFloat(s_RimThresholdId, s.rimThreshold);
                    mat.SetFloat(s_RimSmoothnessId, s.rimSmoothness);
                    mat.SetFloat(s_RimIntensityId, s.rimIntensity);
                    mat.SetColor(s_RimColorId, s.rimColor);
                    
                    mat.SetFloat(s_EnableColorQuantId, s.enableColorQuantization ? 1 : 0);
                    mat.SetFloat(s_ColorStepsId, s.colorSteps);
                    mat.SetFloat(s_SaturationBoostId, s.saturationBoost);
                    
                    mat.SetFloat(s_DebugModeId, (float)s.debugMode);

                    if (data.cameraNormals.IsValid())
                        mat.SetTexture("_CameraNormalsTexture", data.cameraNormals);

                    Blitter.BlitTexture(context.cmd, data.cameraColor, new Vector4(1, 1, 0, 0), mat, 0);
                });
            }
        }
    }

    private Material m_Material;
    private Shader m_Shader;
    private CelShadingPass m_Pass;

    public override void Create()
    {
        m_Shader = Shader.Find("Hidden/CelShading");
        if (m_Shader == null)
        {
            Debug.LogError("CelShading: Could not find Hidden/CelShading shader!");
            return;
        }

        m_Material = CoreUtils.CreateEngineMaterial(m_Shader);

        m_Pass = new CelShadingPass(m_Material, settings);
        m_Pass.renderPassEvent = settings.renderPassEvent;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (m_Material == null)
            return;

        if (settings.disableInSceneView && renderingData.cameraData.isSceneViewCamera)
            return;

        if (renderingData.cameraData.isPreviewCamera)
            return;

        m_Pass.UpdateSettings(settings);
        renderer.EnqueuePass(m_Pass);
    }

    protected override void Dispose(bool disposing)
    {
        if (m_Material != null)
        {
            CoreUtils.Destroy(m_Material);
            m_Material = null;
        }
    }
}