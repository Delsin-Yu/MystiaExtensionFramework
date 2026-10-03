using Mystia.Listeners;

namespace Mystia.Scenes;

[AutoWire]
public interface IGuestSpawnModifier
{
    void OnPreSpawnNormalGuests(ref GuestSpawnRequest request, ref bool cancelInvocation) { }

    void OnPreSpawnSpecialGuest(ref GuestSpawnRequest request, ref int guestId, ref bool cancelInvocation) { }

    /// <summary>
    /// The rolled guests of one normal guest group, as <see cref="GuestDescription"/> values: a modifier may
    /// replace, drop or add one, and the framework resolves every id it leaves in the list back to the game's
    /// own guest. A description the database does not carry is reported and dropped, so the game is never asked
    /// to spawn a guest that does not exist.
    /// </summary>
    void OnNormalGuestsGenerating(ref List<GuestDescription> guests) { }

    void OnSpecialGuestGenerating(ref int guestId) { }

    void OnNormalGuestVisual(int guestId, ref int visualIndex) { }
}
