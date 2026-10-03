namespace Mystia.Modding.Bridge;

// The leave seams nest: PatientDepletedLeave ends in PayAndLeave (GuestsManager.cs:2842), PayAndLeave and
// ExBadLeave end in LeaveFromDesk (:2728, :2855), and RepellAndLeavePay/RepellAndLeaveNoPay/PlayerRepell go
// through RepellInternal (:2869, :2887, :2909) which ends in LeaveFromDesk again (:3011). Every leave seam
// reports its own OnGroupLeft, so a nested leave would report one group twice, the second time with the
// inner kind, and advance a listener's state machine twice. This count keeps the report to the outermost
// leave seam of a leave, whose kind is the one the caller asked for.
internal static class LeaveDispatch
{
    // The leave gate stopped the original, which therefore cannot nest: the seam stays out of the count but
    // keeps reporting, exactly as it did before the count existed.
    internal const int Skipped = -1;

    [ThreadStatic]
    private static int _depth;

    /// <summary>
    /// Prefix side; the value is handed to the paired <see cref="Exit"/>. It is the depth this seam entered
    /// at, so zero means it is the outermost leave seam of the leave in progress.
    /// </summary>
    internal static int Enter(bool allowed) => allowed ? _depth++ : Skipped;

    /// <summary>
    /// Postfix side, which HarmonyX runs even when a prefix returned false, and reports whether this seam is
    /// the one that notifies the listeners: the outermost one and the gated ones do. The depth is written
    /// back before the caller notifies, so a leave a listener starts inside the notification counts on its
    /// own, which leaves the notification of the outer leave unaffected. The seams call this from their
    /// finalizer as well, because a throw from the original skips the postfix; the write back is absolute,
    /// so doing it twice is harmless.
    /// </summary>
    internal static bool Exit(int state)
    {
        if (state == Skipped)
            return true;
        _depth = state;
        return state == 0;
    }
}
