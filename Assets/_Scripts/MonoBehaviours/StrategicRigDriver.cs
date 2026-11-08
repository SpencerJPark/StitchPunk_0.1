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

    [SerializeField] private CinemachineMixingCamera mixingCamera;
    [SerializeField] private CinemachineCamera caravanCamera;
    [SerializeField] private CinemachineCamera strategicCamera;
    [SerializeField] private StrategicZoomProfile zoomProfile;
    [SerializeField] private Volume depthOfFieldVolume;
    [SerializeField] private Transform focusTarget;

    public float ScatterDistanceMultiplier { get; private set; } = 1f;
    public float HudFade { get; private set; }

    // Above the camera manager's active priority so the miniature view is live on foot as well as in the caravan.
    private const int LivePriorityWhileZoomed = 500;
    private const float SharpBandEndFraction = 1.05f;
    private const float BlurFullFraction = 1.9f;
    private const float GaussianBlurMaxRadius = 1.5f;

    private World cachedSizeWorld;
    private EntityQuery sizeScalesQuery;
    private DepthOfField depthOfField;
    private bool takenOver;
    private PrioritySettings originalPriority;
    private Transform originalCaravanFollow;
    private Transform originalStrategicFollow;

    private void OnDisable()
    {
        Shader.SetGlobalFloat(StrategicLayerFadeId, 0f);
        Shader.SetGlobalFloat(TerrainDirectionalStrengthId, 1f);
        ScatterDistanceMultiplier = 1f;
        HudFade = 0f;
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

        if (zoomProfile == null)
            return;

        Shader.SetGlobalFloat(StrategicLayerFadeId, zoomProfile.EvaluateStrategicLayerFade(mix));
        Shader.SetGlobalFloat(TerrainDirectionalStrengthId, zoomProfile.EvaluateGroundDirectionalStrength(mix));
        ScatterDistanceMultiplier = zoomProfile.EvaluateScatterDistanceMultiplier(mix);
        HudFade = zoomProfile.EvaluateHudFade(mix);

        ApplyDepthOfField(zoomProfile.EvaluateDofStrength(mix));
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
    // manager had live, and everything is put back when the view returns to normal.
    private void UpdateTakeover(bool zoomed)
    {
        if (mixingCamera == null || caravanCamera == null || strategicCamera == null)
            return;

        if (zoomed && !takenOver)
        {
            takenOver = true;
            originalPriority = mixingCamera.Priority;
            originalCaravanFollow = caravanCamera.Follow;
            originalStrategicFollow = strategicCamera.Follow;
            PrioritySettings live = mixingCamera.Priority;
            live.Enabled = true;
            live.Value = LivePriorityWhileZoomed;
            mixingCamera.Priority = live;

            Transform playerTarget = ResolvePlayerFollowTarget();
            if (playerTarget != null)
            {
                caravanCamera.Follow = playerTarget;
                strategicCamera.Follow = playerTarget;
                focusTarget = playerTarget;
            }
        }
        else if (!zoomed && takenOver)
        {
            takenOver = false;
            mixingCamera.Priority = originalPriority;
            caravanCamera.Follow = originalCaravanFollow;
            strategicCamera.Follow = originalStrategicFollow;
        }
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

    // The gradient-blur fallback tier keeps DoF off; a gradient blur pass is future work.
    private void ApplyDepthOfField(float strength)
    {
        if (depthOfFieldVolume == null)
            return;

        TerrainPlatformProfile platformProfile = TerrainPlatformProfile.Active;
        bool allowed = platformProfile != null && platformProfile.useDepthOfField && !platformProfile.useGradientBlurFallback;
        if (!allowed || strength <= 0f)
        {
            depthOfFieldVolume.enabled = false;
            return;
        }

        if (depthOfField == null && !ResolveDepthOfFieldOverride())
            return;

        depthOfFieldVolume.enabled = true;
        depthOfFieldVolume.weight = strength;
        depthOfField.active = true;
        // Gaussian, not Bokeh: physical lens values give no visible blur at 700 m, and it is the Switch-affordable mode.
        depthOfField.mode.Override(DepthOfFieldMode.Gaussian);
        depthOfField.gaussianMaxRadius.Override(GaussianBlurMaxRadius);

        Camera activeCamera = Camera.main;
        if (activeCamera != null && focusTarget != null)
        {
            float focusDistance = Mathf.Max(1f, Vector3.Distance(activeCamera.transform.position, focusTarget.position));
            depthOfField.gaussianStart.Override(focusDistance * SharpBandEndFraction);
            depthOfField.gaussianEnd.Override(focusDistance * BlurFullFraction);
        }
    }

    private bool ResolveDepthOfFieldOverride()
    {
        VolumeProfile profile = depthOfFieldVolume.profile;
        if (profile == null)
            return false;
        if (!profile.TryGet(out depthOfField))
            depthOfField = profile.Add<DepthOfField>(true);
        return depthOfField != null;
    }
}
