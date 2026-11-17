using Unity.Collections;
using Unity.Entities;

public struct GameDataTag : IComponentData { }

public struct SaveRequest : IComponentData, IEnableableComponent
{
    public int slot;                              // legacy, unused since SaveAndTitle; kept for old call sites
    public int kind;                              // (int)SaveKind
    public FixedString64Bytes overwriteSaveId;    // Manual only: empty = new save folder
}

public struct LoadRequest : IComponentData, IEnableableComponent
{
    public int slot;                              // legacy, unused
    public FixedString64Bytes saveId;             // empty = load the newest save of any kind
}

// While this singleton exists, every GameSceneSystemGroup (except streaming + save), physics and the toolkit groups are held.
public struct SimulationHold : IComponentData { }

// While present, TerrainStreamingSystem streams around this position instead of the TerrainNavFocus entity, so the
// saved location is already loaded behind the title when Continue commits (SaveAndTitle SM-D10).
public struct TerrainStreamingFocusOverride : IComponentData
{
    public Unity.Mathematics.float3 position;
}

public struct AutoSaveTimer : IComponentData
{
    public float elapsedSeconds;
    public float intervalSeconds;
}

public struct PlayTimeTracker : IComponentData, IPersist
{
    public double totalSeconds; // double for long-session precision
}

public struct GameSettings : IComponentData, IPersist
{
    public int animationFrameRate;

    // Audio bus volumes (0–1), applied to the AudioMixer by AudioManager. Persist via IPersist.
    public float masterVolume;
    public float musicVolume;
    public float sfxVolume;
    public float ambientVolume;
}

// SM-D8: autosaves wait until no unit has been in combat for this long, so an autosave never lands mid-swing.
public struct AutoSaveCombatQuiet : IComponentData { public float secondsSinceCombat; public float requiredQuietSeconds; }   // singleton, created at runtime
public struct AutoSavePending : IComponentData { }   // singleton tag: an autosave is due, waiting for quiet
public struct TravelAutoSaveState : IComponentData { public Unity.Mathematics.float3 lastSavedPlayerPosition; public bool hasLastSavedPosition; public float thresholdMetres; }  // singleton, created at runtime
