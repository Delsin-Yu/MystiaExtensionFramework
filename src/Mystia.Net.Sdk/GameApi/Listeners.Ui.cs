using Mystia;
using Mystia.Scenes;

namespace Mystia.Listeners;

[AutoWire]
public interface IDayUiListener
{
    /// <summary>The day scene shop panel opened.</summary>
    void OnShopPannelOpened(ShopPannelView view) { }

    /// <summary>Day fast forward is about to run; set <c>cancelInvocation</c> to keep the fast forward from starting.</summary>
    void OnPreFastForward(ref bool cancelInvocation) { }
}

[AutoWire]
public interface IWorkUiListener
{
    /// <summary>The work scene HUD opened (the night scene UI manager, not the day scene one).</summary>
    void OnHudOpened() { }
}
