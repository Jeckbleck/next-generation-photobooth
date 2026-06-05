using System.Collections.Generic;
using Moq;
using Xunit;

namespace Photobooth.Tests.Unit;

public sealed class FlowControllerTests
{
    private readonly Mock<INavigator> _nav = new();

    private FlowController CreateSut() => new FlowController(_nav.Object);

    // ── Idle transitions ──────────────────────────────────────────────────────

    [Fact]
    public void StartNormal_FromIdle_NavigatesToShooting()
    {
        var sut = CreateSut();
        sut.Trigger(FlowTrigger.StartNormal);
        _nav.Verify(n => n.NavigateTo(BoothState.Shooting), Times.Once);
        Assert.Equal(BoothState.Shooting, sut.CurrentState);
    }

    [Fact]
    public void StartAI_FromIdle_NavigatesToStylePick()
    {
        var sut = CreateSut();
        sut.Trigger(FlowTrigger.StartAI);
        _nav.Verify(n => n.NavigateTo(BoothState.StylePick), Times.Once);
        Assert.Equal(BoothState.StylePick, sut.CurrentState);
    }

    // ── StylePick transitions ─────────────────────────────────────────────────

    [Fact]
    public void StyleChosen_FromStylePick_NavigatesToShooting()
    {
        var sut = CreateSut();
        sut.Trigger(FlowTrigger.StartAI);   // reach StylePick
        _nav.Invocations.Clear();

        sut.Trigger(FlowTrigger.StyleChosen);
        _nav.Verify(n => n.NavigateTo(BoothState.Shooting), Times.Once);
        Assert.Equal(BoothState.Shooting, sut.CurrentState);
    }

    [Fact]
    public void StyleCancelled_FromStylePick_NavigatesToIdle()
    {
        var sut = CreateSut();
        sut.Trigger(FlowTrigger.StartAI);   // reach StylePick
        _nav.Invocations.Clear();

        sut.Trigger(FlowTrigger.StyleCancelled);
        _nav.Verify(n => n.NavigateTo(BoothState.Idle), Times.Once);
        Assert.Equal(BoothState.Idle, sut.CurrentState);
    }

    // ── Shooting transitions ──────────────────────────────────────────────────

    [Fact]
    public void ShotsDone_FromShooting_NavigatesToPreview()
    {
        var sut = CreateSut();
        sut.Trigger(FlowTrigger.StartNormal);  // reach Shooting
        _nav.Invocations.Clear();

        sut.Trigger(FlowTrigger.ShotsDone);
        _nav.Verify(n => n.NavigateTo(BoothState.Preview), Times.Once);
        Assert.Equal(BoothState.Preview, sut.CurrentState);
    }

    [Fact]
    public void SessionAborted_FromShooting_NavigatesToIdle()
    {
        var sut = CreateSut();
        sut.Trigger(FlowTrigger.StartNormal);  // reach Shooting
        _nav.Invocations.Clear();

        sut.Trigger(FlowTrigger.SessionAborted);
        _nav.Verify(n => n.NavigateTo(BoothState.Idle), Times.Once);
        Assert.Equal(BoothState.Idle, sut.CurrentState);
    }

    // ── Preview transitions ───────────────────────────────────────────────────

    [Fact]
    public void PreviewDone_FromPreview_NavigatesToIdle()
    {
        var sut = CreateSut();
        sut.Trigger(FlowTrigger.StartNormal);  // Shooting
        sut.Trigger(FlowTrigger.ShotsDone);    // Preview
        _nav.Invocations.Clear();

        sut.Trigger(FlowTrigger.PreviewDone);
        _nav.Verify(n => n.NavigateTo(BoothState.Idle), Times.Once);
        Assert.Equal(BoothState.Idle, sut.CurrentState);
    }

    // ── Invalid / ignored triggers ────────────────────────────────────────────

    [Fact]
    public void InvalidTrigger_DoesNotNavigate_AndStateUnchanged()
    {
        var sut = CreateSut();
        // ShotsDone while Idle has no mapping — should be silently ignored
        sut.Trigger(FlowTrigger.ShotsDone);
        _nav.Verify(n => n.NavigateTo(It.IsAny<BoothState>()), Times.Never);
        Assert.Equal(BoothState.Idle, sut.CurrentState);
    }

    // ── FlowContext propagation ───────────────────────────────────────────────

    [Fact]
    public void ShotsDone_WithContext_StoresPhotosAndSessionId()
    {
        var sut = CreateSut();
        sut.Trigger(FlowTrigger.StartNormal);   // Shooting

        var photos = new List<string> { "a.jpg", "b.jpg" };
        sut.Trigger(FlowTrigger.ShotsDone, new FlowContext { PhotoPaths = photos, SessionId = 42 });

        Assert.Equal(photos, sut.SessionPhotos);
        Assert.Equal(42,     sut.SessionId);
    }

    // ── StateChanged event ────────────────────────────────────────────────────

    [Fact]
    public void StateChanged_FiredWithCorrectNewState()
    {
        var sut = CreateSut();
        BoothState? fired = null;
        sut.StateChanged += (_, s) => fired = s;

        sut.Trigger(FlowTrigger.StartNormal);

        Assert.Equal(BoothState.Shooting, fired);
    }
}
