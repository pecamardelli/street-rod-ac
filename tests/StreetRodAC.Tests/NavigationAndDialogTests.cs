using Street_Rod_AC.Dialogs;
using Street_Rod_AC.Dialogs.Information;
using Street_Rod_AC.Navigation;

namespace StreetRodAC.Tests;

/// <summary>A dialog that only records what happened to it</summary>
internal sealed class FakeDialog(string? key = null) : IDialog
{
    public int Opened { get; private set; }
    public int Closed { get; private set; }
    public string? DuplicateKey { get; } = key;

    public void OnOpened() => Opened++;
    public void OnClosed() => Closed++;
}

internal sealed class OtherFakeDialog(string? key = null) : IDialog
{
    public string? DuplicateKey { get; } = key;
    public void OnOpened() { }
    public void OnClosed() { }
}

public class DialogServiceTests
{
    [Fact]
    public void A_dialog_asked_for_while_another_is_up_waits_its_turn()
    {
        var dialogs = new DialogService();
        var first = new FakeDialog();
        var second = new FakeDialog();

        dialogs.ShowDialog(first);
        dialogs.ShowDialog(second);
        Assert.Same(first, dialogs.CurrentDialog);
        Assert.Equal(0, second.Opened);

        dialogs.CloseDialog();
        Assert.Equal(1, first.Closed);
        Assert.Same(second, dialogs.CurrentDialog);
        Assert.Equal(1, second.Opened);

        dialogs.CloseDialog();
        Assert.Null(dialogs.CurrentDialog);
        Assert.False(dialogs.IsDialogOpen);
    }

    [Fact]
    public void The_same_question_is_not_asked_twice_at_once()
    {
        var dialogs = new DialogService();
        var shown = new FakeDialog("quit");
        dialogs.ShowDialog(shown);
        dialogs.ShowDialog(new FakeDialog("quit"));
        dialogs.ShowDialog(shown);

        dialogs.CloseDialog();
        Assert.Null(dialogs.CurrentDialog);
    }

    [Fact]
    public void A_waiting_duplicate_is_dropped_too_but_not_one_of_another_type_or_key()
    {
        var dialogs = new DialogService();
        dialogs.ShowDialog(new FakeDialog());
        dialogs.ShowDialog(new FakeDialog("a"));
        dialogs.ShowDialog(new FakeDialog("a"));
        dialogs.ShowDialog(new OtherFakeDialog("a"));
        dialogs.ShowDialog(new FakeDialog("b"));
        dialogs.ShowDialog(new FakeDialog());
        dialogs.ShowDialog(new FakeDialog());

        var seen = 1;
        while (dialogs.IsDialogOpen && seen < 20)
        {
            dialogs.CloseDialog();
            if (dialogs.IsDialogOpen) seen++;
        }

        // The first, "a" once, the other type's "a", "b", and both without a key
        Assert.Equal(6, seen);
    }

    [Fact]
    public void Jumping_the_queue_puts_the_one_in_front_back_first_without_closing_it()
    {
        var dialogs = new DialogService();
        var front = new FakeDialog();
        var waiting = new FakeDialog();
        var urgent = new FakeDialog();

        dialogs.ShowDialog(front);
        dialogs.ShowDialog(waiting);
        dialogs.ShowDialog(urgent, jumpQueue: true);

        Assert.Same(urgent, dialogs.CurrentDialog);
        Assert.Equal(0, front.Closed);

        dialogs.CloseDialog();
        Assert.Same(front, dialogs.CurrentDialog);
        Assert.Equal(2, front.Opened);

        dialogs.CloseDialog();
        Assert.Same(waiting, dialogs.CurrentDialog);
    }

    [Fact]
    public void Withdraw_takes_a_dialog_away_whether_up_or_waiting()
    {
        var dialogs = new DialogService();
        var front = new FakeDialog();
        var waiting = new FakeDialog();
        var last = new FakeDialog();
        dialogs.ShowDialog(front);
        dialogs.ShowDialog(waiting);
        dialogs.ShowDialog(last);

        dialogs.Withdraw(waiting);
        Assert.Same(front, dialogs.CurrentDialog);

        dialogs.Withdraw(front);
        Assert.Equal(1, front.Closed);
        Assert.Same(last, dialogs.CurrentDialog);
        Assert.Equal(0, waiting.Opened);

        // One that is already gone: nothing happens
        dialogs.Withdraw(front);
        Assert.Same(last, dialogs.CurrentDialog);
    }

