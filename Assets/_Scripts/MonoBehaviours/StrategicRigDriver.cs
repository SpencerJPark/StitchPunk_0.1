using Unity.Cinemachine;
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

    private DepthOfField depthOfField;

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
        depthOfField.mode.Override(DepthOfFieldMode.Bokeh);

        Camera activeCamera = Camera.main;
        if (activeCamera != null && focusTarget != null)
        {
            float focusDistance = Vector3.Distance(activeCamera.transform.position, focusTarget.position);
            depthOfField.focusDistance.Override(Mathf.Max(0.1f, focusDistance));
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
