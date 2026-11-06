using UnityEngine;

// Pushes the shader globals PaintedGroundCore.hlsl reads; w = 0 on the ambient vector means no effect.
[ExecuteAlways]
public class TerrainLightingBridge : MonoBehaviour
{
    static readonly int AmbientColorPropertyId = Shader.PropertyToID("_TerrainAmbientColor");
    static readonly int LightningFlashPropertyId = Shader.PropertyToID("_TerrainLightningFlash");

    public Color ambientTint = Color.white;

    [Range(0f, 1f)]
    public float ambientTintStrength = 0f;

    float currentFlash;
    float flashDecayPerSecond;

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
        // Decay after the push so a seconds <= 0 strike is visible for exactly one frame.
        currentFlash = AdvanceFlash(currentFlash, flashDecayPerSecond, Time.unscaledDeltaTime);
    }

    void OnDisable()
    {
        currentFlash = 0f;
        Shader.SetGlobalVector(AmbientColorPropertyId, Vector4.zero);
        Shader.SetGlobalFloat(LightningFlashPropertyId, 0f);
    }

    internal static float AdvanceFlash(float currentFlash, float flashPerSecondDecay, float deltaTime)
    {
        return Mathf.Max(0f, currentFlash - flashPerSecondDecay * deltaTime);
    }
}
