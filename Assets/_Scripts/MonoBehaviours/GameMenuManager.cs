using UnityEngine;

/// <summary>
/// Owns the in-game menu overlay (a UGUI Canvas panel) and the pause state. It does NOT decide
/// when to open/close — that flows through <see cref="PlayerInputManager"/> so the menu lives in
/// the controller system alongside every other input. PlayerInputManager calls Show/Hide; the
/// menu's own buttons call the OnX hooks below.
///
/// v1 = test menu: Save / Load / Close only. Slot is fixed (manual slot 1) for now.
/// </summary>
public class GameMenuManager : MonoBehaviour
{
    public static GameMenuManager Instance { get; private set; }

    [Header("Wiring")]
    [Tooltip("The panel/root GameObject to toggle on/off. Starts hidden.")]
    [SerializeField] private GameObject menuRoot;

    [Tooltip("The ECS save/load seam. Auto-found on this GameObject if left empty.")]
    [SerializeField] private SaveLoadBridge saveLoadBridge;

    [Tooltip("Save slot the menu writes to / reads from. 0 = autosave, 1–3 = manual.")]
    [SerializeField] private int slot = 1;

    public bool IsOpen { get; private set; }

    private void Awake()
    {
        Instance = this;

        if (saveLoadBridge == null)
            saveLoadBridge = GetComponent<SaveLoadBridge>();

        if (menuRoot != null)
            menuRoot.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        // Safety: never leave the game frozen if this is torn down while open.
        if (IsOpen)
            Time.timeScale = 1f;
    }

    /// <summary>Shows the panel and pauses the game. Called by PlayerInputManager.</summary>
    public void Show()
    {
        if (menuRoot != null)
            menuRoot.SetActive(true);

        Time.timeScale = 0f;
        IsOpen = true;
    }

    /// <summary>Hides the panel and unpauses. Called by PlayerInputManager.</summary>
    public void Hide()
    {
        if (menuRoot != null)
            menuRoot.SetActive(false);

        Time.timeScale = 1f;
        IsOpen = false;
    }

    // --- Button hooks (wire these to the UGUI Button OnClick events) ---

    public void OnSaveButton()
    {
        if (saveLoadBridge != null)
            saveLoadBridge.RequestSave(slot);
    }

    public void OnLoadButton()
    {
        if (saveLoadBridge != null)
            saveLoadBridge.RequestLoad(slot);
    }

    /// <summary>Closes the menu through the input manager so the action map is restored.</summary>
    public void OnCloseButton()
    {
        if (PlayerInputManager.Instance != null)
            PlayerInputManager.Instance.CloseMenu();
    }
}
