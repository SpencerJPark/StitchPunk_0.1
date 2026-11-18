using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public class TintedGlassShadowFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public LayerMask layerMask = -1;
        [Min(64)] public int textureSize = 1024;
        [Min(1f)] public float coverageRadius = 40f;
        [Min(1f)] public float focusDistance = 20f;
        [Min(1f)] public float depthRange = 200f;
        public float depthBias = 0.002f;
        [Range(0f, 1f)] public float strength = 1f;
        public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;
    }

    public Settings settings = new Settings();

    private static readonly int TintedShadowTextureId = Shader.PropertyToID("_StitchTintedShadowTexture");
    private static readonly int TintedShadowWorldToLightId = Shader.PropertyToID("_StitchTintedShadowWorldToLight");
    private static readonly int TintedShadowParamsId = Shader.PropertyToID("_StitchTintedShadowParams");
    private const string TintedShadowLightModeTag = "StitchTintedShadow";

    private static void DisableTintedShadowReceiving()
    {
        Shader.SetGlobalVector(TintedShadowParamsId, Vector4.zero);
    }

    class TintedGlassShadowPass : ScriptableRenderPass
    {
        public Settings passSettings;

        private class PassData
        {
            public RendererListHandle rendererListHandle;
            public Matrix4x4 lightView;
            public Matrix4x4 lightProjection;
            public Matrix4x4 worldToLight;
            public Vector4 shadowParams;
            public Matrix4x4 cameraView;
            public Matrix4x4 cameraGpuProjection;
            public TextureHandle shadowTexture;
        }

        // Mirrors URP's check without resolving activeColorTexture: a TextureHandle cast to RTHandle throws outside a render func.
        private static bool IsActiveCameraTargetProjectionFlipped(UniversalCameraData cameraData, UniversalResourceData resourceData)
        {
            return !SystemInfo.graphicsUVStartsAtTop
                || cameraData.targetTexture != null
                || cameraData.cameraType == CameraType.SceneView
                || cameraData.cameraType == CameraType.Preview
                || !resourceData.isActiveTargetBackBuffer;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            bool isCameraTargetFlipped = IsActiveCameraTargetProjectionFlipped(cameraData, resourceData);

            if (lightData.mainLightIndex < 0)
            {
                DisableTintedShadowReceiving();
                return;
            }

            Matrix4x4 lightLocalToWorld = lightData.visibleLights[lightData.mainLightIndex].localToWorldMatrix;
            Quaternion lightRotation = lightLocalToWorld.rotation;
            Vector3 lightForward = lightRotation * Vector3.forward;
            Vector3 lightRight = lightRotation * Vector3.right;
            Vector3 lightUp = lightRotation * Vector3.up;

            Transform cameraTransform = cameraData.camera.transform;
            Vector3 focusPoint = cameraTransform.position + cameraTransform.forward * passSettings.focusDistance;

            // Snap in light space so the map does not shimmer as the camera moves.
            float texelSize = 2f * passSettings.coverageRadius / passSettings.textureSize;
            float snappedRight = Mathf.Floor(Vector3.Dot(focusPoint, lightRight) / texelSize) * texelSize;
            float snappedUp = Mathf.Floor(Vector3.Dot(focusPoint, lightUp) / texelSize) * texelSize;
            float alongLight = Vector3.Dot(focusPoint, lightForward);
            focusPoint = lightRight * snappedRight + lightUp * snappedUp + lightForward * alongLight;

            Vector3 eyePosition = focusPoint - lightForward * (passSettings.depthRange * 0.5f);
            Matrix4x4 lightView = Matrix4x4.Scale(new Vector3(1f, 1f, -1f))
                * Matrix4x4.TRS(eyePosition, lightRotation, Vector3.one).inverse;
            float radius = passSettings.coverageRadius;
            Matrix4x4 lightProjection = Matrix4x4.Ortho(-radius, radius, -radius, radius, 0f, passSettings.depthRange);

            Matrix4x4 scaleBias = Matrix4x4.identity;
            scaleBias.m00 = 0.5f; scaleBias.m11 = 0.5f; scaleBias.m22 = 0.5f;
            scaleBias.m03 = 0.5f; scaleBias.m13 = 0.5f; scaleBias.m23 = 0.5f;

            TextureDesc shadowTextureDesc = new TextureDesc(passSettings.textureSize, passSettings.textureSize)
            {
                colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
                depthBufferBits = DepthBits.None,
                msaaSamples = MSAASamples.None,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                clearBuffer = true,
                clearColor = Color.white,
                name = "StitchTintedShadowTexture"
            };
            TextureHandle shadowTexture = renderGraph.CreateTexture(shadowTextureDesc);

            // Glass off-screen is not in the camera cull results, so it does not cast.
            DrawingSettings drawingSettings = RenderingUtils.CreateDrawingSettings(
                new ShaderTagId(TintedShadowLightModeTag), renderingData, cameraData, lightData, SortingCriteria.None);
            FilteringSettings filteringSettings = new FilteringSettings(RenderQueueRange.all, passSettings.layerMask);
            RendererListParams rendererListParams = new RendererListParams(renderingData.cullResults, drawingSettings, filteringSettings);

            using (IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass<PassData>("Tinted Glass Shadow", out PassData passData))
            {
                passData.rendererListHandle = renderGraph.CreateRendererList(rendererListParams);
                passData.lightView = lightView;
                passData.lightProjection = lightProjection;
                passData.worldToLight = scaleBias * lightProjection * lightView;
                passData.shadowParams = new Vector4(1f, passSettings.depthBias, passSettings.strength, 0f);
                passData.cameraView = cameraData.GetViewMatrix();
                passData.cameraGpuProjection = GL.GetGPUProjectionMatrix(cameraData.GetProjectionMatrix(0), isCameraTargetFlipped);
                passData.shadowTexture = shadowTexture;

                builder.UseRendererList(passData.rendererListHandle);
                builder.SetRenderAttachment(shadowTexture, 0);
                builder.AllowGlobalStateModification(true);
                builder.SetGlobalTextureAfterPass(shadowTexture, TintedShadowTextureId);

                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    RasterCommandBuffer cmd = context.cmd;
                    cmd.SetGlobalMatrix(TintedShadowWorldToLightId, data.worldToLight);
                    cmd.SetGlobalVector(TintedShadowParamsId, data.shadowParams);

                    bool isTargetFlipped = context.GetTextureUVOrigin(data.shadowTexture) == TextureUVOrigin.BottomLeft;
                    Matrix4x4 drawProjection = GL.GetGPUProjectionMatrix(data.lightProjection, isTargetFlipped);
                    RenderingUtils.SetViewAndProjectionMatrices(cmd, data.lightView, drawProjection, false);
                    cmd.DrawRendererList(data.rendererListHandle);

                    // Render Graph does not re-apply camera matrices, so the flip must match the camera target or later draws land upside down.
                    RenderingUtils.SetViewAndProjectionMatrices(cmd, data.cameraView, data.cameraGpuProjection, false);
                });
            }
        }
    }

    private TintedGlassShadowPass m_Pass;

    public override void Create()
    {
        m_Pass = new TintedGlassShadowPass();
        m_Pass.passSettings = settings;
        m_Pass.renderPassEvent = settings.renderPassEvent;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        CameraType cameraType = renderingData.cameraData.cameraType;
        if (m_Pass == null || (cameraType != CameraType.Game && cameraType != CameraType.SceneView))
        {
            DisableTintedShadowReceiving();
            return;
        }

        m_Pass.passSettings = settings;
        m_Pass.renderPassEvent = settings.renderPassEvent;
        renderer.EnqueuePass(m_Pass);
    }

    protected override void Dispose(bool disposing)
    {
        DisableTintedShadowReceiving();
        m_Pass = null;
    }
}
