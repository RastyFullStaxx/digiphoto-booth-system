using DigiPhoto.Contracts.Sessions;

namespace DigiPhoto.Booth.Sessions;

public static class SessionStateMachine
{
    private static readonly Dictionary<SessionState, SessionState[]> Allowed =
        new Dictionary<SessionState, SessionState[]>
        {
            [SessionState.Attract] = [SessionState.PackageSelection],
            [SessionState.PackageSelection] = [SessionState.PrivacyNotice],
            [SessionState.PrivacyNotice] = [SessionState.LivePreview],
            [SessionState.LivePreview] = [SessionState.Countdown],
            [SessionState.Countdown] = [SessionState.Capturing],
            [SessionState.Capturing] = [SessionState.Review, SessionState.RecoveryRequired],
            [SessionState.Review] = [SessionState.Rendering],
            [SessionState.Rendering] = [SessionState.PrintPending, SessionState.RecoveryRequired],
            [SessionState.PrintPending] = [SessionState.Printing, SessionState.RecoveryRequired],
            [SessionState.Printing] = [SessionState.Completed, SessionState.RecoveryRequired],
            [SessionState.RecoveryRequired] = [SessionState.Completed, SessionState.Cancelled],
        };

    public static bool CanTransition(SessionState current, SessionState next) =>
        Allowed.TryGetValue(current, out var states) && states.Contains(next);

    public static void EnsureAllowed(SessionState current, SessionState next)
    {
        if (!CanTransition(current, next))
        {
            throw new BoothWorkflowException($"Cannot move a session from {current} to {next}.");
        }
    }
}
