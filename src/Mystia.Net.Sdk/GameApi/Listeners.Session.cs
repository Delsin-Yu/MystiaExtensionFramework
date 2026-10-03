using Mystia;

namespace Mystia.Listeners;

/// <summary>Session level notifications. Scene changes stay on <see cref="IDayListener.OnSceneChanging"/>.</summary>
[AutoWire]
public interface ISessionListener
{
    void OnPlayerDataLoading() { }

    /// <summary>Player data is being reset; <c>rewindingDay</c> tells a rewind from a normal reset.</summary>
    void OnGameStatusResetting(bool rewindingDay) { }
}

[AutoWire]
public interface IStatusListener
{
    /// <summary>Player skin change (<c>RunTimeAlbum.ChangePlayerSkin</c>).</summary>
    void OnPlayerSkinChanged(int skinId) { }

    /// <summary>
    /// The izakaya skin decorated for a place resolved to a concrete index; <c>izakayaId</c> is the place
    /// the decoration was applied to, <c>skinIndex</c> is the resolved skin.
    /// </summary>
    void OnIzakayaSkinResolved(int izakayaId, int skinIndex) { }
}
