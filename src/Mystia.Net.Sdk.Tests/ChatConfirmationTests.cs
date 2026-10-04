using Mystia;
using Mystia.Listeners;
using Mystia.Modding.Bridge;
using Xunit;

namespace Mystia.Tests;

/// <summary>
/// The chat confirmations the day scene acts on: who is asked, what a verdict does to the listeners after it, and
/// that the game's own action travels with the notification, so the listener that held the confirmation can run it
/// later. The dispatch is the framework's own pure code, so none of this needs the game running.
/// </summary>
[Collection("Panel and schedule interceptions")]
public sealed class ChatConfirmationTests : IDisposable
{
    private readonly ModRegistry _registry = new();

    public ChatConfirmationTests() => BridgeInstaller.Bind(_registry);

    public void Dispose() => BridgeInstaller.Bind(null);

    [Fact]
    public void EveryListenerSeesTheConfirmationAndTheActionTravelsWithIt()
    {
        var trace = new List<string>();
        _registry.Add<IChatConfirmationListener>(new ConfirmationListener(trace, "first"));
        _registry.Add<IChatConfirmationListener>(new ConfirmationListener(trace, "second"));
        var ran = 0;

        var allowed = ChatConfirmations.Allow(ChatConfirmationKind.YuyukoChallenge, true, () => ran++);

        Assert.True(allowed);
        Assert.Equal(new[] { "first", "second" }, trace);
        // The pipeline never runs the action itself: what the game was about to do is the listener's to run.
        Assert.Equal(0, ran);
    }

    [Fact]
    public void AConfirmationIsReportedWithTheVerdictTheGameWasHanded()
    {
        var trace = new List<string>();
        var listener = new ConfirmationListener(trace, "first");
        _registry.Add<IChatConfirmationListener>(listener);

        ChatConfirmations.Allow(ChatConfirmationKind.YuyukoChallenge, false, () => { });

        Assert.NotNull(listener.Seen);
        Assert.Equal(ChatConfirmationKind.YuyukoChallenge, listener.Seen!.Kind);
        Assert.False(listener.Seen.Confirmed);
    }

    [Fact]
    public void AHeldConfirmationOnlyHappensWhenTheListenerRunsIt()
    {
        var trace = new List<string>();
        var holder = new ConfirmationListener(trace, "holder").Hold();
        _registry.Add<IChatConfirmationListener>(holder);
        var ran = 0;

        var allowed = ChatConfirmations.Allow(ChatConfirmationKind.YuyukoChallenge, true, () => ran++);

        Assert.False(allowed);
        Assert.Equal(0, ran);

        // The action the game was about to run is still there after the notification returned.
        Assert.NotNull(holder.Seen?.Confirm);
        holder.Seen!.Confirm();
        Assert.Equal(1, ran);
    }

    [Fact]
    public void EveryListenerIsAskedAfterOneHeldTheConfirmation()
    {
        var trace = new List<string>();
        _registry.Add<IChatConfirmationListener>(new ConfirmationListener(trace, "holder").Hold());
        var later = new ConfirmationListener(trace, "later");
        _registry.Add<IChatConfirmationListener>(later);

        ChatConfirmations.Allow(ChatConfirmationKind.YuyukoChallenge, true, () => { });

        Assert.Equal(new[] { "holder", "later" }, trace);
        Assert.True(later.SawCancel);
    }

    private sealed class ConfirmationListener(List<string> trace, string name) : IChatConfirmationListener
    {
        internal ChatConfirmationView? Seen { get; private set; }

        internal bool SawCancel { get; private set; }

        private bool _hold;

        internal ConfirmationListener Hold()
        {
            _hold = true;
            return this;
        }

        public void OnPreChatConfirmation(ChatConfirmationView confirmation, ref bool cancelInvocation)
        {
            trace.Add(name);
            Seen = confirmation;
            SawCancel = cancelInvocation;
            cancelInvocation |= _hold;
        }
    }
}
