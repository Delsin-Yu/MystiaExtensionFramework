using GameData.Core.Collections.NightSceneUtility;

using Mystia;
using Mystia.Listeners;

namespace Mystia.Scenes;

[AutoWire]
public interface IGuestSpawnModifier
{
    void OnPreSpawnNormalGuests(ref GuestSpawnRequest request, ref bool cancelInvocation) { }

    void OnPreSpawnSpecialGuest(ref GuestSpawnRequest request, ref int guestId, ref bool cancelInvocation) { }

    void OnNormalGuestsGenerating(ref List<NormalGuest> guests) { }

    void OnSpecialGuestGenerating(ref int guestId) { }

    void OnNormalGuestVisual(int guestId, ref int visualIndex) { }
}
