using HdrPilot.App.Services;
using HdrPilot.Tests.TestSupport;
using Microsoft.Extensions.Logging;

namespace HdrPilot.App.Tests.Shell;

public sealed class NotificationGateTests
{
    [Fact]
    public void Disabled_notifications_are_only_logged()
    {
        var state = new FixedState(UserNotificationState.AcceptsNotifications);
        var logger = new ListLogger<NotificationGate>();
        var gate = new NotificationGate(() => false, state, logger);

        var decision = gate.Evaluate("HDR an", "Cyberpunk 2077");

        Assert.Equal(NotificationDecision.SuppressedDisabled, decision);
        Assert.Equal(0, state.Queries);
        Assert.True(logger.Contains(LogLevel.Information, "Cyberpunk 2077"));
    }

    [Theory]
    [InlineData(UserNotificationState.Busy)]
    [InlineData(UserNotificationState.RunningDirect3DFullScreen)]
    [InlineData(UserNotificationState.PresentationMode)]
    [InlineData(UserNotificationState.NotPresent)]
    [InlineData(UserNotificationState.App)]
    public void Fullscreen_game_or_presentation_suppresses_and_logs(UserNotificationState current)
    {
        var logger = new ListLogger<NotificationGate>();
        var gate = new NotificationGate(() => true, new FixedState(current), logger);

        Assert.Equal(NotificationDecision.SuppressedBusy, gate.Evaluate("Schalten fehlgeschlagen", "Bildschirm meldet kein HDR"));
        Assert.True(logger.Contains(LogLevel.Information, "Bildschirm meldet kein HDR"));
        Assert.True(NotificationGate.IsBusy(current));
    }

    [Theory]
    [InlineData(UserNotificationState.AcceptsNotifications)]
    [InlineData(UserNotificationState.QuietTime)]
    [InlineData(UserNotificationState.Unknown)]
    public void Free_desktop_shows_the_notification(UserNotificationState current)
    {
        var state = new FixedState(current);
        var gate = new NotificationGate(() => true, state);

        Assert.Equal(NotificationDecision.Show, gate.Evaluate("HDR an", "Hades II"));
        Assert.Equal(1, state.Queries);
    }

    [Fact]
    public void Setting_is_read_at_each_notification()
    {
        var enabled = true;
        var gate = new NotificationGate(() => enabled, new FixedState(UserNotificationState.AcceptsNotifications));

        Assert.Equal(NotificationDecision.Show, gate.Evaluate("a", "b"));
        enabled = false;
        Assert.Equal(NotificationDecision.SuppressedDisabled, gate.Evaluate("a", "b"));
    }

    [Fact]
    public void Real_query_returns_a_defined_state()
    {
        var state = new ShellUserNotificationState().Query();

        Assert.True(Enum.IsDefined(state));
    }

    private sealed class FixedState(UserNotificationState state) : IUserNotificationStateProvider
    {
        public int Queries { get; private set; }

        public UserNotificationState Query()
        {
            Queries++;
            return state;
        }
    }
}
