using UnityEngine;

public enum TickMode
    {
        GameTime,   // uses Time.time (affected by timeScale)
        RealTime,   // uses Time.unscaledTime (ignores timeScale)
        Frames      // ticks every N frames (N=1 means once-per-frame)
    }

public abstract class ScriptableSystem : ScriptableObject
{
    [Header("Scheduling")]
    [SerializeField] private TickMode tickMode = TickMode.GameTime;

    [Tooltip("Meaning depends on TickMode:\n" +
             "- GameTime / RealTime: seconds per tick\n" +
             "- Frames: frames per tick (1 = once per frame)")]
    [SerializeField] private float tickRate = 1f;

    [Tooltip("Optional initial delay before the first tick. (seconds for time modes; frames for Frames)")]
    [SerializeField] private float initialDelay = 0f;

    [Tooltip("If true, do catch-up ticks when multiple intervals elapse (capped by MaxCatchUpPerUpdate).")]
    [SerializeField] private bool allowCatchUp = true;

    [Tooltip("Max catch-up ticks allowed in a single Update.")]
    [SerializeField] private int maxCatchUpPerUpdate = 3;

    // Exposed read-only properties
    public TickMode TickMode   => tickMode;
    public float    TickRate   => tickRate;
    public float    InitialDelay => initialDelay;
    public bool     AllowCatchUp => allowCatchUp;
    public int      MaxCatchUpPerUpdate => maxCatchUpPerUpdate;

    /// Called once by the scheduler on Start (if enabled there).
    public virtual void Initialize() { }

    /// Called by the scheduler according to your TickMode and TickRate.
    public virtual void Tick() { }

    /// Optional cleanup on scheduler destroy/disable.
    public virtual void Shutdown() { }
}