    [Fact]
    public void A_dialog_closes_before_its_callback_runs()
    {
        var dialogs = new DialogService();
        IDialog? upDuringCallback = null;
        var followUp = new FakeDialog();
        InformationDialogViewModel? info = null;
        info = new InformationDialogViewModel(dialogs, "Done", "Title", () =>
        {
            upDuringCallback = dialogs.CurrentDialog;
            dialogs.ShowDialog(followUp);
        });

        dialogs.ShowDialog(info);
        info.OkCommand.Execute(null);

        Assert.Null(upDuringCallback);
        // The dialog the callback showed is not closed along with the first
        Assert.Same(followUp, dialogs.CurrentDialog);
    }
}

public class NavigationServiceTests
{
    private sealed class FakeScreen(bool throwOnEnter = false) : IScreen
    {
        public List<string> Calls { get; } = new();

        public void Enter()
        {
            Calls.Add("Enter");
            if (throwOnEnter) throw new InvalidOperationException("enter failed");
        }

        public void Exit() => Calls.Add("Exit");
        public void Resume() => Calls.Add("Resume");
    }

    // The services only go to the screens the typed factories build; SafeNavigate is given its screen here
    private static NavigationService NewService(DialogService dialogs) => new(
        dialogs, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
        null!, null!, null!, null!, null!, null!, null!, null!, _ => { });

    [Fact]
    public void A_screen_that_enters_becomes_the_current_one()
    {
        var dialogs = new DialogService();
        var navigation = NewService(dialogs);
        var first = new FakeScreen();
        var second = new FakeScreen();

        Assert.True(navigation.SafeNavigate("first", () => first));
        Assert.True(navigation.SafeNavigate("second", () => second));

        Assert.Same(second, navigation.CurrentScreen);
        Assert.Equal(new[] { "Enter", "Exit" }, first.Calls);
        Assert.Equal(new[] { "Enter" }, second.Calls);
        Assert.False(dialogs.IsDialogOpen);
    }

    [Fact]
    public void A_constructor_that_throws_leaves_the_player_where_they_were()
    {
        var dialogs = new DialogService();
        var navigation = NewService(dialogs);
        var home = new FakeScreen();
        navigation.SafeNavigate("home", () => home);

        Assert.False(navigation.SafeNavigate("broken", () => throw new InvalidOperationException("ctor failed")));

        Assert.Same(home, navigation.CurrentScreen);
        Assert.Equal(new[] { "Enter" }, home.Calls);
        Assert.IsType<InformationDialogViewModel>(dialogs.CurrentDialog);
    }

    [Fact]
    public void An_enter_that_throws_goes_back_by_resuming_never_entering_again()
    {
        var dialogs = new DialogService();
        var navigation = NewService(dialogs);
        var home = new FakeScreen();
        var broken = new FakeScreen(throwOnEnter: true);
        navigation.SafeNavigate("home", () => home);

        Assert.False(navigation.SafeNavigate("broken", () => broken));

        Assert.Same(home, navigation.CurrentScreen);
        Assert.Equal(new[] { "Enter", "Exit", "Resume" }, home.Calls);
        Assert.Equal(new[] { "Enter", "Exit" }, broken.Calls);
        Assert.IsType<InformationDialogViewModel>(dialogs.CurrentDialog);
    }

    [Fact]
    public void The_same_error_twice_is_one_dialog()
    {
        var dialogs = new DialogService();
        var navigation = NewService(dialogs);
        navigation.SafeNavigate("home", () => new FakeScreen());

        navigation.SafeNavigate("broken", () => throw new InvalidOperationException());
        navigation.SafeNavigate("broken", () => throw new InvalidOperationException());

        dialogs.CloseDialog();
        Assert.False(dialogs.IsDialogOpen);
    }

    [Fact]
    public void A_card_that_opens_is_entered_and_the_screen_stays()
    {
        var dialogs = new DialogService();
        var navigation = NewService(dialogs);
        var home = new FakeScreen();
        navigation.SafeNavigate("home", () => home);
        var card = new FakeScreen();

        Assert.Same(card, navigation.SafeOpenCard("card", () => card));

        Assert.Same(home, navigation.CurrentScreen);
        Assert.Equal(new[] { "Enter" }, card.Calls);
        Assert.False(dialogs.IsDialogOpen);
    }

    [Fact]
    public void A_card_that_will_not_build_or_enter_is_reported_and_left()
    {
        var dialogs = new DialogService();
        var navigation = NewService(dialogs);
        var broken = new FakeScreen(throwOnEnter: true);

        Assert.Null(navigation.SafeOpenCard<FakeScreen>("card", () => throw new InvalidOperationException("ctor failed")));
        Assert.IsType<InformationDialogViewModel>(dialogs.CurrentDialog);
        dialogs.CloseDialog();

        Assert.Null(navigation.SafeOpenCard("card", () => broken));
        Assert.Equal(new[] { "Enter", "Exit" }, broken.Calls);
        Assert.IsType<InformationDialogViewModel>(dialogs.CurrentDialog);
    }
}
