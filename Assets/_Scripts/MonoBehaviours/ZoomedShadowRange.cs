using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Grows the main-light shadow distance with the camera's pull-back so shadows stay on all the way to the miniature view, and shifts
// the cascade splits out so the shadow map is spent on the ground in view instead of the empty air between camera and ground.
// The pipeline asset is shared (and saved in the Editor), so the original values are always written back by Restore.
public class ZoomedShadowRange
{
    // The zoomed camera looks across the ground; the top of the screen is about this many focus-distances away.
    private const float ShadowReachPerFocusDistance = 2.4f;
    // Fraction of the focus distance that is still air above the nearest visible ground.
    private const float EmptyAirFractionOfFocusDistance = 0.5f;

    private UniversalRenderPipelineAsset capturedAsset;
    private float originalShadowDistance;
    private float originalCascade2Split;
    private Vector2 originalCascade3Split;
    private Vector3 originalCascade4Split;

    public void Apply(float cameraToFocusMetres, float maximumShadowDistanceMetres)
    {
        UniversalRenderPipelineAsset pipelineAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (pipelineAsset == null)
            return;
        if (capturedAsset != pipelineAsset)
        {
            Restore();
            Capture(pipelineAsset);
        }

        float wantedDistance = Mathf.Max(originalShadowDistance, cameraToFocusMetres * ShadowReachPerFocusDistance);
        float shadowDistance = Mathf.Min(wantedDistance, Mathf.Max(originalShadowDistance, maximumShadowDistanceMetres));
        // Ramps in from zero as the distance grows past the original, so ground level keeps the authored cascades exactly.
        float pullBackAmount = Mathf.Clamp01((shadowDistance - originalShadowDistance) / originalShadowDistance);
        float emptyAirFraction = Mathf.Clamp01(cameraToFocusMetres * EmptyAirFractionOfFocusDistance / shadowDistance) * pullBackAmount;

        pipelineAsset.shadowDistance = shadowDistance;
        pipelineAsset.cascade2Split = ShiftSplitPastAir(originalCascade2Split, emptyAirFraction);
        pipelineAsset.cascade3Split = new Vector2(
            ShiftSplitPastAir(originalCascade3Split.x, emptyAirFraction),
            ShiftSplitPastAir(originalCascade3Split.y, emptyAirFraction));
        pipelineAsset.cascade4Split = new Vector3(
            ShiftSplitPastAir(originalCascade4Split.x, emptyAirFraction),
            ShiftSplitPastAir(originalCascade4Split.y, emptyAirFraction),
            ShiftSplitPastAir(originalCascade4Split.z, emptyAirFraction));
    }

    public void Restore()
    {
        if (capturedAsset == null)
            return;
        capturedAsset.shadowDistance = originalShadowDistance;
        capturedAsset.cascade2Split = originalCascade2Split;
        capturedAsset.cascade3Split = originalCascade3Split;
        capturedAsset.cascade4Split = originalCascade4Split;
        capturedAsset = null;
    }

    private void Capture(UniversalRenderPipelineAsset pipelineAsset)
    {
        capturedAsset = pipelineAsset;
        originalShadowDistance = pipelineAsset.shadowDistance;
        originalCascade2Split = pipelineAsset.cascade2Split;
        originalCascade3Split = pipelineAsset.cascade3Split;
        originalCascade4Split = pipelineAsset.cascade4Split;
    }

    private static float ShiftSplitPastAir(float originalSplit, float emptyAirFraction)
    {
        return emptyAirFraction + (1f - emptyAirFraction) * originalSplit;
    }
}
