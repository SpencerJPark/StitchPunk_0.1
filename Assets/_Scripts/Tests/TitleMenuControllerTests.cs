using System;
using System.Collections.Generic;
using NUnit.Framework;

public class TitleMenuControllerTests
{
    private sealed class FakeTitleMenuView : ITitleMenuView
    {
        public TitleScreen LastShownScreen;

        public event Action<TitleCommand> CommandIssued;

        public void Show(TitleScreen screen, TitleMenuModel model)
        {
            LastShownScreen = screen;
        }

        public void Hide(float fadeSeconds)
        {
        }

        public void Raise(TitleCommand command)
        {
            CommandIssued?.Invoke(command);
        }
    }

    private sealed class FakeTitleMenuActions : ITitleMenuActions
    {
        public int ContinueNewestCount;
        public int StartNewGameCount;

        public void ContinueNewest() { ContinueNewestCount++; }
        public void StartNewGame() { StartNewGameCount++; }
        public void LoadSave(string saveId) { }
        public void DeleteSave(string saveId) { }
        public void OpenOptions() { }
        public void Quit() { }
    }

    private static TitleMenuModel CreateModelWithSaves()
    {
        TitleMenuModel model = new TitleMenuModel();
        model.SavesNewestFirst = new List<SaveHeaderV2> { new SaveHeaderV2 { saveId = "manual_a" } };
        return model;
    }

    private static TitleMenuController StartOnMain(
        FakeTitleMenuView view, FakeTitleMenuActions actions, TitleMenuModel model)
    {
        TitleMenuController controller = new TitleMenuController(view, actions, model);
        controller.Start();
        view.Raise(new TitleCommand(TitleCommandKind.AnyButton));
        return controller;
    }

    [Test]
    public void BackFromTheLoadListReturnsToMain()
    {
        FakeTitleMenuView view = new FakeTitleMenuView();
        TitleMenuController controller = StartOnMain(view, new FakeTitleMenuActions(), CreateModelWithSaves());

        view.Raise(new TitleCommand(TitleCommandKind.OpenLoadList));
        Assert.AreEqual(TitleScreen.LoadList, controller.CurrentScreen);

        view.Raise(new TitleCommand(TitleCommandKind.Back));
        Assert.AreEqual(TitleScreen.Main, controller.CurrentScreen);
        Assert.AreEqual(TitleScreen.Main, view.LastShownScreen);
    }

    [Test]
    public void NewGameWithExistingSavesAsksForConfirmationFirst()
    {
        FakeTitleMenuView view = new FakeTitleMenuView();
        FakeTitleMenuActions actions = new FakeTitleMenuActions();
        TitleMenuController controller = StartOnMain(view, actions, CreateModelWithSaves());

        controller.Handle(new TitleCommand(TitleCommandKind.NewGame));

        Assert.AreEqual(0, actions.StartNewGameCount);
        Assert.AreEqual(TitleScreen.ConfirmNewGame, controller.CurrentScreen);
    }

    [Test]
    public void ContinueDoesNothingWhenThereAreNoSaves()
    {
        FakeTitleMenuView view = new FakeTitleMenuView();
        FakeTitleMenuActions actions = new FakeTitleMenuActions();
        TitleMenuModel model = new TitleMenuModel();
        TitleMenuController controller = StartOnMain(view, actions, model);

        Assert.IsFalse(model.IsContinueOffered);
        controller.Handle(new TitleCommand(TitleCommandKind.Continue));

        Assert.AreEqual(0, actions.ContinueNewestCount);
        Assert.AreEqual(TitleScreen.Main, controller.CurrentScreen);
    }
}
