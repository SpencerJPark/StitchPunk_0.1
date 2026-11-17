using System;

// Save/load progress, raised on the main thread by PersistentSaveSystem / PersistentLoadSystem. Lives in Core so the
// indicator (UI), the thumbnail writer and BootFlow (MonoBehaviours) can listen without referencing Systems.
public static class SaveNotifications
{
    public static event Action<string, SaveKind> SaveStarted;     // saveId, kind: capture done, disk write running
    public static event Action<string, bool> SaveFinished;        // saveId, success
    public static event Action<string> SaveBlocked;               // human-readable reason, e.g. "Can't save during a cutscene"
    public static event Action<string> LoadFinished;              // saveId
    public static event Action<string> LoadFailed;                // saveId (may be empty): nothing was applied

    // True from capture until the background disk write ends; saves and loads are refused meanwhile (SM-D8).
    public static bool IsSaveInProgress { get; private set; }
    public static long LastSaveUnixSeconds { get; private set; }

    public static void RaiseSaveStarted(string saveId, SaveKind kind)
    {
        IsSaveInProgress = true;
        // A throwing listener must not abort the save: the write task starts after this call and clears the flag.
        try { SaveStarted?.Invoke(saveId, kind); }
        catch (Exception listenerException) { UnityEngine.Debug.LogException(listenerException); }
    }

    public static void RaiseSaveFinished(string saveId, bool success)
    {
        IsSaveInProgress = false;
        if (success)
            LastSaveUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        SaveFinished?.Invoke(saveId, success);
    }

    public static void RaiseSaveBlocked(string reason) => SaveBlocked?.Invoke(reason);

    public static void RaiseLoadFailed(string saveId) => LoadFailed?.Invoke(saveId);

    public static void RaiseLoadFinished(string saveId)
    {
        LastSaveUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        LoadFinished?.Invoke(saveId);
    }
}
