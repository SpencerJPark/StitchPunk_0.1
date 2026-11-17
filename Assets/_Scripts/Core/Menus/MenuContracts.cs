using System;
using System.Collections.Generic;

// The seam between menu flow and menu presentation (SaveAndTitle SM-A4). Views (UI Toolkit now, Rive later)
// implement these; the controllers and BootFlow only ever talk to the interfaces. Lives in Core because both
// UI (views) and MonoBehaviours (BootFlow) reference Core, while UI already references MonoBehaviours.

public enum TitleScreen { Splash, Main, LoadList, ConfirmNewGame, ConfirmDelete, Options, Credits }

public enum TitleCommandKind
{
    AnyButton,
    Continue,
    NewGame,
    ConfirmNewGame,
    OpenLoadList,
    LoadSave,
    DeleteSave,
    ConfirmDelete,
    Options,
    Credits,
    Quit,
    Back,
}

public readonly struct TitleCommand
{
    public readonly TitleCommandKind Kind;
    public readonly string SaveId;

    public TitleCommand(TitleCommandKind kind, string saveId = null)
    {
        Kind = kind;
        SaveId = saveId;
    }
}

public sealed class TitleMenuModel
{
    public IReadOnlyList<SaveHeaderV2> SavesNewestFirst = Array.Empty<SaveHeaderV2>();
    public bool ContinuePreloadFinished;
    public byte[] NewestThumbnailPng;
    public string PendingDeleteSaveId;
    public bool ShowQuitButton = true;
    // Saves whose moment layer no longer matches the component layout: the list shows "Older version: units reset".
    public HashSet<string> UnitsResetSaveIds = new HashSet<string>();

    public bool HasAnySave => SavesNewestFirst != null && SavesNewestFirst.Count > 0;
    public bool IsContinueOffered => HasAnySave;
    public SaveHeaderV2 NewestSave => HasAnySave ? SavesNewestFirst[0] : null;
}

public interface ITitleMenuView
{
    event Action<TitleCommand> CommandIssued;
    void Show(TitleScreen screen, TitleMenuModel model);
    void Hide(float fadeSeconds);
}

// What the title flow asks the game to do; BootFlow implements it.
public interface ITitleMenuActions
{
    void ContinueNewest();
    void StartNewGame();
    void LoadSave(string saveId);
    void DeleteSave(string saveId);   // must refresh the model's save list before returning
    void OpenOptions();
    void Quit();
}

public interface ITitleBackdrop
{
    void Enter();
    void Exit(float fadeSeconds);
}

public enum PauseScreen { Main, SaveList, LoadList, ConfirmOverwrite, ConfirmDelete, ConfirmReturnToTitle, ConfirmQuit, Options }

public enum PauseCommandKind
{
    Resume,
    OpenSaveList,
    OpenLoadList,
    SaveNew,
    SaveOverwrite,
    LoadSave,
    DeleteSave,
    Confirm,
    Options,
    ReturnToTitle,
    Quit,
    Back,
}

public readonly struct PauseCommand
{
    public readonly PauseCommandKind Kind;
    public readonly string SaveId;

    public PauseCommand(PauseCommandKind kind, string saveId = null)
    {
        Kind = kind;
        SaveId = saveId;
    }
}

public sealed class PauseMenuModel
{
    public IReadOnlyList<SaveHeaderV2> SavesNewestFirst = Array.Empty<SaveHeaderV2>();
    public HashSet<string> UnitsResetSaveIds = new HashSet<string>();
    public string PendingSaveId;
    public bool SavingBlockedByCutscene;
    public double SecondsSinceLastSave;
    public bool ShowQuitButton = true;
}

public interface IPauseMenuView
{
    event Action<PauseCommand> CommandIssued;
    void Show(PauseScreen screen, PauseMenuModel model);
    void Hide();
}

public interface IPauseMenuActions
{
    void Resume();
    void SaveNew();
    void SaveOverwrite(string saveId);
    void LoadSave(string saveId);
    void DeleteSave(string saveId);   // must refresh the model's save list before returning
    void OpenOptions();
    void ReturnToTitle();
    void Quit();
}
