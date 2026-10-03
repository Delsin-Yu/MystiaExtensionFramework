using Mystia;

namespace Mystia.Modding.Bridge;

// When the host installs its queued scheduler (BridgeInstaller.Install, before any mod loads) it becomes the
// ICommonServices.MainThread of every mod. A host that never installed one — the test host — gets this one
// instead, so the member stays callable and the work runs straight away rather than being queued forever.
internal sealed class InlineMainThread : IMainThreadScheduler
{
    internal static readonly InlineMainThread Shared = new();

    public void RunOnMainThread(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        action();
    }
}
