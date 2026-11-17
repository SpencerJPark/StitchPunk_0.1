public sealed class TitleMenuController
{
    private readonly ITitleMenuView view;
    private readonly ITitleMenuActions actions;
    private bool isSubscribed;

    public TitleMenuController(ITitleMenuView view, ITitleMenuActions actions, TitleMenuModel model)
    {
        this.view = view;
        this.actions = actions;
        Model = model;
        CurrentScreen = TitleScreen.Splash;
    }

    public TitleScreen CurrentScreen { get; private set; }
    public TitleMenuModel Model { get; }

    public void Start()
    {
        if (!isSubscribed)
        {
            view.CommandIssued += Handle;
            isSubscribed = true;
        }
        GoTo(TitleScreen.Splash);
    }

    public void Stop()
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

    public void Handle(TitleCommand command)
    {
        switch (CurrentScreen)
        {
            case TitleScreen.Splash:
                if (command.Kind == TitleCommandKind.AnyButton)
                {
                    GoTo(TitleScreen.Main);
                }
                break;
            case TitleScreen.Main:
                HandleMain(command);
                break;
            case TitleScreen.ConfirmNewGame:
                if (command.Kind == TitleCommandKind.ConfirmNewGame)
                {
                    actions.StartNewGame();
                }
                else if (command.Kind == TitleCommandKind.Back)
                {
                    GoTo(TitleScreen.Main);
                }
                break;
            case TitleScreen.LoadList:
                HandleLoadList(command);
                break;
            case TitleScreen.ConfirmDelete:
                HandleConfirmDelete(command);
                break;
            case TitleScreen.Options:
            case TitleScreen.Credits:
                if (command.Kind == TitleCommandKind.Back)
                {
                    GoTo(TitleScreen.Main);
                }
                break;
        }
    }

    private void HandleMain(TitleCommand command)
    {
        switch (command.Kind)
        {
            case TitleCommandKind.Continue:
                if (Model.IsContinueOffered)
                {
                    actions.ContinueNewest();
                }
                break;
            case TitleCommandKind.NewGame:
                if (Model.HasAnySave)
                {
                    GoTo(TitleScreen.ConfirmNewGame);
                }
                else
                {
                    actions.StartNewGame();
                }
                break;
            case TitleCommandKind.OpenLoadList:
                GoTo(TitleScreen.LoadList);
                break;
            case TitleCommandKind.Options:
                actions.OpenOptions();
                GoTo(TitleScreen.Options);
                break;
            case TitleCommandKind.Credits:
                GoTo(TitleScreen.Credits);
                break;
            case TitleCommandKind.Quit:
                actions.Quit();
                break;
            case TitleCommandKind.Back:
                GoTo(TitleScreen.Splash);
                break;
        }
    }

    private void HandleLoadList(TitleCommand command)
    {
        switch (command.Kind)
        {
            case TitleCommandKind.LoadSave:
                if (command.SaveId != null)
                {
                    actions.LoadSave(command.SaveId);
                }
                break;
            case TitleCommandKind.DeleteSave:
                if (command.SaveId != null)
                {
                    Model.PendingDeleteSaveId = command.SaveId;
                    GoTo(TitleScreen.ConfirmDelete);
                }
                break;
            case TitleCommandKind.Back:
                GoTo(TitleScreen.Main);
                break;
        }
    }

    private void HandleConfirmDelete(TitleCommand command)
    {
        if (command.Kind == TitleCommandKind.ConfirmDelete)
        {
            string saveId = Model.PendingDeleteSaveId;
            Model.PendingDeleteSaveId = null;
            actions.DeleteSave(saveId);
            GoTo(Model.HasAnySave ? TitleScreen.LoadList : TitleScreen.Main);
        }
        else if (command.Kind == TitleCommandKind.Back)
        {
            Model.PendingDeleteSaveId = null;
            GoTo(TitleScreen.LoadList);
        }
    }

    private void GoTo(TitleScreen screen)
    {
        CurrentScreen = screen;
        view.Show(screen, Model);
    }
}
