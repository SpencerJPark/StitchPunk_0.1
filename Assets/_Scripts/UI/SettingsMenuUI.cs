using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

// Placeholder view until the Rive menu exists: every option reads and writes UserSettings / InputRebinding / SaveLoadBridge,
// so the Rive artboard only has to replace this class. Opened in-game by GameMenuManager (pause) or from the main menu.
public class SettingsMenuUI : MonoBehaviour
{
    public static SettingsMenuUI Instance { get; private set; }

    private enum Page { Game, Audio, Video, Controls }

    private static readonly Color DimColor = new Color(0f, 0f, 0f, 0.75f);
    private static readonly Color PanelColor = new Color(0.12f, 0.12f, 0.13f, 1f);
    private static readonly Color SidebarColor = new Color(0.09f, 0.09f, 0.1f, 1f);
    private static readonly Color TileColor = new Color(0.2f, 0.2f, 0.21f, 1f);
    private static readonly Color SelectedTileColor = new Color(0.22f, 0.45f, 0.43f, 1f);
    private static readonly Color DangerTileColor = new Color(0.5f, 0.17f, 0.17f, 1f);
    private static readonly Color CreamColor = new Color(0.94f, 0.9f, 0.78f);
    private static readonly Color MutedColor = new Color(0.62f, 0.6f, 0.54f);

    private const int AutosaveSlot = 0;
    private const int FirstManualSlot = 1;
    private const int LastManualSlot = 3;
    private const int MainMenuSceneBuildIndex = 0;
    private const long ConfirmWindowMilliseconds = 3000;

    [SerializeField] private UIDocument menuDocument;
    [Tooltip("Main menu only: the game scene registers its own copy, so rebinding there needs this asset.")]
    [SerializeField] private InputActionAsset fallbackInputActions;

    private VisualElement overlay;
    private VisualElement sidebar;
    private VisualElement contentHost;
    private Button footerButton;
    private readonly Dictionary<Page, Button> tabButtons = new Dictionary<Page, Button>();

    private SaveLoadBridge saveLoadBridge;
    private bool openedInGame;
    private string activeBindingGroup = InputRebinding.KeyboardMouseGroup;

    private void OnEnable()
    {
        Instance = this;
        GameMenuManager.VisibilityChanged += OnGameMenuVisibilityChanged;
    }

    private void OnDisable()
    {
        GameMenuManager.VisibilityChanged -= OnGameMenuVisibilityChanged;
        if (Instance == this)
            Instance = null;

        InputRebinding.CancelActiveRebind();
        if (overlay != null && overlay.parent != null)
            overlay.RemoveFromHierarchy();
        overlay = null;
    }

    public void OpenFromMainMenu()
    {
        Open(false);
    }

    public void Close()
    {
        if (openedInGame && GameMenuManager.Instance != null)
            GameMenuManager.Instance.OnCloseButton();
        else
            HideOverlay();
    }

    private void OnGameMenuVisibilityChanged(bool isOpen)
    {
        if (isOpen)
            Open(true);
        else
            HideOverlay();
    }

    private void Open(bool inGame)
    {
        if (!EnsureBuilt())
            return;

        openedInGame = inGame;
        if (inGame && saveLoadBridge == null)
        {
            saveLoadBridge = FindFirstObjectByType<SaveLoadBridge>();
            if (saveLoadBridge == null)
                saveLoadBridge = gameObject.AddComponent<SaveLoadBridge>();
        }

        if (InputRebinding.Asset == null && fallbackInputActions != null)
            InputRebinding.Register(fallbackInputActions);

        footerButton.text = inGame ? "Resume" : "Back";
        tabButtons[Page.Game].style.display = inGame ? DisplayStyle.Flex : DisplayStyle.None;

        overlay.style.display = DisplayStyle.Flex;
        UnityEngine.Cursor.visible = true;
        UnityEngine.Cursor.lockState = CursorLockMode.None;
        SelectPage(inGame ? Page.Game : Page.Audio);
    }

    private void HideOverlay()
    {
        InputRebinding.CancelActiveRebind();
        UserSettings.Save();
        if (overlay != null)
            overlay.style.display = DisplayStyle.None;
    }

