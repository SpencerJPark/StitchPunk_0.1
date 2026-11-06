#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class TerrainPlatformProfileAssets
{
    public const string PlatformTierPreferenceKey = "TerrainEditor.PlatformTier";
    private const string ProfilesFolder = "Assets/Terrain/Profiles";
    private const string ResourcesFolder = "Assets/Resources";

    private static string AssetPathForTier(TerrainPlatformTier tier)
    {
        return ProfilesFolder + "/Platform_" + tier + ".asset";
    }

    private static void EnsureFolder(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
        {
            return;
        }
        int separatorIndex = folderPath.LastIndexOf('/');
        string parentFolder = folderPath.Substring(0, separatorIndex);
        EnsureFolder(parentFolder);
        AssetDatabase.CreateFolder(parentFolder, folderPath.Substring(separatorIndex + 1));
    }

    [MenuItem("Stitch Punk/Terrain/Create Platform Profiles")]
    public static void CreatePlatformProfiles()
    {
        EnsureFolder(ProfilesFolder);
        foreach (TerrainPlatformTier tier in System.Enum.GetValues(typeof(TerrainPlatformTier)))
        {
            string assetPath = AssetPathForTier(tier);
            if (AssetDatabase.LoadAssetAtPath<TerrainPlatformProfile>(assetPath) != null)
            {
                continue;
            }
            AssetDatabase.CreateAsset(TerrainPlatformProfile.CreatePreset(tier), assetPath);
        }

        EnsureFolder(ResourcesFolder);
        string activePath = ResourcesFolder + "/" + TerrainPlatformProfile.ActiveResourceName + ".asset";
        if (AssetDatabase.LoadAssetAtPath<TerrainPlatformProfile>(activePath) == null)
        {
            AssetDatabase.CreateAsset(TerrainPlatformProfile.CreatePreset(TerrainPlatformTier.PC), activePath);
        }
        AssetDatabase.SaveAssets();
    }

    public static TerrainPlatformProfile LoadEditorPreview()
    {
        int tierIndex = EditorPrefs.GetInt(PlatformTierPreferenceKey, (int)TerrainPlatformTier.PC);
        TerrainPlatformTier tier = System.Enum.IsDefined(typeof(TerrainPlatformTier), tierIndex)
            ? (TerrainPlatformTier)tierIndex
            : TerrainPlatformTier.PC;
        TerrainPlatformProfile loaded = AssetDatabase.LoadAssetAtPath<TerrainPlatformProfile>(AssetPathForTier(tier));
        return loaded != null ? loaded : TerrainPlatformProfile.CreatePreset(tier);
    }
}
#endif
