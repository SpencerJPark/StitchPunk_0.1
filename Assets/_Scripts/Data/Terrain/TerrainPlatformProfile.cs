using UnityEngine;

public enum TerrainPlatformTier
{
    PC,
    SteamDeck,
    Switch
}

[CreateAssetMenu(menuName = "Stitch Punk/Terrain/Platform Profile")]
public class TerrainPlatformProfile : ScriptableObject
{
    public const string ActiveResourceName = "TerrainPlatformProfile_Active";

    [Tooltip("Hardware class this budget set targets.")]
    public TerrainPlatformTier tier = TerrainPlatformTier.PC;

    [Min(1)]
    [Tooltip("Resident terrain memory allowed with the nine-tile ring loaded.")]
    public int maxResidentTerrainMegabytesForNineTiles = 256;

    [Min(1)]
    [Tooltip("Decals plus props allowed on one tile.")]
    public int maxDecalsPlusPropsPerTile = 1000;

    [Min(0.1f)]
    [Tooltip("Disk size allowed for one baked wilderness tile.")]
    public float maxDiskMegabytesPerWildernessTile = 1.5f;

    [Min(1)]
    [Tooltip("Memory allowed for the strategic layer textures.")]
    public int maxStrategicLayerMegabytes = 64;

    [Min(64)]
    [Tooltip("Edge length of the strategic layer textures.")]
    public int strategicTextureSizePixels = 2048;

    [Min(1)]
    [Tooltip("Furthest ring of tiles drawn at full visual detail.")]
    public int maxVisualLoadRadiusTiles = 3;

    [Min(64)]
    [Tooltip("Splat mask resolution for a new tile.")]
    public int defaultMaskResolution = 512;

    [Tooltip("Real depth of field in the strategic view.")]
    public bool useDepthOfField = true;

    [Tooltip("Cheap screen-Y gradient blur instead of depth of field.")]
    public bool useGradientBlurFallback;

    [Min(0.1f)]
    [Tooltip("Main-thread time one tile load may spend per frame.")]
    public float maxTileLoadMainThreadMilliseconds = 5f;

    [Min(1)]
    [Tooltip("Tile loads started in one frame.")]
    public int maxTileLoadsPerFrame = 2;

    [Min(1)]
    [Tooltip("Ribbon road vertices allowed on one tile.")]
    public int maxRibbonVerticesPerTile = 4000;

    [Min(1f)]
    [Tooltip("Frame time this platform aims for.")]
    public float targetFrameMilliseconds = 8.3f;

    private static TerrainPlatformProfile cachedDefault;

    public static TerrainPlatformProfile Active
    {
        get
        {
            TerrainPlatformProfile loaded = Resources.Load<TerrainPlatformProfile>(ActiveResourceName);
            if (loaded != null)
            {
                return loaded;
            }

            if (cachedDefault == null)
            {
                cachedDefault = CreatePreset(TerrainPlatformTier.PC);
                cachedDefault.hideFlags = HideFlags.HideAndDontSave;
            }
            return cachedDefault;
        }
    }

    public static TerrainPlatformProfile CreatePreset(TerrainPlatformTier tier)
    {
        TerrainPlatformProfile profile = CreateInstance<TerrainPlatformProfile>();
        profile.tier = tier;
        switch (tier)
        {
            case TerrainPlatformTier.SteamDeck:
                profile.maxResidentTerrainMegabytesForNineTiles = 150;
                profile.maxDecalsPlusPropsPerTile = 600;
                profile.maxDiskMegabytesPerWildernessTile = 1.2f;
                profile.maxStrategicLayerMegabytes = 40;
                profile.strategicTextureSizePixels = 1024;
                profile.maxVisualLoadRadiusTiles = 2;
                profile.defaultMaskResolution = 256;
                profile.useDepthOfField = true;
                profile.useGradientBlurFallback = false;
                profile.maxTileLoadsPerFrame = 1;
                profile.maxRibbonVerticesPerTile = 4000;
                profile.targetFrameMilliseconds = 16.6f;
                break;
            case TerrainPlatformTier.Switch:
                profile.maxResidentTerrainMegabytesForNineTiles = 96;
                profile.maxDecalsPlusPropsPerTile = 400;
                profile.maxDiskMegabytesPerWildernessTile = 1.0f;
                profile.maxStrategicLayerMegabytes = 24;
                profile.strategicTextureSizePixels = 1024;
                profile.maxVisualLoadRadiusTiles = 1;
                profile.defaultMaskResolution = 256;
                profile.useDepthOfField = false;
                profile.useGradientBlurFallback = true;
                profile.maxTileLoadsPerFrame = 1;
                profile.maxRibbonVerticesPerTile = 3000;
                profile.targetFrameMilliseconds = 33.3f;
                break;
            default:
                break;
        }
        profile.maxTileLoadMainThreadMilliseconds = 5f;
        return profile;
    }

    public static TerrainPlatformTier DetectTierForCurrentPlatform()
    {
#if UNITY_SWITCH
        return TerrainPlatformTier.Switch;
#else
        bool isSteamDeckModel = SystemInfo.deviceModel.Contains("Steam Deck");
        bool isDesktopHandheld = SystemInfo.deviceType == DeviceType.Handheld
            && Application.platform != RuntimePlatform.Android
            && Application.platform != RuntimePlatform.IPhonePlayer;
        return (isSteamDeckModel || isDesktopHandheld) ? TerrainPlatformTier.SteamDeck : TerrainPlatformTier.PC;
#endif
    }
}
