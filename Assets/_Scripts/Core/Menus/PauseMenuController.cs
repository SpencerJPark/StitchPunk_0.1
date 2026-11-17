public sealed class PauseMenuController
{
    public const double UnsavedProgressWarningSeconds = 60.0;

    private readonly IPauseMenuView view;
    private readonly IPauseMenuActions actions;
    private bool isSubscribed;
    private PauseScreen deleteReturnScreen = PauseScreen.LoadList;

    public PauseMenuController(IPauseMenuView view, IPauseMenuActions actions, PauseMenuModel model)
    {
        this.view = view;
        this.actions = actions;
        Model = model;
        CurrentScreen = PauseScreen.Main;
    }

    public bool IsOpen { get; private set; }
    public PauseScreen CurrentScreen { get; private set; }
    public PauseMenuModel Model { get; }

    public void Open()
    {
        if (!isSubscribed)
        {
            view.CommandIssued += Handle;
            isSubscribed = true;
        }
        IsOpen = true;
        GoTo(PauseScreen.Main);
    }

    public void Close()
    {
        IsOpen = false;
        view.Hide();
    }

    public void Dispose()
    {
        if (isSubscribed)
        {
            view.CommandIssued -= Handle;
            isSubscribed = false;
        }
    }

    public void RefreshCurrentScreen()
    {
        view.Show(CurrentScreen, Model);
    }

    public void Handle(PauseCommand command)
    {
        if (!IsOpen)
        {
            return;
        }

        switch (CurrentScreen)
        {
            case PauseScreen.Main:
                HandleMain(command);
                break;
            case PauseScreen.SaveList:
                HandleSaveList(command);
                break;
            case PauseScreen.LoadList:
                HandleLoadList(command);
                break;
            case PauseScreen.ConfirmOverwrite:
                if (command.Kind == PauseCommandKind.Confirm)
                {
                    string saveId = Model.PendingSaveId;
                    Model.PendingSaveId = null;
                    actions.SaveOverwrite(saveId);
                    GoTo(PauseScreen.Main);
                }
                else if (command.Kind == PauseCommandKind.Back)
                {
                    Model.PendingSaveId = null;
                    GoTo(PauseScreen.SaveList);
                }
                break;
            case PauseScreen.ConfirmDelete:
                if (command.Kind == PauseCommandKind.Confirm)
                {
                    string saveId = Model.PendingSaveId;
                    Model.PendingSaveId = null;
                    actions.DeleteSave(saveId);
                    GoTo(deleteReturnScreen);
                }
                else if (command.Kind == PauseCommandKind.Back)
                {
                    Model.PendingSaveId = null;
                    GoTo(deleteReturnScreen);
                }
                break;
            case PauseScreen.ConfirmReturnToTitle:
                if (command.Kind == PauseCommandKind.Confirm)
                {
                    actions.ReturnToTitle();
                }
                else if (command.Kind == PauseCommandKind.Back)
                {
                    GoTo(PauseScreen.Main);
                }
                break;
            case PauseScreen.ConfirmQuit:
                if (command.Kind == PauseCommandKind.Confirm)
                {
                    actions.Quit();
                }
                else if (command.Kind == PauseCommandKind.Back)
                {
                    GoTo(PauseScreen.Main);
                }
                break;
            case PauseScreen.Options:
                if (command.Kind == PauseCommandKind.Back)
                {
                    GoTo(PauseScreen.Main);
                }
                break;
        }
    }

    private void HandleMain(PauseCommand command)
    {
        switch (command.Kind)
        {
            case PauseCommandKind.Resume:
            case PauseCommandKind.Back:
                actions.Resume();
                Close();
                break;
            case PauseCommandKind.OpenSaveList:
                if (!Model.SavingBlockedByCutscene)
                {
                    GoTo(PauseScreen.SaveList);
                }
                break;
            case PauseCommandKind.OpenLoadList:
                GoTo(PauseScreen.LoadList);
                break;
            case PauseCommandKind.Options:
                actions.OpenOptions();
                GoTo(PauseScreen.Options);
                break;
            case PauseCommandKind.ReturnToTitle:
                if (Model.SecondsSinceLastSave > UnsavedProgressWarningSeconds)
                {
                    GoTo(PauseScreen.ConfirmReturnToTitle);
                }
                else
                {
                    actions.ReturnToTitle();
                }
                break;
            case PauseCommandKind.Quit:
                GoTo(PauseScreen.ConfirmQuit);
                break;
        }
    }

    private void HandleSaveList(PauseCommand command)
    {
        switch (command.Kind)
        {
            case PauseCommandKind.SaveNew:
                actions.SaveNew();
                GoTo(PauseScreen.Main);
                break;
            case PauseCommandKind.SaveOverwrite:
                if (command.SaveId != null)
                {
                    Model.PendingSaveId = command.SaveId;
                    GoTo(PauseScreen.ConfirmOverwrite);
                }
                break;
            case PauseCommandKind.DeleteSave:
                BeginDelete(command.SaveId, PauseScreen.SaveList);
                break;
            case PauseCommandKind.Back:
                GoTo(PauseScreen.Main);
                break;
        }
    }

    private void HandleLoadList(PauseCommand command)
    {
        switch (command.Kind)
        {
            case PauseCommandKind.LoadSave:
                if (command.SaveId != null)
                {
                    actions.LoadSave(command.SaveId);
                    Close();
                }
                break;
            case PauseCommandKind.DeleteSave:
                BeginDelete(command.SaveId, PauseScreen.LoadList);
                break;
            case PauseCommandKind.Back:
                GoTo(PauseScreen.Main);
                break;
        }
    }

    private void BeginDelete(string saveId, PauseScreen returnScreen)
    {
        if (saveId == null)
        {
            return;
        }
        Model.PendingSaveId = saveId;
        deleteReturnScreen = returnScreen;
        GoTo(PauseScreen.ConfirmDelete);
    }

    private void GoTo(PauseScreen screen)
    {
        CurrentScreen = screen;
        view.Show(screen, Model);
    }
}
