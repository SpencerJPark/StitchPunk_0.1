using UnityEngine;

[CreateAssetMenu(menuName = "Stitch Punk/Terrain/Strategic Zoom Profile")]
public class StrategicZoomProfile : ScriptableObject
{
    [Tooltip("Depth of field strength over zoom mix. Forced to exactly 0 at or below dofStartMix01.")]
    public AnimationCurve dofStrength;
    public AnimationCurve groundDirectionalStrength;
    public AnimationCurve strategicLayerFade;
    [Tooltip("Float curve; consumers round it and apply the platform cap.")]
    public AnimationCurve visualLoadRadiusTiles;
    public AnimationCurve scatterDistanceMultiplier;
    public AnimationCurve hudFade;
    public AnimationCurve timeScaleTarget;

    [Header("Zoomed-out size at full zoom (1 = true size). Each category eases from 1 as the view pulls back.")]
    [Min(1f)] public float wandererZoomedScale = 1f;
    [Min(1f)] public float hordeZoomedScale = 4f;
    [Min(1f)] public float vehicleZoomedScale = 6f;
    [Min(1f)] public float landmarkZoomedScale = 8f;
    [Min(1f)] public float playerZoomedScale = 7f;

    [Range(0f, 1f)] public float dofStartMix01 = 0.75f;
    [Min(0.05f)] public float transitionSeconds = 1.5f;
    public AnimationCurve transitionEase;

    private void Reset()
    {
        ApplyDefaults();
    }

    public static StrategicZoomProfile CreateDefault()
    {
        StrategicZoomProfile profile = CreateInstance<StrategicZoomProfile>();
        profile.ApplyDefaults();
        return profile;
    }

    public float EvaluateDofStrength(float mix01)
    {
        float clampedMix = Mathf.Clamp01(mix01);
        if (clampedMix <= dofStartMix01)
        {
            return 0f;
        }
        return Mathf.Clamp01(dofStrength.Evaluate(clampedMix));
    }

    public float EvaluateGroundDirectionalStrength(float mix01)
    {
        return groundDirectionalStrength.Evaluate(Mathf.Clamp01(mix01));
    }

    public float EvaluateStrategicLayerFade(float mix01)
    {
        return Mathf.Clamp01(strategicLayerFade.Evaluate(Mathf.Clamp01(mix01)));
    }

    public float EvaluateVisualLoadRadiusTiles(float mix01, int platformCap)
    {
        float upperBound = Mathf.Max(1, platformCap);
        return Mathf.Clamp(visualLoadRadiusTiles.Evaluate(Mathf.Clamp01(mix01)), 1f, upperBound);
    }

    public float EvaluateScatterDistanceMultiplier(float mix01)
    {
        return scatterDistanceMultiplier.Evaluate(Mathf.Clamp01(mix01));
    }

    public float EvaluateHudFade(float mix01)
    {
        return Mathf.Clamp01(hudFade.Evaluate(Mathf.Clamp01(mix01)));
    }

    // Out parameters because the Data assembly cannot see the ECS component that carries these.
    public void EvaluateSizeScales(float mix01, out float wanderer, out float horde, out float vehicle, out float landmark, out float player)
    {
        float eased = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(mix01));
        wanderer = Mathf.Lerp(1f, wandererZoomedScale, eased);
        horde = Mathf.Lerp(1f, hordeZoomedScale, eased);
        vehicle = Mathf.Lerp(1f, vehicleZoomedScale, eased);
        landmark = Mathf.Lerp(1f, landmarkZoomedScale, eased);
        player = Mathf.Lerp(1f, playerZoomedScale, eased);
    }

    public float EvaluateTimeScaleTarget(float mix01)
    {
        return timeScaleTarget.Evaluate(Mathf.Clamp01(mix01));
    }

    private void ApplyDefaults()
    {
        dofStartMix01 = 0.75f;
        transitionSeconds = 1.5f;
        dofStrength = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 0f),
            new Keyframe(dofStartMix01, 0f, 0f, 0f),
            new Keyframe(1f, 1f, 0f, 0f));
        groundDirectionalStrength = AnimationCurve.Linear(0f, 1f, 1f, 1.6f);
        strategicLayerFade = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 0f),
            new Keyframe(0.1f, 0f, 0f, 0f),
            new Keyframe(1f, 1f, 0f, 0f));
        visualLoadRadiusTiles = AnimationCurve.Linear(0f, 1f, 1f, 2f);
        scatterDistanceMultiplier = AnimationCurve.Linear(0f, 1f, 1f, 0.4f);
        hudFade = new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.6f, 0f),
            new Keyframe(1f, 1f));
        timeScaleTarget = AnimationCurve.Linear(0f, 1f, 1f, 4f);
        transitionEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    }
}
