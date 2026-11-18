using UnityEngine;

// Pushes the shader globals PaintedGroundCore.hlsl reads; w = 0 on the ambient vector means no effect.
[ExecuteAlways]
public class TerrainLightingBridge : MonoBehaviour
{
    static readonly int AmbientColorPropertyId = Shader.PropertyToID("_TerrainAmbientColor");
    static readonly int LightningFlashPropertyId = Shader.PropertyToID("_TerrainLightningFlash");
    static readonly int ShadowSofteningPropertyId = Shader.PropertyToID("_StitchShadowSoftening");
    static readonly int ShadowBandHardnessPropertyId = Shader.PropertyToID("_StitchShadowBandHardness");
    static readonly int HatchTexturePropertyId = Shader.PropertyToID("_StitchHatchTexture");
    static readonly int HatchParamsPropertyId = Shader.PropertyToID("_StitchHatchParams");
    static readonly int InkColorPropertyId = Shader.PropertyToID("_StitchInkColor");
    static readonly int HatchBoilPropertyId = Shader.PropertyToID("_StitchHatchBoil");

    public enum HatchSpaceMode
    {
        WorldTriplanar = 0,
        CameraPlaneWorldAnchored = 1,
        CameraPlaneObjectAnchored = 2,
    }

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

    [Header("Ink look (hatched shadows, paper grain)")]
    [Tooltip("T_InkHatch: R = hatch strokes, G = paper grain. Without it the hatch and grain stay off.")]
    public Texture2D hatchTexture;

    [Range(0f, 1f)]
    public float hatchStrength = 0f;

    [Min(0.05f)]
    [Tooltip("Metres per hatch texture tile. Keep it a divisor of 100 so the hatch stays continuous across terrain tiles.")]
    public float hatchTileMetres = 10f;

    [Tooltip("World = glued to the world. Camera plane = always / on screen. Object anchored = the hatch rides with each moving object.")]
    public HatchSpaceMode hatchSpaceMode = HatchSpaceMode.CameraPlaneObjectAnchored;

    [Range(0f, 1f)]
    public float paperGrainStrength = 0f;

    [Tooltip("One pen for the hatch strokes.")]
    public Color inkColor = new Color(0.36f, 0.24f, 0.15f, 1f);

    [Min(0f)]
    [Tooltip("Hand-drawn shimmer: the hatch jumps between three offsets this many times a second. 0 = off.")]
    public float hatchBoilFramesPerSecond = 0f;

    [Range(0f, 1f)]
    public float hatchBoilOffsetTiles = 0.35f;

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
        PushInkLookGlobals();
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
        Shader.SetGlobalVector(HatchParamsPropertyId, Vector4.zero);
        Shader.SetGlobalVector(HatchBoilPropertyId, Vector4.zero);
    }

    void PushInkLookGlobals()
    {
        bool hasHatchTexture = hatchTexture != null;
        if (hasHatchTexture)
            Shader.SetGlobalTexture(HatchTexturePropertyId, hatchTexture);
        Vector4 hatchParams = new Vector4(
            hasHatchTexture ? hatchStrength : 0f,
            hatchTileMetres,
            (float)hatchSpaceMode,
            hasHatchTexture ? paperGrainStrength : 0f);
        Shader.SetGlobalVector(HatchParamsPropertyId, hatchParams);
        Shader.SetGlobalVector(InkColorPropertyId, new Vector4(inkColor.r, inkColor.g, inkColor.b, 1f));
        Shader.SetGlobalVector(HatchBoilPropertyId, new Vector4(hatchBoilFramesPerSecond, hatchBoilOffsetTiles, 0f, 0f));
    }

    internal static float AdvanceFlash(float currentFlash, float flashPerSecondDecay, float deltaTime)
    {
        return Mathf.Max(0f, currentFlash - flashPerSecondDecay * deltaTime);
    }
}