    private bool EnsureBuilt()
    {
        if (overlay != null)
            return true;
        if (menuDocument == null)
        {
            Debug.LogWarning("SettingsMenuUI: no UIDocument assigned.");
            return false;
        }

        overlay = new VisualElement();
        overlay.style.position = Position.Absolute;
        overlay.style.left = 0f;
        overlay.style.right = 0f;
        overlay.style.top = 0f;
        overlay.style.bottom = 0f;
        overlay.style.backgroundColor = DimColor;
        overlay.style.alignItems = Align.Center;
        overlay.style.justifyContent = Justify.Center;
        overlay.style.display = DisplayStyle.None;

        VisualElement panel = new VisualElement();
        panel.style.flexDirection = FlexDirection.Row;
        panel.style.width = Length.Percent(80f);
        panel.style.maxWidth = 1200f;
        panel.style.height = Length.Percent(80f);
        panel.style.maxHeight = 760f;
        panel.style.backgroundColor = PanelColor;
        overlay.Add(panel);

        sidebar = new VisualElement();
        sidebar.style.width = 240f;
        sidebar.style.paddingTop = 24f;
        sidebar.style.paddingBottom = 24f;
        sidebar.style.paddingLeft = 16f;
        sidebar.style.paddingRight = 16f;
        sidebar.style.backgroundColor = SidebarColor;
        panel.Add(sidebar);

        sidebar.Add(CreateLabel("SETTINGS", 28f, CreamColor));
        sidebar.Add(CreateSpacer(16f));
        AddTab(Page.Game, "Game");
        AddTab(Page.Audio, "Audio");
        AddTab(Page.Video, "Video");
        AddTab(Page.Controls, "Controls");

        VisualElement flexibleGap = new VisualElement();
        flexibleGap.style.flexGrow = 1f;
        sidebar.Add(flexibleGap);

        footerButton = CreateButton("Resume", Close, TileColor, 22f);
        footerButton.style.height = 52f;
        sidebar.Add(footerButton);

        contentHost = new VisualElement();
        contentHost.style.flexGrow = 1f;
        contentHost.style.flexShrink = 1f;
        contentHost.style.paddingTop = 24f;
        contentHost.style.paddingBottom = 24f;
        contentHost.style.paddingLeft = 32f;
        contentHost.style.paddingRight = 32f;
        panel.Add(contentHost);

        menuDocument.rootVisualElement.Add(overlay);
        return true;
    }

    private void AddTab(Page page, string title)
    {
        Button tabButton = CreateButton(title, () => SelectPage(page), TileColor, 22f);
        tabButton.style.height = 48f;
        tabButton.style.marginBottom = 8f;
        tabButtons[page] = tabButton;
        sidebar.Add(tabButton);
    }

    private void SelectPage(Page page)
    {
        InputRebinding.CancelActiveRebind();
        UserSettings.Save();

        foreach (KeyValuePair<Page, Button> tab in tabButtons)
            tab.Value.style.backgroundColor = tab.Key == page ? SelectedTileColor : TileColor;

        contentHost.Clear();
        switch (page)
        {
            case Page.Game: BuildGamePage(); break;
            case Page.Audio: BuildAudioPage(); break;
            case Page.Video: BuildVideoPage(); break;
            case Page.Controls: BuildControlsPage(); break;
        }
    }

    #region Game page

    private void BuildGamePage()
    {
        contentHost.Add(CreateLabel("Paused", 32f, CreamColor));
        contentHost.Add(CreateSpacer(16f));

        Label statusLabel = CreateLabel("", 18f, MutedColor);

        List<Label> slotLabels = new List<Label>();
        List<Button> loadButtons = new List<Button>();
        Action refreshSlots = () =>
        {
            for (int slotRow = 0; slotRow < slotLabels.Count; slotRow++)
            {
                int slot = slotRow == 0 ? AutosaveSlot : FirstManualSlot + slotRow - 1;
                slotLabels[slotRow].text = DescribeSlot(slot);
                loadButtons[slotRow].SetEnabled(File.Exists(GetSlotPath(slot)));
            }
        };

        for (int slot = AutosaveSlot; slot <= LastManualSlot; slot++)
        {
            int rowSlot = slot;
            VisualElement row = CreateRow();
            Label slotLabel = CreateLabel("", 20f, CreamColor);
            slotLabel.style.flexGrow = 1f;
            row.Add(slotLabel);
            slotLabels.Add(slotLabel);

            if (rowSlot != AutosaveSlot)
            {
                Button saveButton = CreateButton("Save", () =>
                {
                    saveLoadBridge.RequestSave(rowSlot);
                    statusLabel.text = "Saving to slot " + rowSlot + "...";
                    overlay.schedule.Execute(() =>
                    {
                        statusLabel.text = "Saved to slot " + rowSlot + ".";
                        refreshSlots();
                    }).StartingIn(700);
                }, TileColor, 18f);
                saveButton.style.width = 110f;
                row.Add(saveButton);
            }

            Button loadButton = CreateButton("Load", () =>
            {
                statusLabel.text = "Loading slot " + rowSlot + "...";
                saveLoadBridge.RequestLoad(rowSlot);
            }, TileColor, 18f);
            loadButton.style.width = 110f;
            loadButton.style.marginLeft = 8f;
            row.Add(loadButton);
            loadButtons.Add(loadButton);

            contentHost.Add(row);
        }

        refreshSlots();
        contentHost.Add(statusLabel);
        contentHost.Add(CreateSpacer(24f));

        contentHost.Add(CreateConfirmButton("Quit to Main Menu", QuitToMainMenu));
        contentHost.Add(CreateSpacer(8f));
        contentHost.Add(CreateConfirmButton("Quit to Desktop", QuitToDesktop));
    }

