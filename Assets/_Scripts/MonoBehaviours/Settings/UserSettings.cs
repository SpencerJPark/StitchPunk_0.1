using System;
using System.IO;
using UnityEngine;

// Per-machine options (audio, video, control rebinds). Deliberately NOT part of a save slot: loading an old save must not
// reset your volume or resolution. AudioManager reads the volumes from here every frame.
public static class UserSettings
{
    public static readonly string[] DisplayModeNames = { "Fullscreen", "Borderless Window", "Maximized Window", "Windowed" };
    public static readonly FullScreenMode[] DisplayModes =
    {
        FullScreenMode.ExclusiveFullScreen,
        FullScreenMode.FullScreenWindow,
        FullScreenMode.MaximizedWindow,
        FullScreenMode.Windowed,
    };
    public static readonly int[] FrameRateCaps = { -1, 30, 60, 90, 120, 144, 165, 240 };

    [Serializable]
    private sealed class UserSettingsData
    {
        public float masterVolume = 1f;
        public float musicVolume = 0.7f;
        public float sfxVolume = 0.9f;
        public float ambientVolume = 0.7f;

        // Video is only forced onto the machine once the player has touched it, so first launch keeps Unity's defaults.
        public bool videoCustomised;
        public int qualityLevel;
        public int displayModeIndex;
        public int resolutionWidth;
        public int resolutionHeight;
        public bool verticalSync;
        public int targetFrameRate = -1;

        public string bindingOverridesJson = "";
    }

    private const string FileName = "user_settings.json";

    private static UserSettingsData data;

    public static float MasterVolume { get { return Data.masterVolume; } set { Data.masterVolume = Mathf.Clamp01(value); } }
    public static float MusicVolume { get { return Data.musicVolume; } set { Data.musicVolume = Mathf.Clamp01(value); } }
    public static float SfxVolume { get { return Data.sfxVolume; } set { Data.sfxVolume = Mathf.Clamp01(value); } }
    public static float AmbientVolume { get { return Data.ambientVolume; } set { Data.ambientVolume = Mathf.Clamp01(value); } }

    public static int QualityLevel => Data.videoCustomised ? Data.qualityLevel : QualitySettings.GetQualityLevel();
    public static int DisplayModeIndex => Data.videoCustomised ? Data.displayModeIndex : IndexOfDisplayMode(Screen.fullScreenMode);
    public static bool VerticalSync => Data.videoCustomised ? Data.verticalSync : QualitySettings.vSyncCount > 0;
    public static int TargetFrameRate => Data.videoCustomised ? Data.targetFrameRate : Application.targetFrameRate;
    public static int ResolutionWidth => Data.videoCustomised ? Data.resolutionWidth : Screen.width;
    public static int ResolutionHeight => Data.videoCustomised ? Data.resolutionHeight : Screen.height;

    public static string BindingOverridesJson
    {
        get { return Data.bindingOverridesJson; }
        set { Data.bindingOverridesJson = value ?? ""; Save(); }
    }

    private static UserSettingsData Data
    {
        get
        {
            if (data == null)
                Load();
            return data;
        }
    }

    private static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticStateForDisabledDomainReload()
    {
        data = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ApplyAtStartup()
    {
        ApplyVideo();
    }

    public static void SetVideo(int qualityLevel, int displayModeIndex, int resolutionWidth, int resolutionHeight, bool verticalSync, int targetFrameRate)
    {
        Data.videoCustomised = true;
        Data.qualityLevel = qualityLevel;
        Data.displayModeIndex = displayModeIndex;
        Data.resolutionWidth = resolutionWidth;
        Data.resolutionHeight = resolutionHeight;
        Data.verticalSync = verticalSync;
        Data.targetFrameRate = targetFrameRate;
        ApplyVideo();
        Save();
    }

    public static void ResetAudio()
    {
        UserSettingsData defaults = new UserSettingsData();
        Data.masterVolume = defaults.masterVolume;
        Data.musicVolume = defaults.musicVolume;
        Data.sfxVolume = defaults.sfxVolume;
        Data.ambientVolume = defaults.ambientVolume;
        Save();
    }

    public static void Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonUtility.ToJson(Data, true));
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"UserSettings: could not write {FilePath}: {exception.Message}");
        }
    }

    private static void Load()
    {
        data = new UserSettingsData();
        try
        {
            if (File.Exists(FilePath))
                JsonUtility.FromJsonOverwrite(File.ReadAllText(FilePath), data);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"UserSettings: could not read {FilePath}, using defaults: {exception.Message}");
            data = new UserSettingsData();
        }
    }

    private static void ApplyVideo()
    {
        if (!Data.videoCustomised)
            return;

        QualitySettings.SetQualityLevel(Mathf.Clamp(Data.qualityLevel, 0, QualitySettings.names.Length - 1), true);
        QualitySettings.vSyncCount = Data.verticalSync ? 1 : 0;
        Application.targetFrameRate = Data.targetFrameRate;

        FullScreenMode displayMode = DisplayModes[Mathf.Clamp(Data.displayModeIndex, 0, DisplayModes.Length - 1)];
        if (Data.resolutionWidth > 0 && Data.resolutionHeight > 0)
            Screen.SetResolution(Data.resolutionWidth, Data.resolutionHeight, displayMode);
        else
            Screen.fullScreenMode = displayMode;
    }

    private static int IndexOfDisplayMode(FullScreenMode mode)
    {
        int index = Array.IndexOf(DisplayModes, mode);
        return index < 0 ? 0 : index;
    }
}
