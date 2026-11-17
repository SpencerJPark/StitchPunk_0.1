using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class UITKTitleMenuView : MonoBehaviour, ITitleMenuView
{
    private const string SplashPulseDimClass = "splash-pulse-dim";
    private const long SplashPulseIntervalMilliseconds = 800;

    [SerializeField] private UIDocument document;

    public event Action<TitleCommand> CommandIssued;

    private struct ButtonBinding
    {
        public Button Target;
        public Action Handler;
    }

    private readonly List<ButtonBinding> fixedButtonBindings = new List<ButtonBinding>();
    private readonly Dictionary<TitleScreen, VisualElement> screenContainers = new Dictionary<TitleScreen, VisualElement>();

    private VisualElement root;
    private TitleScreen currentScreen = TitleScreen.Splash;
    private bool isHidden;
    private int fadeGeneration;
    private IVisualElementScheduledItem splashPulseItem;

    private Button continueButton;
    private Button quitButton;
    private VisualElement continueCard;
    private VisualElement continueThumbnail;
    private Label continueLocationLabel;
    private Label continuePlayTimeLabel;
    private Label continueDateLabel;
    private Label continueSpinnerLabel;
    private ScrollView saveListScrollView;
    private Label emptyListLabel;
    private SaveListView saveListView;
    private Texture2D thumbnailTexture;

    private void OnEnable()
    {
        if (document == null)
            return;

        root = document.rootVisualElement;

        screenContainers.Clear();
        RegisterScreen(TitleScreen.Splash, "screen-splash");
        RegisterScreen(TitleScreen.Main, "screen-main");
        RegisterScreen(TitleScreen.LoadList, "screen-load-list");
        RegisterScreen(TitleScreen.ConfirmNewGame, "screen-confirm-new-game");
        RegisterScreen(TitleScreen.ConfirmDelete, "screen-confirm-delete");
        RegisterScreen(TitleScreen.Options, "screen-options");
        RegisterScreen(TitleScreen.Credits, "screen-credits");

        continueButton = root.Q<Button>("btn-continue");
        quitButton = root.Q<Button>("btn-quit");
        continueCard = root.Q<VisualElement>("continue-card");
        continueThumbnail = root.Q<VisualElement>("continue-thumbnail");
        continueLocationLabel = root.Q<Label>("continue-location");
        continuePlayTimeLabel = root.Q<Label>("continue-playtime");
        continueDateLabel = root.Q<Label>("continue-date");
        continueSpinnerLabel = root.Q<Label>("continue-spinner");
        saveListScrollView = root.Q<ScrollView>("save-list");
        emptyListLabel = root.Q<Label>("save-list-empty");
        if (saveListScrollView != null)
        {
            saveListView = new SaveListView(saveListScrollView.contentContainer);
            saveListView.SaveChosen += OnSaveChosen;
            saveListView.DeleteRequested += OnDeleteRequested;
        }

        fixedButtonBindings.Clear();
        BindFixedButton("btn-continue", TitleCommandKind.Continue);
        BindFixedButton("btn-new-game", TitleCommandKind.NewGame);
        BindFixedButton("btn-load-game", TitleCommandKind.OpenLoadList);
        BindFixedButton("btn-options", TitleCommandKind.Options);
        BindFixedButton("btn-credits", TitleCommandKind.Credits);
        BindFixedButton("btn-quit", TitleCommandKind.Quit);
        BindFixedButton("btn-new-yes", TitleCommandKind.ConfirmNewGame);
        BindFixedButton("btn-new-no", TitleCommandKind.Back);
        BindFixedButton("btn-delete-yes", TitleCommandKind.ConfirmDelete);
        BindFixedButton("btn-delete-no", TitleCommandKind.Back);
        BindFixedButton("btn-load-back", TitleCommandKind.Back);
        BindFixedButton("btn-options-back", TitleCommandKind.Back);
        BindFixedButton("btn-credits-back", TitleCommandKind.Back);

        // Trickle so a key press counts even when a child element has focus.
        root.RegisterCallback<KeyDownEvent>(OnSplashKeyDown, TrickleDown.TrickleDown);
        root.RegisterCallback<NavigationSubmitEvent>(OnSplashNavigationSubmit, TrickleDown.TrickleDown);
        root.RegisterCallback<PointerDownEvent>(OnSplashPointerDown, TrickleDown.TrickleDown);
        root.RegisterCallback<NavigationCancelEvent>(OnNavigationCancel);

        VisualElement splashLabel = root.Q<VisualElement>("splash-prompt");
        if (splashLabel != null)
        {
            splashPulseItem = splashLabel.schedule.Execute(() => splashLabel.ToggleInClassList(SplashPulseDimClass))
                .Every(SplashPulseIntervalMilliseconds);
        }
    }

    private void OnDisable()
    {
        fadeGeneration++;
        splashPulseItem?.Pause();
        splashPulseItem = null;

        for (int index = 0; index < fixedButtonBindings.Count; index++)
        {
            ButtonBinding binding = fixedButtonBindings[index];
            if (binding.Target != null)
                binding.Target.clicked -= binding.Handler;
        }
        fixedButtonBindings.Clear();

        if (root != null)
        {
            root.UnregisterCallback<KeyDownEvent>(OnSplashKeyDown, TrickleDown.TrickleDown);
            root.UnregisterCallback<NavigationSubmitEvent>(OnSplashNavigationSubmit, TrickleDown.TrickleDown);
            root.UnregisterCallback<PointerDownEvent>(OnSplashPointerDown, TrickleDown.TrickleDown);
            root.UnregisterCallback<NavigationCancelEvent>(OnNavigationCancel);
        }

        DestroyThumbnailTexture();
        if (saveListView != null)
        {
            saveListView.SaveChosen -= OnSaveChosen;
            saveListView.DeleteRequested -= OnDeleteRequested;
            saveListView.Release();
            saveListView = null;
        }
    }

    public void Show(TitleScreen screen, TitleMenuModel model)
    {
        if (root == null)
            return;

        fadeGeneration++;
        isHidden = false;
        root.style.display = DisplayStyle.Flex;
        root.style.opacity = 1f;
        root.pickingMode = PickingMode.Position;

        currentScreen = screen;
        foreach (KeyValuePair<TitleScreen, VisualElement> pair in screenContainers)
            pair.Value.style.display = pair.Key == screen ? DisplayStyle.Flex : DisplayStyle.None;

        switch (screen)
        {
            case TitleScreen.Main:
                FillMain(model);
                break;
            case TitleScreen.LoadList:
                FillLoadList(model);
                break;
        }

        VisualElement activeContainer = screenContainers[screen];
        // Confirm screens default to the non-destructive button.
        string safeButtonName = screen == TitleScreen.ConfirmNewGame ? "btn-new-no"
            : screen == TitleScreen.ConfirmDelete ? "btn-delete-no" : null;
        activeContainer.schedule.Execute(() =>
        {
            Button safeButton = safeButtonName != null ? activeContainer.Q<Button>(safeButtonName) : null;
            if (safeButton != null)
                safeButton.Focus();
            else
                FocusFirstVisibleFocusable(activeContainer);
        });
    }

    public void Hide(float fadeSeconds)
    {
        if (root == null || isHidden)
            return;

        isHidden = true;
        fadeGeneration++;
        int generation = fadeGeneration;
        root.pickingMode = PickingMode.Ignore;

        if (fadeSeconds <= 0f)
        {
            FinishHide();
            return;
        }

        float elapsedSeconds = 0f;
        root.schedule.Execute(() =>
        {
            if (generation != fadeGeneration)
                return;

            elapsedSeconds += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsedSeconds / fadeSeconds);
            root.style.opacity = 1f - progress;
            if (progress >= 1f)
                FinishHide();
        }).Every(16).Until(() => generation != fadeGeneration || !isHidden || root.style.display == DisplayStyle.None);
    }

    private void FinishHide()
    {
        root.style.opacity = 0f;
        root.style.display = DisplayStyle.None;
        root.pickingMode = PickingMode.Ignore;
        DestroyThumbnailTexture();
    }

    private void RegisterScreen(TitleScreen screen, string elementName)
    {
        VisualElement container = root.Q<VisualElement>(elementName);
        if (container != null)
            screenContainers[screen] = container;
    }

    private void BindFixedButton(string buttonName, TitleCommandKind kind)
    {
        Button button = root.Q<Button>(buttonName);
        if (button == null)
            return;

        Action handler = () => CommandIssued?.Invoke(new TitleCommand(kind));
        button.clicked += handler;
        fixedButtonBindings.Add(new ButtonBinding { Target = button, Handler = handler });
    }

    private void OnSplashKeyDown(KeyDownEvent keyEvent)
    {
        if (currentScreen == TitleScreen.Splash && !isHidden)
            CommandIssued?.Invoke(new TitleCommand(TitleCommandKind.AnyButton));
    }

    private void OnSplashNavigationSubmit(NavigationSubmitEvent submitEvent)
    {
        if (currentScreen == TitleScreen.Splash && !isHidden)
            CommandIssued?.Invoke(new TitleCommand(TitleCommandKind.AnyButton));
    }

    private void OnSplashPointerDown(PointerDownEvent pointerEvent)
    {
        if (currentScreen == TitleScreen.Splash && !isHidden)
            CommandIssued?.Invoke(new TitleCommand(TitleCommandKind.AnyButton));
    }

    private void OnNavigationCancel(NavigationCancelEvent cancelEvent)
    {
        if (currentScreen == TitleScreen.Splash || isHidden)
            return;

        CommandIssued?.Invoke(new TitleCommand(TitleCommandKind.Back));
    }

    private void FillMain(TitleMenuModel model)
    {
        if (continueButton != null)
            continueButton.style.display = model.IsContinueOffered ? DisplayStyle.Flex : DisplayStyle.None;
        if (quitButton != null)
            quitButton.style.display = model.ShowQuitButton ? DisplayStyle.Flex : DisplayStyle.None;

        bool showCard = model.IsContinueOffered;
        if (continueCard != null)
            continueCard.style.display = showCard ? DisplayStyle.Flex : DisplayStyle.None;
        if (!showCard)
            return;

        SaveHeaderV2 newest = model.NewestSave;
        SetLabelText(continueLocationLabel, string.IsNullOrEmpty(newest.locationLabel) ? "Unknown location" : newest.locationLabel);
        SetLabelText(continuePlayTimeLabel, FormatPlayTime(newest.totalPlaySeconds));
        SetLabelText(continueDateLabel, FormatLocalDateTime(newest.timestampUnix));
        if (continueSpinnerLabel != null)
            continueSpinnerLabel.style.display = model.ContinuePreloadFinished ? DisplayStyle.None : DisplayStyle.Flex;

        DestroyThumbnailTexture();
        if (continueThumbnail == null)
            return;

        if (model.NewestThumbnailPng != null && model.NewestThumbnailPng.Length > 0)
        {
            thumbnailTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (thumbnailTexture.LoadImage(model.NewestThumbnailPng))
            {
                continueThumbnail.style.backgroundImage = new StyleBackground(thumbnailTexture);
            }
            else
            {
                DestroyThumbnailTexture();
            }
        }
    }

    private void FillLoadList(TitleMenuModel model)
    {
        if (saveListScrollView == null)
            return;

        bool hasSaves = model.HasAnySave;
        if (emptyListLabel != null)
            emptyListLabel.style.display = hasSaves ? DisplayStyle.None : DisplayStyle.Flex;
        if (!hasSaves)
        {
            saveListView.Fill(null, null, SaveListMode.Load);
            return;
        }

        saveListView.Fill(model.SavesNewestFirst, model.UnitsResetSaveIds, SaveListMode.Load);
    }

    private void OnSaveChosen(string saveId)
    {
        CommandIssued?.Invoke(new TitleCommand(TitleCommandKind.LoadSave, saveId));
    }

    private void OnDeleteRequested(string saveId)
    {
        CommandIssued?.Invoke(new TitleCommand(TitleCommandKind.DeleteSave, saveId));
    }

    private static void FocusFirstVisibleFocusable(VisualElement container)
    {
        List<Button> buttons = container.Query<Button>().ToList();
        for (int index = 0; index < buttons.Count; index++)
        {
            Button button = buttons[index];
            if (button.style.display != DisplayStyle.None && !button.ClassListContains("save-row-delete"))
            {
                button.Focus();
                return;
            }
        }

        // Splash has no button; its container takes focus so gamepad submit reaches the root.
        if (container.focusable)
            container.Focus();
    }

    private static void SetLabelText(Label label, string text)
    {
        if (label != null)
            label.text = text;
    }

    private static string FormatPlayTime(double totalPlaySeconds)
    {
        int totalMinutes = (int)(Math.Max(0.0, totalPlaySeconds) / 60.0);
        return (totalMinutes / 60) + ":" + (totalMinutes % 60).ToString("00");
    }

    private static string FormatLocalDateTime(long timestampUnix)
    {
        return DateTimeOffset.FromUnixTimeSeconds(timestampUnix).ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    }

    private void DestroyThumbnailTexture()
    {
        if (continueThumbnail != null)
            continueThumbnail.style.backgroundImage = StyleKeyword.None;

        if (thumbnailTexture != null)
        {
            Destroy(thumbnailTexture);
            thumbnailTexture = null;
        }
    }
}