    private static string DescribeSlot(int slot)
    {
        string slotName = slot == AutosaveSlot ? "Autosave" : "Slot " + slot;
        string path = GetSlotPath(slot);
        if (!File.Exists(path))
            return slotName + "  -  Empty";
        return slotName + "  -  " + File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm");
    }

    // Mirrors SavePaths (Systems assembly, not referenced from UI) — keep the file naming in sync.
    private static string GetSlotPath(int slot)
    {
        return Path.Combine(Application.persistentDataPath, "save_slot_" + slot + ".json");
    }

    private static void QuitToMainMenu()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(MainMenuSceneBuildIndex);
    }

    private static void QuitToDesktop()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    #endregion

    #region Audio page

    private void BuildAudioPage()
    {
        contentHost.Add(CreateLabel("Audio", 32f, CreamColor));
        contentHost.Add(CreateSpacer(16f));

        contentHost.Add(CreateVolumeRow("Master", UserSettings.MasterVolume, volume => UserSettings.MasterVolume = volume));
        contentHost.Add(CreateVolumeRow("Music", UserSettings.MusicVolume, volume => UserSettings.MusicVolume = volume));
        contentHost.Add(CreateVolumeRow("Effects", UserSettings.SfxVolume, volume => UserSettings.SfxVolume = volume));
        contentHost.Add(CreateVolumeRow("Ambient", UserSettings.AmbientVolume, volume => UserSettings.AmbientVolume = volume));
        contentHost.Add(CreateSpacer(16f));

        contentHost.Add(CreateButton("Reset to Defaults", () =>
        {
            UserSettings.ResetAudio();
            SelectPage(Page.Audio);
        }, TileColor, 20f));
    }

    private VisualElement CreateVolumeRow(string title, float initialVolume, Action<float> setVolume)
    {
        VisualElement row = CreateRow();
        Label titleLabel = CreateLabel(title, 20f, CreamColor);
        titleLabel.style.width = 160f;
        row.Add(titleLabel);

        Slider slider = new Slider(0f, 1f);
        slider.value = initialVolume;
        slider.style.flexGrow = 1f;
        row.Add(slider);

        Label percentLabel = CreateLabel(Mathf.RoundToInt(initialVolume * 100f) + "%", 20f, CreamColor);
        percentLabel.style.width = 64f;
        percentLabel.style.unityTextAlign = TextAnchor.MiddleRight;
        row.Add(percentLabel);

        slider.RegisterValueChangedCallback(changeEvent =>
        {
            setVolume(changeEvent.newValue);
            percentLabel.text = Mathf.RoundToInt(changeEvent.newValue * 100f) + "%";
        });
        // Written to disk on release, page change and close — not on every drag step.
        slider.RegisterCallback<PointerUpEvent>(pointerUpEvent => UserSettings.Save(), TrickleDown.TrickleDown);
        return row;
    }

    #endregion

    #region Video page

    private void BuildVideoPage()
    {
        contentHost.Add(CreateLabel("Video", 32f, CreamColor));
        contentHost.Add(CreateSpacer(16f));

        List<string> qualityNames = new List<string>(QualitySettings.names);
        List<Vector2Int> resolutions = BuildResolutionList();
        List<string> resolutionNames = new List<string>();
        for (int resolutionIndex = 0; resolutionIndex < resolutions.Count; resolutionIndex++)
            resolutionNames.Add(resolutions[resolutionIndex].x + " x " + resolutions[resolutionIndex].y);
        List<string> frameCapNames = new List<string>();
        for (int capIndex = 0; capIndex < UserSettings.FrameRateCaps.Length; capIndex++)
        {
            int cap = UserSettings.FrameRateCaps[capIndex];
            frameCapNames.Add(cap < 0 ? "Unlimited" : cap + " FPS");
        }

        int currentResolutionIndex = resolutions.FindIndex(resolution =>
            resolution.x == UserSettings.ResolutionWidth && resolution.y == UserSettings.ResolutionHeight);
        int currentFrameCapIndex = Mathf.Max(0, Array.IndexOf(UserSettings.FrameRateCaps, UserSettings.TargetFrameRate));

        DropdownField displayModeDropdown = new DropdownField(new List<string>(UserSettings.DisplayModeNames), UserSettings.DisplayModeIndex);
        DropdownField resolutionDropdown = new DropdownField(resolutionNames, Mathf.Max(0, currentResolutionIndex));
        DropdownField qualityDropdown = new DropdownField(qualityNames, Mathf.Clamp(UserSettings.QualityLevel, 0, qualityNames.Count - 1));
        DropdownField frameCapDropdown = new DropdownField(frameCapNames, currentFrameCapIndex);
        Toggle verticalSyncToggle = new Toggle();
        verticalSyncToggle.value = UserSettings.VerticalSync;

        Action applyVideo = () =>
        {
            Vector2Int chosenResolution = resolutions.Count > 0
                ? resolutions[Mathf.Clamp(resolutionDropdown.index, 0, resolutions.Count - 1)]
                : new Vector2Int(Screen.width, Screen.height);
            UserSettings.SetVideo(
                qualityDropdown.index,
                displayModeDropdown.index,
                chosenResolution.x,
                chosenResolution.y,
                verticalSyncToggle.value,
                UserSettings.FrameRateCaps[frameCapDropdown.index]);
        };

        displayModeDropdown.RegisterValueChangedCallback(changeEvent => applyVideo());
        resolutionDropdown.RegisterValueChangedCallback(changeEvent => applyVideo());
        qualityDropdown.RegisterValueChangedCallback(changeEvent => applyVideo());
        frameCapDropdown.RegisterValueChangedCallback(changeEvent => applyVideo());
        verticalSyncToggle.RegisterValueChangedCallback(changeEvent => applyVideo());

        contentHost.Add(CreateLabeledRow("Display Mode", displayModeDropdown));
        contentHost.Add(CreateLabeledRow("Resolution", resolutionDropdown));
        contentHost.Add(CreateLabeledRow("Quality", qualityDropdown));
        contentHost.Add(CreateLabeledRow("VSync", verticalSyncToggle));
        contentHost.Add(CreateLabeledRow("Frame Rate Limit", frameCapDropdown));
        contentHost.Add(CreateLabel("Resolution has no effect in the Editor's Game view.", 16f, MutedColor));
    }

    // One entry per width x height, highest-first, ignoring the many refresh-rate duplicates Screen.resolutions returns.
    private static List<Vector2Int> BuildResolutionList()
    {
        List<Vector2Int> resolutions = new List<Vector2Int>();
        Resolution[] screenResolutions = Screen.resolutions;
        for (int index = 0; index < screenResolutions.Length; index++)
        {
            Vector2Int candidate = new Vector2Int(screenResolutions[index].width, screenResolutions[index].height);
            if (!resolutions.Contains(candidate))
                resolutions.Add(candidate);
        }
        return resolutions;
    }

    #endregion

    #region Controls page

    private void BuildControlsPage()
    {
        contentHost.Add(CreateLabel("Controls", 32f, CreamColor));
        contentHost.Add(CreateSpacer(8f));

        if (InputRebinding.Asset == null)
        {
            contentHost.Add(CreateLabel("No input actions are loaded here, so controls cannot be rebound.", 20f, MutedColor));
            return;
        }

        VisualElement schemeRow = CreateRow();
        Button keyboardButton = CreateButton("Keyboard & Mouse", () => ChangeBindingGroup(InputRebinding.KeyboardMouseGroup),
            activeBindingGroup == InputRebinding.KeyboardMouseGroup ? SelectedTileColor : TileColor, 18f);
        Button gamepadButton = CreateButton("Gamepad", () => ChangeBindingGroup(InputRebinding.GamepadGroup),
            activeBindingGroup == InputRebinding.GamepadGroup ? SelectedTileColor : TileColor, 18f);
        keyboardButton.style.width = 200f;
        gamepadButton.style.width = 200f;
        gamepadButton.style.marginLeft = 8f;
        schemeRow.Add(keyboardButton);
        schemeRow.Add(gamepadButton);
        contentHost.Add(schemeRow);

        ScrollView bindingList = new ScrollView(ScrollViewMode.Vertical);
        bindingList.style.flexGrow = 1f;
        bindingList.style.marginTop = 8f;
        bindingList.style.marginBottom = 8f;
        contentHost.Add(bindingList);

        string lastMapName = null;
        foreach (RebindEntry entry in InputRebinding.GetEntries(activeBindingGroup))
        {
            if (entry.mapName != lastMapName)
            {
                lastMapName = entry.mapName;
                Label mapHeader = CreateLabel(InputRebinding.SplitWords(entry.mapName), 22f, SelectedTileColor);
                mapHeader.style.marginTop = 12f;
                bindingList.Add(mapHeader);
            }
            bindingList.Add(CreateRebindRow(entry));
        }

        contentHost.Add(CreateButton("Reset All Controls", () =>
        {
            InputRebinding.ResetAll();
            SelectPage(Page.Controls);
        }, TileColor, 20f));
    }

    private void ChangeBindingGroup(string bindingGroup)
    {
        activeBindingGroup = bindingGroup;
        SelectPage(Page.Controls);
    }

    private VisualElement CreateRebindRow(RebindEntry entry)
    {
        VisualElement row = CreateRow();
        Label actionLabel = CreateLabel(entry.label, 20f, CreamColor);
        actionLabel.style.flexGrow = 1f;
        row.Add(actionLabel);

        Button bindingButton = CreateButton(InputRebinding.GetDisplayString(entry), null, TileColor, 18f);
        bindingButton.style.width = 220f;
        bindingButton.clicked += () =>
        {
            if (InputRebinding.IsRebinding)
                return;

            bindingButton.text = activeBindingGroup == InputRebinding.GamepadGroup ? "Press a button..." : "Press a key...";
            bindingButton.style.backgroundColor = SelectedTileColor;
            InputRebinding.StartRebind(entry, activeBindingGroup, () =>
            {
                if (bindingButton.panel == null)
                    return;
                bindingButton.text = InputRebinding.GetDisplayString(entry);
                bindingButton.style.backgroundColor = TileColor;
            });
        };
        row.Add(bindingButton);

        Button resetButton = CreateButton("Reset", () =>
        {
            InputRebinding.ResetEntry(entry);
            bindingButton.text = InputRebinding.GetDisplayString(entry);
        }, TileColor, 16f);
        resetButton.style.width = 80f;
        resetButton.style.marginLeft = 8f;
        row.Add(resetButton);
        return row;
    }

    #endregion

    #region Element helpers

    private static Label CreateLabel(string text, float fontSize, Color color)
    {
        Label label = new Label(text);
        label.style.fontSize = fontSize;
        label.style.color = color;
        label.style.unityTextAlign = TextAnchor.MiddleLeft;
        return label;
    }

    private static VisualElement CreateSpacer(float height)
    {
        VisualElement spacer = new VisualElement();
        spacer.style.height = height;
        spacer.style.flexShrink = 0f;
        return spacer;
    }

    private static VisualElement CreateRow()
    {
        VisualElement row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.minHeight = 48f;
        row.style.flexShrink = 0f;
        return row;
    }

    private static VisualElement CreateLabeledRow(string title, VisualElement control)
    {
        VisualElement row = CreateRow();
        Label titleLabel = CreateLabel(title, 20f, CreamColor);
        titleLabel.style.width = 220f;
        row.Add(titleLabel);
        control.style.flexGrow = 1f;
        row.Add(control);
        return row;
    }

    private static Button CreateButton(string text, Action onClick, Color backgroundColor, float fontSize)
    {
        Button button = new Button();
        if (onClick != null)
            button.clicked += onClick;
        button.text = text;
        button.style.fontSize = fontSize;
        button.style.color = CreamColor;
        button.style.backgroundColor = backgroundColor;
        button.style.height = 40f;
        button.style.borderTopWidth = 0f;
        button.style.borderBottomWidth = 0f;
        button.style.borderLeftWidth = 0f;
        button.style.borderRightWidth = 0f;
        return button;
    }

    // First click arms it (red, "Click again"), second click within a few seconds does it.
    private Button CreateConfirmButton(string text, Action onConfirmed)
    {
        Button button = CreateButton(text, null, TileColor, 20f);
        bool armed = false;
        button.clicked += () =>
        {
            if (armed)
            {
                onConfirmed();
                return;
            }

            armed = true;
            button.text = text + " - click again to confirm";
            button.style.backgroundColor = DangerTileColor;
            button.schedule.Execute(() =>
            {
                armed = false;
                button.text = text;
                button.style.backgroundColor = TileColor;
            }).StartingIn(ConfirmWindowMilliseconds);
        };
        return button;
    }

    #endregion
}
