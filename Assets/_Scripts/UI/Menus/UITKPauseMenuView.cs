using System;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class UITKPauseMenuView : MonoBehaviour, IPauseMenuView
{
    [SerializeField] private UIDocument document;

    public event Action<PauseCommand> CommandIssued;

    private VisualElement pauseRoot;
    private VisualElement mainScreen;
    private VisualElement saveListScreen;
    private VisualElement loadListScreen;
    private VisualElement confirmScreen;
    private VisualElement optionsScreen;
    private SaveListView saveListView;
    private SaveListView loadListView;
    private Label confirmMessageLabel;
    private Label saveBlockedCaption;
    private Button saveButton;
    private Button quitButton;

    private bool isBuilt;

    private void OnEnable()
    {
        BuildIfNeeded();
        if (pauseRoot != null)
            pauseRoot.style.display = DisplayStyle.None;
    }

    private void OnDisable()
    {
        if (saveListView != null)
        {
            saveListView.SaveChosen -= OnSaveListSaveChosen;
            saveListView.DeleteRequested -= OnDeleteRequested;
            saveListView.NewSaveRequested -= OnNewSaveRequested;
            saveListView.Release();
            saveListView = null;
        }

        if (loadListView != null)
        {
            loadListView.SaveChosen -= OnLoadListSaveChosen;
            loadListView.DeleteRequested -= OnDeleteRequested;
            loadListView.Release();
            loadListView = null;
        }

        isBuilt = false;
    }

    private void OnSaveListSaveChosen(string saveId)
    {
        Issue(new PauseCommand(PauseCommandKind.SaveOverwrite, saveId));
    }

    private void OnLoadListSaveChosen(string saveId)
    {
        Issue(new PauseCommand(PauseCommandKind.LoadSave, saveId));
    }

    private void OnDeleteRequested(string saveId)
    {
        Issue(new PauseCommand(PauseCommandKind.DeleteSave, saveId));
    }

    private void OnNewSaveRequested()
    {
        Issue(new PauseCommand(PauseCommandKind.SaveNew));
    }

    public void Show(PauseScreen screen, PauseMenuModel model)
    {
        BuildIfNeeded();
        if (pauseRoot == null)
            return;

        pauseRoot.style.display = DisplayStyle.Flex;
        SetOnlyScreenVisible(ScreenContainerFor(screen));

        switch (screen)
        {
            case PauseScreen.Main:
                saveButton.SetEnabled(!model.SavingBlockedByCutscene);
                saveBlockedCaption.style.display = model.SavingBlockedByCutscene ? DisplayStyle.Flex : DisplayStyle.None;
                quitButton.style.display = model.ShowQuitButton ? DisplayStyle.Flex : DisplayStyle.None;
                break;
            case PauseScreen.SaveList:
                saveListView.Fill(model.SavesNewestFirst, model.UnitsResetSaveIds, SaveListMode.Save);
                break;
            case PauseScreen.LoadList:
                loadListView.Fill(model.SavesNewestFirst, model.UnitsResetSaveIds, SaveListMode.Load);
                break;
            case PauseScreen.ConfirmOverwrite:
                confirmMessageLabel.text = "Overwrite this save?";
                break;
            case PauseScreen.ConfirmDelete:
                confirmMessageLabel.text = "Delete this save? This can't be undone.";
                break;
            case PauseScreen.ConfirmReturnToTitle:
                confirmMessageLabel.text = $"Return to title? Progress since your last save ({(int)model.SecondsSinceLastSave}s ago) will be lost.";
                break;
            case PauseScreen.ConfirmQuit:
                confirmMessageLabel.text = "Quit the game? Unsaved progress will be lost.";
                break;
        }

        VisualElement activeScreen = ScreenContainerFor(screen);
        if (activeScreen == confirmScreen)
            activeScreen.schedule.Execute(() => confirmScreen.Q<Button>("pause-confirm-no").Focus());
        else
            activeScreen.schedule.Execute(() => FocusFirstEnabledButton(activeScreen));
    }

    public void Hide()
    {
        if (pauseRoot != null)
            pauseRoot.style.display = DisplayStyle.None;
    }

    private void BuildIfNeeded()
    {
        if (isBuilt || document == null)
            return;

        VisualElement root = document.rootVisualElement;
        pauseRoot = root.Q<VisualElement>("pause-root");
        if (pauseRoot == null)
            return;

        mainScreen = root.Q<VisualElement>("pause-main");
        saveListScreen = root.Q<VisualElement>("pause-save-list");
        loadListScreen = root.Q<VisualElement>("pause-load-list");
        confirmScreen = root.Q<VisualElement>("pause-confirm");
        optionsScreen = root.Q<VisualElement>("pause-options-screen");
        ScrollView saveRowsScrollView = root.Q<ScrollView>("pause-save-rows");
        ScrollView loadRowsScrollView = root.Q<ScrollView>("pause-load-rows");
        saveListView = new SaveListView(saveRowsScrollView.contentContainer);
        loadListView = new SaveListView(loadRowsScrollView.contentContainer);
        saveListView.SaveChosen += OnSaveListSaveChosen;
        saveListView.DeleteRequested += OnDeleteRequested;
        saveListView.NewSaveRequested += OnNewSaveRequested;
        loadListView.SaveChosen += OnLoadListSaveChosen;
        loadListView.DeleteRequested += OnDeleteRequested;
        confirmMessageLabel = root.Q<Label>("pause-confirm-message");
        saveBlockedCaption = root.Q<Label>("pause-save-blocked");
        saveButton = root.Q<Button>("pause-save");
        quitButton = root.Q<Button>("pause-quit");

        BindButton(root, "pause-resume", PauseCommandKind.Resume);
        BindButton(root, "pause-save", PauseCommandKind.OpenSaveList);
        BindButton(root, "pause-load", PauseCommandKind.OpenLoadList);
        BindButton(root, "pause-options", PauseCommandKind.Options);
        BindButton(root, "pause-return-to-title", PauseCommandKind.ReturnToTitle);
        BindButton(root, "pause-quit", PauseCommandKind.Quit);
        // SaveListView supplies its own New save row in Save mode.
        Button orphanedNewSaveButton = root.Q<Button>("pause-new-save");
        if (orphanedNewSaveButton != null)
            orphanedNewSaveButton.style.display = DisplayStyle.None;
        BindButton(root, "pause-save-list-back", PauseCommandKind.Back);
        BindButton(root, "pause-load-list-back", PauseCommandKind.Back);
        BindButton(root, "pause-confirm-yes", PauseCommandKind.Confirm);
        BindButton(root, "pause-confirm-no", PauseCommandKind.Back);
        BindButton(root, "pause-options-back", PauseCommandKind.Back);

        // Bubble phase: a focused button handles Submit itself, Cancel is only ours.
        pauseRoot.RegisterCallback<NavigationCancelEvent>(OnNavigationCancel);
        isBuilt = true;
    }

    private void BindButton(VisualElement root, string buttonName, PauseCommandKind kind)
    {
        Button button = root.Q<Button>(buttonName);
        if (button != null)
            button.clicked += () => Issue(new PauseCommand(kind));
    }

    private void OnNavigationCancel(NavigationCancelEvent navigationCancelEvent)
    {
        Issue(new PauseCommand(PauseCommandKind.Back));
        navigationCancelEvent.StopPropagation();
    }

    private void Issue(PauseCommand command)
    {
        CommandIssued?.Invoke(command);
    }

    private VisualElement ScreenContainerFor(PauseScreen screen)
    {
        switch (screen)
        {
            case PauseScreen.Main: return mainScreen;
            case PauseScreen.SaveList: return saveListScreen;
            case PauseScreen.LoadList: return loadListScreen;
            case PauseScreen.Options: return optionsScreen;
            default: return confirmScreen;
        }
    }

    private void SetOnlyScreenVisible(VisualElement visibleScreen)
    {
        VisualElement[] allScreens = { mainScreen, saveListScreen, loadListScreen, confirmScreen, optionsScreen };
        foreach (VisualElement candidate in allScreens)
            candidate.style.display = candidate == visibleScreen ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private static void FocusFirstEnabledButton(VisualElement screen)
    {
        foreach (Button button in screen.Query<Button>().ToList())
        {
            if (button.enabledInHierarchy && button.resolvedStyle.display != DisplayStyle.None)
            {
                button.Focus();
                return;
            }
        }
    }
}
