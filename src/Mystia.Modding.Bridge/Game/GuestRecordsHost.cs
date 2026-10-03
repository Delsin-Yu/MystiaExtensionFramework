using GameData.RunTime.Common;

namespace Mystia.Modding.Bridge;

internal sealed class GuestRecords : IGuestRecords
{
    internal static readonly GuestRecords Shared = new();

    public void RecordInvited(int guestId) => StatusTracker.Instance.RecordInvitedGuest(guestId);

    public bool HasInvited(int guestId) => StatusTracker.Instance.HasNPCInvited(guestId);

    public bool IsIgnored(int guestId) => StatusTracker.Instance.IgnoredGuests.Contains(guestId);

    // StatusTracker.Reset clears the night-business statistics and leaves the invite list untouched, so clear the list itself.
    public void Reset() => StatusTracker.Instance.InvitedGuests.Clear();
}
