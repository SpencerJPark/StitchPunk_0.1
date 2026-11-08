using Unity.Cinemachine;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class StrategicRigDriver : MonoBehaviour
{
    private static readonly int StrategicLayerFadeId = Shader.PropertyToID("_StrategicLayerFade");
    private static readonly int TerrainDirectionalStrengthId = Shader.PropertyToID("_TerrainDirectionalStrength");
    private static readonly int TerrainFocusPositionId = Shader.PropertyToID("_TerrainFocusPosition");
    private static readonly int TiltShiftStrengthId = Shader.PropertyToID("_TiltShiftStrength");
    private static readonly int TiltShiftCenterYId = Shader.PropertyToID("_TiltShiftCenterY");
    private static readonly int TiltShiftBandHalfHeightId = Shader.PropertyToID("_TiltShiftBandHalfHeight");
    private static readonly int TiltShiftFalloffId = Shader.PropertyToID("_TiltShiftFalloff");
    private static readonly int TiltShiftMaxPixelsId = Shader.PropertyToID("_TiltShiftMaxPixels");
    private static readonly int ZoomOutlineSuppressionId = Shader.PropertyToID("_ZoomOutlineSuppression");

    [SerializeField] private CinemachineMixingCamera mixingCamera;
    [SerializeField] private CinemachineCamera caravanCamera;
    [SerializeField] private CinemachineCamera strategicCamera;
    [SerializeField] private StrategicZoomProfile zoomProfile;
    [SerializeField] private Volume depthOfFieldVolume;
    [SerializeField] private Transform focusTarget;
    [SerializeField] private ScriptableRendererFeature tiltShiftFeature;
    [Tooltip("Edge-detect outline passes: fine at ground level, thick black borders from the miniature camera.")]
    [SerializeField] private ScriptableRendererFeature[] outlineFeaturesOffWhileZoomed;

    public float ScatterDistanceMultiplier { get; private set; } = 1f;
    public float HudFade { get; private set; }

    // Above the camera manager's active priority so the miniature view is live on foot as well as in the caravan.
    private const int LivePriorityWhileZoomed = 500;
    private const float TiltShiftBandHalfHeight = 0.18f;
    private const float TiltShiftFalloff = 0.40f;
    private const float TiltShiftMaxPixels = 12f;

    private World cachedSizeWorld;
    private EntityQuery sizeScalesQuery;
    private bool takenOver;
    private readonly ZoomedShadowRange zoomedShadowRange = new ZoomedShadowRange();
    private Transform originalCaravanFollow;
    private Transform originalStrategicFollow;

    private void OnDisable()
    {
        Shader.SetGlobalFloat(StrategicLayerFadeId, 0f);
        Shader.SetGlobalFloat(TerrainDirectionalStrengthId, 1f);
        ScatterDistanceMultiplier = 1f;
        HudFade = 0f;
        Shader.SetGlobalFloat(TiltShiftStrengthId, 0f);
        Shader.SetGlobalFloat(ZoomOutlineSuppressionId, 0f);
        zoomedShadowRange.Restore();
        SetTiltShiftActive(false);
        SetOutlinesActive(true);
        if (depthOfFieldVolume != null)
            depthOfFieldVolume.enabled = false;
    }

    private void LateUpdate()
    {
        float mix = StrategicZoomState.Mix01;
        UpdateTakeover(mix > 0.001f);
        ApplyMixingWeights(mix);

        if (focusTarget != null)
            Shader.SetGlobalVector(TerrainFocusPositionId, focusTarget.position);
        UpdateShadowRange(mix > 0.001f);

        if (zoomProfile == null)
            return;

        Shader.SetGlobalFloat(StrategicLayerFadeId, zoomProfile.EvaluateStrategicLayerFade(mix));
        Shader.SetGlobalFloat(TerrainDirectionalStrengthId, zoomProfile.EvaluateGroundDirectionalStrength(mix));
        ScatterDistanceMultiplier = zoomProfile.EvaluateScatterDistanceMultiplier(mix);
        HudFade = zoomProfile.EvaluateHudFade(mix);

        ApplyDepthOfField(zoomProfile.EvaluateDofStrength(mix));
        ApplyOutlineFade(mix, zoomProfile.dofStartMix01);
        StrategicSizeScales sizeScales = new StrategicSizeScales();
        zoomProfile.EvaluateSizeScales(mix, out sizeScales.wandererScale, out sizeScales.hordeScale, out sizeScales.vehicleScale,
            out sizeScales.landmarkScale, out sizeScales.playerScale);
        PublishSizeScales(sizeScales);
    }

    private void PublishSizeScales(StrategicSizeScales scales)
    {
        World defaultWorld = World.DefaultGameObjectInjectionWorld;
        if (defaultWorld == null || !defaultWorld.IsCreated)
            return;
        if (cachedSizeWorld != defaultWorld)
        {
            cachedSizeWorld = defaultWorld;
            sizeScalesQuery = defaultWorld.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<StrategicSizeScales>());
        }
        if (sizeScalesQuery.CalculateEntityCount() == 0)
            defaultWorld.EntityManager.CreateSingleton(StrategicSizeScales.Identity());
        sizeScalesQuery.SetSingleton(scales);
    }

    // While zoomed the whole rig follows the player's camera target (it sits on the caravan seat when driving), whichever camera the
    // manager had live. CameraManager.SwitchCamera rewrites the mixing camera's priority on every vehicle enter and exit, so the
    // priority and follow target are re-asserted every zoomed frame; otherwise the view drops to ground level with everything still scaled up.
    private void UpdateTakeover(bool zoomed)
    {
        if (mixingCamera == null || caravanCamera == null || strategicCamera == null)
            return;

        if (zoomed)
        {
            if (!takenOver)
            {
                takenOver = true;
                originalCaravanFollow = caravanCamera.Follow;
                originalStrategicFollow = strategicCamera.Follow;
            }

            PrioritySettings live = mixingCamera.Priority;
            live.Enabled = true;
            live.Value = LivePriorityWhileZoomed;
            mixingCamera.Priority = live;

            Transform followTarget = ResolveZoomedFollowTarget();
            if (followTarget != null)
            {
                if (caravanCamera.Follow != followTarget)
                    caravanCamera.Follow = followTarget;
                if (strategicCamera.Follow != followTarget)
                    strategicCamera.Follow = followTarget;
                focusTarget = followTarget;
            }
        }
        else if (takenOver)
        {
            takenOver = false;
            caravanCamera.Follow = originalCaravanFollow;
            strategicCamera.Follow = originalStrategicFollow;
            // Hand the priorities back to whichever camera the manager considers live right now.
            CameraManager manager = CameraManager.Instance;
            if (manager != null)
                manager.SwitchCamera(manager.GetCurrentCamera());
        }
    }

    // While driving, the player's own camera target stays where they boarded; the caravan's target is the one that moves, so following
    // the player's would park the view at the boarding spot and snap it back on exit.
    private Transform ResolveZoomedFollowTarget()
    {
        CameraManager manager = CameraManager.Instance;
        bool driving = manager != null && manager.GetCurrentCamera() == CinemachineCameraType.Vehicle;
        if (driving && originalCaravanFollow != null)
            return originalCaravanFollow;
        return ResolvePlayerFollowTarget();
    }

    private static Transform ResolvePlayerFollowTarget()
    {
        CameraManager manager = CameraManager.Instance;
        if (manager == null)
            return null;
        System.Reflection.FieldInfo playerCamField = typeof(CameraManager).GetField("playerCam",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        CinemachineVirtualCameraBase playerCamera = playerCamField != null ? playerCamField.GetValue(manager) as CinemachineVirtualCameraBase : null;
        return playerCamera != null ? playerCamera.Follow : null;
    }

    private void ApplyMixingWeights(float mix)
    {
        if (mixingCamera == null || caravanCamera == null || strategicCamera == null)
            return;

        // Weight index is hierarchy order inside the mixing camera, not field order.
        int caravanIndex = mixingCamera.ChildCameras.IndexOf(caravanCamera);
        int strategicIndex = mixingCamera.ChildCameras.IndexOf(strategicCamera);
        if (caravanIndex < 0 || strategicIndex < 0)
            return;

        mixingCamera.SetWeight(caravanIndex, 1f - mix);
        mixingCamera.SetWeight(strategicIndex, mix);
    }

    // Screen-Y band blur and grade live in the Painted Post pass (cheap enough for Switch); the physical depth-of-field volume stays off.
    private void ApplyDepthOfField(float strength)
    {
        if (depthOfFieldVolume != null)
            depthOfFieldVolume.enabled = false;

        Shader.SetGlobalFloat(TiltShiftStrengthId, strength);
        SetTiltShiftActive(strength > 0f);
        if (strength <= 0f)
            return;

        float centerViewportY = 0.5f;
        Camera activeCamera = Camera.main;
        if (activeCamera != null && focusTarget != null)
            centerViewportY = Mathf.Clamp01(activeCamera.WorldToViewportPoint(focusTarget.position).y);
        Shader.SetGlobalFloat(TiltShiftCenterYId, centerViewportY);
        Shader.SetGlobalFloat(TiltShiftBandHalfHeightId, TiltShiftBandHalfHeight);
        Shader.SetGlobalFloat(TiltShiftFalloffId, TiltShiftFalloff);
        Shader.SetGlobalFloat(TiltShiftMaxPixelsId, TiltShiftMaxPixels);
    }

    private void UpdateShadowRange(bool zoomed)
    {
        Camera activeCamera = Camera.main;
        if (!zoomed || activeCamera == null || focusTarget == null)
        {
            zoomedShadowRange.Restore();
            return;
        }
        float cameraToFocusMetres = Vector3.Distance(activeCamera.transform.position, focusTarget.position);
        zoomedShadowRange.Apply(cameraToFocusMetres, TerrainPlatformProfile.Active.maxZoomedShadowDistanceMetres);
    }

    // Outlines fade out over the first part of the zoom (switching them off in one frame flashed black lines across the hills);
    // the passes are only switched off once fully faded, so zoomed out still saves their cost.
    private void ApplyOutlineFade(float mix, float fullyFadedAtMix)
    {
        float suppression = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(mix / Mathf.Max(fullyFadedAtMix, 0.01f)));
        Shader.SetGlobalFloat(ZoomOutlineSuppressionId, suppression);
        SetOutlinesActive(suppression < 1f);
    }

    // Renderer features are shared assets, so the on/off state is restored whenever the zoom ends or this component is disabled.
    private void SetTiltShiftActive(bool active)
    {
        if (tiltShiftFeature != null && tiltShiftFeature.isActive != active)
            tiltShiftFeature.SetActive(active);
    }

    private void SetOutlinesActive(bool active)
    {
        if (outlineFeaturesOffWhileZoomed == null)
            return;
        foreach (ScriptableRendererFeature outlineFeature in outlineFeaturesOffWhileZoomed)
        {
            if (outlineFeature != null && outlineFeature.isActive != active)
                outlineFeature.SetActive(active);
        }
    }
}
