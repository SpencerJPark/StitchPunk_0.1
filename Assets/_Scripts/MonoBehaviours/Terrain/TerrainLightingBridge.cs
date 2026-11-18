using UnityEngine;

// Pushes the shader globals PaintedGroundCore.hlsl reads; w = 0 on the ambient vector means no effect.
[ExecuteAlways]
public class TerrainLightingBridge : MonoBehaviour
{
    static readonly int AmbientColorPropertyId = Shader.PropertyToID("_TerrainAmbientColor");
    static readonly int LightningFlashPropertyId = Shader.PropertyToID("_TerrainLightningFlash");
    static readonly int ShadowSofteningPropertyId = Shader.PropertyToID("_StitchShadowSoftening");
    static readonly int ShadowBandHardnessPropertyId = Shader.PropertyToID("_StitchShadowBandHardness");

    public Color ambientTint = Color.white;

    [Range(0f, 1f)]
    public float ambientTintStrength = 0f;

    [Header("Global shadow look (ground, units and props together)")]
    [Range(0f, 1f)]
    [Tooltip("0 = every material's own shadow strength, 1 = no cast shadows anywhere.")]
    public float shadowSoftening = 0f;

    [Range(0f, 1f)]
    [Tooltip("0 = soft painted falloff, 1 = hard cel edge on cast shadows and form shading.")]
    public float shadowBandHardness = 0f;

    float currentFlash;
    float flashDecayPerSecond;

    public float CurrentFlash => currentFlash;

    public void TriggerLightning(float strength, float seconds)
    {
        // A strike landing mid-flash keeps the brighter of the two.
        currentFlash = Mathf.Max(currentFlash, strength);
        flashDecayPerSecond = seconds > 0f ? strength / seconds : float.PositiveInfinity;
    }

    [ContextMenu("Test Lightning")]
    void TestLightning()
    {
        TriggerLightning(1.5f, 0.3f);
    }

    void Update()
    {
        Shader.SetGlobalVector(AmbientColorPropertyId, new Vector4(ambientTint.r, ambientTint.g, ambientTint.b, ambientTintStrength));
        Shader.SetGlobalFloat(LightningFlashPropertyId, currentFlash);
        Shader.SetGlobalFloat(ShadowSofteningPropertyId, shadowSoftening);
        Shader.SetGlobalFloat(ShadowBandHardnessPropertyId, shadowBandHardness);
        // Decay after the push so a seconds <= 0 strike is visible for exactly one frame.
        currentFlash = AdvanceFlash(currentFlash, flashDecayPerSecond, Time.unscaledDeltaTime);
    }

    void OnDisable()
    {
        currentFlash = 0f;
        Shader.SetGlobalVector(AmbientColorPropertyId, Vector4.zero);
        Shader.SetGlobalFloat(LightningFlashPropertyId, 0f);
        Shader.SetGlobalFloat(ShadowSofteningPropertyId, 0f);
        Shader.SetGlobalFloat(ShadowBandHardnessPropertyId, 0f);
    }

    internal static float AdvanceFlash(float currentFlash, float flashPerSecondDecay, float deltaTime)
    {
        return Mathf.Max(0f, currentFlash - flashPerSecondDecay * deltaTime);
    }
}
