using UnityEngine;

// Pushes the shader globals PaintedGroundCore.hlsl reads; w = 0 on the ambient vector means no effect.
[ExecuteAlways]
public class TerrainLightingBridge : MonoBehaviour
{
    static readonly int AmbientColorPropertyId = Shader.PropertyToID("_TerrainAmbientColor");
    static readonly int LightningFlashPropertyId = Shader.PropertyToID("_TerrainLightningFlash");
    static readonly int ShadowSofteningPropertyId = Shader.PropertyToID("_StitchShadowSoftening");
    static readonly int ShadowBandHardnessPropertyId = Shader.PropertyToID("_StitchShadowBandHardness");
    const string InkLookKeyword = "STITCH_INK_LOOK";
    static readonly int InkColorPropertyId = Shader.PropertyToID("_StitchInkColor");
    static readonly int InkHatchParamsPropertyId = Shader.PropertyToID("_StitchInkHatchParams");
    static readonly int InkObjectHatchParamsPropertyId = Shader.PropertyToID("_StitchInkObjectHatchParams");
    static readonly int InkDetailBasePropertyId = Shader.PropertyToID("_StitchInkDetailBaseTex");
    static readonly int InkDetailRPropertyId = Shader.PropertyToID("_StitchInkDetailRTex");
    static readonly int InkDetailGPropertyId = Shader.PropertyToID("_StitchInkDetailGTex");
    static readonly int InkDetailBPropertyId = Shader.PropertyToID("_StitchInkDetailBTex");
    static readonly int TransitionAtlasPropertyId = Shader.PropertyToID("_StitchTransitionAtlas");
    static readonly int InkDetailTileMetresPropertyId = Shader.PropertyToID("_StitchInkDetailTileMetres");
    static readonly int InkDetailParamsPropertyId = Shader.PropertyToID("_StitchInkDetailParams");
    static readonly int TransitionCellMetresPropertyId = Shader.PropertyToID("_StitchTransitionCellMetres");

    public enum ObjectHatchAnchor
    {
        // Lines fixed in the world: static things never change, a walking unit moves through them the same way along X or Z.
        World = 0,
        // Lines measured from each object's origin: they ride with a moving unit, but jump where its body parts meet.
        Object = 1,
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

    [Header("Ink look (ground only: ink detail, tile edges, hatched shadows)")]
    [Tooltip("Global: every Painted/Ground material, the zoomed-out stand-ins included. Needs all five textures below.")]
    public bool inkLookEnabled = false;

    [Tooltip("T_InkDetail_* sheets per ground layer slot (Base, R, G, B): R = ink lines, G = tone, B = scratches.")]
    public Texture2D inkDetailBase;
    public Texture2D inkDetailR;
    public Texture2D inkDetailG;
    public Texture2D inkDetailB;

    [Tooltip("T_TransitionAtlas: Don't Starve style edge tiles, 8 x 4 cells of 128 px.")]
    public Texture2D transitionAtlas;

    [Tooltip("World metres per detail sheet tile: Base, R, G, B.")]
    public Vector4 inkDetailTileMetres = new Vector4(6f, 6f, 6f, 6f);

    [Range(0f, 1f)] public float inkLineStrength = 1f;
    [Range(0f, 1f)] public float inkToneStrength = 0.6f;
    [Range(0f, 1f)] public float inkScratchStrength = 1f;

    [Range(0f, 1f)]
    [Tooltip("1 = each painted layer becomes its own flat average colour, so all detail comes from the ink.")]
    public float flattenPaintedTextures = 1f;

    [Range(1f, 8f)]
    public float transitionCellMetres = 4f;

    [Range(0f, 1f)]
    [Tooltip("Hatch lines in the ground's shadows. Characters and props are never hatched.")]
    public float hatchStrength = 1f;

    [Range(2f, 12f)]
    [Tooltip("Gap between hatch lines on screen. Constant at every zoom: zooming out thins the lines, it never blurs them.")]
    public float hatchSpacingPixels = 5f;

    [Range(0.5f, 2.5f)]
    public float hatchLineWidthPixels = 1f;

    [Range(0f, 1f)]
    [Tooltip("1 = shadows on the ground stop darkening: the hatch alone draws them, like the ink reference.")]
    public float shadowDrawnOnlyByHatch = 1f;

    [Range(0f, 1f)]
    [Tooltip("Hatch lines in the shade on buildings and props, and on characters where a cast shadow falls on them. 0 = ground only.")]
    public float objectHatchStrength = 1f;

    public ObjectHatchAnchor objectHatchAnchor = ObjectHatchAnchor.World;

    [Range(0f, 1f)]
    [Tooltip("1 = shade on objects stops darkening and only the hatch draws it; 0 = normal shade tone with hatch on top.")]
    public float objectShadowDrawnOnlyByHatch = 0.5f;

    [Tooltip("One pen for detail lines, edges and hatch.")]
    public Color inkColor = new Color(0.17f, 0.12f, 0.09f, 1f);

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
        Shader.DisableKeyword(InkLookKeyword);
        Shader.SetGlobalVector(InkObjectHatchParamsPropertyId, Vector4.zero);
    }

    bool InkLookReady()
    {
        return inkLookEnabled && inkDetailBase != null && inkDetailR != null && inkDetailG != null && inkDetailB != null && transitionAtlas != null;
    }

    void PushInkLookGlobals()
    {
        if (!InkLookReady())
        {
            Shader.DisableKeyword(InkLookKeyword);
            Shader.SetGlobalVector(InkObjectHatchParamsPropertyId, Vector4.zero);
            return;
        }
        Shader.SetGlobalTexture(InkDetailBasePropertyId, inkDetailBase);
        Shader.SetGlobalTexture(InkDetailRPropertyId, inkDetailR);
        Shader.SetGlobalTexture(InkDetailGPropertyId, inkDetailG);
        Shader.SetGlobalTexture(InkDetailBPropertyId, inkDetailB);
        Shader.SetGlobalTexture(TransitionAtlasPropertyId, transitionAtlas);
        Shader.SetGlobalVector(InkDetailTileMetresPropertyId, inkDetailTileMetres);
        Shader.SetGlobalVector(InkDetailParamsPropertyId, new Vector4(inkLineStrength, inkToneStrength, inkScratchStrength, flattenPaintedTextures));
        Shader.SetGlobalFloat(TransitionCellMetresPropertyId, transitionCellMetres);
        Shader.SetGlobalVector(InkColorPropertyId, new Vector4(inkColor.r, inkColor.g, inkColor.b, 1f));
        // Without hatching a lifted shadow would simply vanish, so the lift only applies while the hatch draws it.
        float shadowLift = hatchStrength > 0f ? shadowDrawnOnlyByHatch : 0f;
        Shader.SetGlobalVector(InkHatchParamsPropertyId, new Vector4(hatchStrength, hatchSpacingPixels, hatchLineWidthPixels, shadowLift));
        float objectShadowLift = objectHatchStrength > 0f ? objectShadowDrawnOnlyByHatch : 0f;
        Shader.SetGlobalVector(InkObjectHatchParamsPropertyId, new Vector4(objectHatchStrength, (float)objectHatchAnchor, objectShadowLift, 0f));
        Shader.EnableKeyword(InkLookKeyword);
    }

    internal static float AdvanceFlash(float currentFlash, float flashPerSecondDecay, float deltaTime)
    {
        return Mathf.Max(0f, currentFlash - flashPerSecondDecay * deltaTime);
    }
}
