using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Owns the in-game pause overlay and pause state. PlayerInputManager decides when to call Show/Hide.
/// With a pause view assigned it drives a PauseMenuController; without one it toggles the legacy menuRoot panel.
/// </summary>
public class GameMenuManager : MonoBehaviour, IPauseMenuActions
{
    public static GameMenuManager Instance { get; private set; }

    // The settings menu view (UI assembly) subscribes; true = opened, false = closed.
    public static event Action<bool> VisibilityChanged;

    [Header("Wiring")]
    [Tooltip("The panel/root GameObject to toggle on/off. Legacy fallback when no pause view is set. Starts hidden.")]
    [SerializeField] private GameObject menuRoot;

    [Tooltip("The ECS save/load seam. Auto-found on this GameObject if left empty.")]
    [SerializeField] private SaveLoadBridge saveLoadBridge;

    [Tooltip("A MonoBehaviour implementing IPauseMenuView (the UI Toolkit pause view). Empty = legacy menuRoot.")]
    [SerializeField] private MonoBehaviour pauseMenuViewBehaviour;

    [Tooltip("Raised when the pause menu asks for the options screen.")]
    [SerializeField] private UnityEvent onOpenOptions;

    public bool IsOpen { get; private set; }

    private PauseMenuModel pauseMenuModel;
    private PauseMenuController pauseMenuController;

    public bool HasPauseMenuView => pauseMenuController != null;

    private void Awake()
    {
        Instance = this;

        if (saveLoadBridge == null)
            saveLoadBridge = GetComponent<SaveLoadBridge>();

        // The view lives in the UI assembly, which this assembly cannot reference, so the shared prefab finds it by interface.
        if (pauseMenuViewBehaviour == null)
        {
            MonoBehaviour[] sceneBehaviours = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (MonoBehaviour sceneBehaviour in sceneBehaviours)
            {
                if (sceneBehaviour is IPauseMenuView)
                {
                    pauseMenuViewBehaviour = sceneBehaviour;
                    break;
                }
            }
        }

        if (pauseMenuViewBehaviour is IPauseMenuView pauseMenuView)
        {
            pauseMenuModel = new PauseMenuModel();
            pauseMenuController = new PauseMenuController(pauseMenuView, this, pauseMenuModel);
        }

        if (menuRoot != null)
            menuRoot.SetActive(false);
    }

    private void OnEnable()
    {
        SaveNotifications.SaveFinished += HandleSaveFinished;
    }

    private void OnDisable()
    {
        SaveNotifications.SaveFinished -= HandleSaveFinished;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        pauseMenuController?.Dispose();

        // Safety: never leave the game frozen if this is torn down while open.
        if (IsOpen)
            Time.timeScale = 1f;
    }

    /// <summary>Shows the menu and pauses the game. Called by PlayerInputManager.</summary>
    public void Show()
    {
        if (pauseMenuController != null)
        {
            RefreshPauseMenuModel();
            pauseMenuController.Open();
        }
        else if (menuRoot != null)
        {
            menuRoot.SetActive(true);
        }

        Time.timeScale = 0f;
        IsOpen = true;
        VisibilityChanged?.Invoke(true);
    }

    /// <summary>Hides the menu and unpauses. Called by PlayerInputManager.</summary>
    public void Hide()
    {
        pauseMenuController?.Close();

        if (menuRoot != null)
            menuRoot.SetActive(false);

        Time.timeScale = 1f;
        IsOpen = false;
        VisibilityChanged?.Invoke(false);
    }

    private void RefreshPauseMenuModel()
    {
        RefreshPauseMenuSaveList();
        pauseMenuModel.SavingBlockedByCutscene = saveLoadBridge != null && saveLoadBridge.IsCutsceneActive();

        long lastSaveUnixSeconds = SaveNotifications.LastSaveUnixSeconds;
        pauseMenuModel.SecondsSinceLastSave = lastSaveUnixSeconds > 0
            ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() - lastSaveUnixSeconds
            : Time.realtimeSinceStartupAsDouble;
    }

    private void RefreshPauseMenuSaveList()
    {
        pauseMenuModel.SavesNewestFirst = SaveSlotStore.ListHeadersNewestFirst();
        pauseMenuModel.UnitsResetSaveIds = MomentFiles.FindSavesWithStaleMomentLayer(pauseMenuModel.SavesNewestFirst);
    }

    private void HandleSaveFinished(string saveId, bool success)
    {
        if (!IsOpen || pauseMenuController == null)
            return;

        RefreshPauseMenuModel();
        pauseMenuController.RefreshCurrentScreen();
    }

    // --- IPauseMenuActions ---

    public void Resume()
    {
        if (PlayerInputManager.Instance != null)
            PlayerInputManager.Instance.CloseMenu();
    }

    public void SaveNew()
    {
        if (saveLoadBridge != null)
            saveLoadBridge.RequestSave(SaveKind.Manual);
    }

    public void SaveOverwrite(string saveId)
    {
        if (saveLoadBridge != null)
            saveLoadBridge.RequestSave(SaveKind.Manual, saveId);
    }

    public void LoadSave(string saveId)
    {
        if (saveLoadBridge != null)
            saveLoadBridge.RequestLoad(saveId);

        Resume();
    }

    public void DeleteSave(string saveId)
    {
        SaveSlotStore.DeleteSave(saveId);

        if (pauseMenuModel != null)
            RefreshPauseMenuSaveList();
    }

    public void OpenOptions()
    {
        onOpenOptions?.Invoke();
    }

    public void ReturnToTitle()
    {
        Time.timeScale = 1f;
        BootFlow.ReturnToTitle();
    }

    public void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
        Application.Quit();
    }

    /// <summary>Closes the menu through the input manager so the action map is restored.</summary>
    public void OnCloseButton()
    {
        Resume();
    }
}
