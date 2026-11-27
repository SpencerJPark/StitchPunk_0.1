using UnityEngine;
using UnityEngine.Serialization;

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
    static readonly int InkDetailBasePropertyId = Shader.PropertyToID("_StitchInkDetailBaseTex");
    static readonly int InkDetailRPropertyId = Shader.PropertyToID("_StitchInkDetailRTex");
    static readonly int InkDetailGPropertyId = Shader.PropertyToID("_StitchInkDetailGTex");
    static readonly int InkDetailBPropertyId = Shader.PropertyToID("_StitchInkDetailBTex");
    static readonly int TransitionAtlasPropertyId = Shader.PropertyToID("_StitchTransitionAtlas");
    static readonly int InkDetailTileMetresPropertyId = Shader.PropertyToID("_StitchInkDetailTileMetres");
    static readonly int InkDetailParamsPropertyId = Shader.PropertyToID("_StitchInkDetailParams");
    static readonly int TransitionCellMetresPropertyId = Shader.PropertyToID("_StitchTransitionCellMetres");
    const string GroundEdgeTilesKeyword = "_GROUND_EDGE_TILES";
    static readonly int GroundEdgeTileAtlasGrassPropertyId = Shader.PropertyToID("_GroundEdgeTileAtlasR");
    static readonly int GroundEdgeTileAtlasForestPropertyId = Shader.PropertyToID("_GroundEdgeTileAtlasG");
    static readonly int GroundEdgeTileAtlasStonePropertyId = Shader.PropertyToID("_GroundEdgeTileAtlasB");
    static readonly int GroundEdgeTileCellMetresPropertyId = Shader.PropertyToID("_GroundEdgeTileCellMetres");
    static readonly int TerraceCliffEdgesPropertyId = Shader.PropertyToID("_TerraceCliffEdges");
    static readonly int TerraceRimRockPropertyId = Shader.PropertyToID("_TerraceRimRock");
    static readonly int TerraceFootRockPropertyId = Shader.PropertyToID("_TerraceFootRock");
    static readonly int TerraceCliffEdgesReadyPropertyId = Shader.PropertyToID("_TerraceCliffEdgesReady");

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

    [Header("Ink look (ground only: ink detail, flat colour, tile edges)")]
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

    [Tooltip("One pen for detail lines and edges.")]
    public Color inkColor = new Color(0.17f, 0.12f, 0.09f, 1f);

    [Header("Edge tiles")]
    public bool edgeTilesEnabled = true;

    // One 16-case atlas per splat layer (8 x 4 cells of 128 px, R coverage, G rim, B ink), packed by Tools/terrain_edge_mock/export_transition_atlas.py.
    [Tooltip("Splat R layer: Grass drawn over Dirt.")]
    [FormerlySerializedAs("edgeTileAtlas")] public Texture2D grassEdgeTileAtlas;
    [Tooltip("Splat G layer: Forest drawn over what is below it.")]
    public Texture2D forestEdgeTileAtlas;
    [Tooltip("Splat B layer: Stone drawn over what is below it.")]
    public Texture2D stoneEdgeTileAtlas;

    [Tooltip("Rim slices (Dirt, Grass, Forest, Stone) then Foot slices; packed by Stitch Punk/Terrain/Edge Tiles/Pack Cliff Edge Sets.")]
    public Texture2DArray terraceCliffEdgeArray;
    public Color rimRockColour = new Color(0.72f, 0.62f, 0.50f);
    public Color footRockColour = new Color(0.33f, 0.27f, 0.22f);

    [Min(0.5f)] public float edgeTileCellMetres = 2f;

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
        PushEdgeTileGlobals();
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
        Shader.DisableKeyword(GroundEdgeTilesKeyword);
    }

    void PushEdgeTileGlobals()
    {
        if (terraceCliffEdgeArray != null)
        {
            Shader.SetGlobalTexture(TerraceCliffEdgesPropertyId, terraceCliffEdgeArray);
        }
        // SetGlobalColor does not gamma-convert, and these are picked in sRGB like every inspector colour.
        Shader.SetGlobalColor(TerraceRimRockPropertyId, rimRockColour.linear);
        Shader.SetGlobalColor(TerraceFootRockPropertyId, footRockColour.linear);
        // NC-D1 retired the ground-shader cliff-edge pass (the freeform wall path draws the rim); the pass is deleted in NC-D18.
        Shader.SetGlobalFloat(TerraceCliffEdgesReadyPropertyId, 0f);
        if (!edgeTilesEnabled || grassEdgeTileAtlas == null)
        {
            Shader.DisableKeyword(GroundEdgeTilesKeyword);
            return;
        }
        // An unset global texture samples grey (coverage 0.5 everywhere), so a missing layer borrows the grass set.
        Shader.SetGlobalTexture(GroundEdgeTileAtlasGrassPropertyId, grassEdgeTileAtlas);
        Shader.SetGlobalTexture(GroundEdgeTileAtlasForestPropertyId, forestEdgeTileAtlas != null ? forestEdgeTileAtlas : grassEdgeTileAtlas);
        Shader.SetGlobalTexture(GroundEdgeTileAtlasStonePropertyId, stoneEdgeTileAtlas != null ? stoneEdgeTileAtlas : grassEdgeTileAtlas);
        Shader.SetGlobalFloat(GroundEdgeTileCellMetresPropertyId, edgeTileCellMetres);
        Shader.EnableKeyword(GroundEdgeTilesKeyword);
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
        Shader.EnableKeyword(InkLookKeyword);
    }

    internal static float AdvanceFlash(float currentFlash, float flashPerSecondDecay, float deltaTime)
    {
        return Mathf.Max(0f, currentFlash - flashPerSecondDecay * deltaTime);
    }
}
