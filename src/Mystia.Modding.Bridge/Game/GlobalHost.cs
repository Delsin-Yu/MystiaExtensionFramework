using Mystia;
using Mystia.Scenes;

namespace Mystia.Modding.Bridge;

// Global loops live for the whole process and stay outside ServiceScope, so they may only use
// ICommonServices: the always available capabilities, never the scene scoped IPresentationServices.
internal static class GlobalHost
{
    private enum Phase
    {
        Update,
        FixedUpdate,
    }

    private static readonly List<IGlobalGameLoop> Loops = [];
    private static readonly ImguiDrawer Drawer = new();

    // The registry is bound after mods load, so every entry point re-reads it and sets up the
    // instances it has not seen yet. Setup itself is the eager form of the same call.
    internal static void Setup() => Sync();

    internal static void Tick(float delta) => Run(Phase.Update, delta);

    internal static void FixedTick(float delta) => Run(Phase.FixedUpdate, delta);

    internal static void DrawGui()
    {
        foreach (var provider in Dispatch.Instances<IIMGUIProvider>())
        {
            try
            {
                provider.OnGui(Drawer);
            }
            catch (Exception error)
            {
                Fail(provider, error);
            }
        }
    }

    internal static void Shutdown()
    {
        foreach (var loop in Loops)
        {
            try
            {
                loop.Shutdown(GlobalServices.Shared);
            }
            catch (Exception error)
            {
                Fail(loop, error);
            }
        }

        Loops.Clear();
    }

    private static void Run(Phase phase, float delta)
    {
        Sync();
        foreach (var loop in Loops)
        {
            try
            {
                switch (phase)
                {
                    case Phase.Update:
                        loop.Update(GlobalServices.Shared, delta);
                        break;
                    default:
                        loop.FixedUpdate(GlobalServices.Shared, delta);
                        break;
                }
            }
            catch (Exception error)
            {
                Fail(loop, error);
            }
        }
    }

    private static void Sync()
    {
        foreach (var loop in Dispatch.Instances<IGlobalGameLoop>())
        {
            if (Loops.Contains(loop))
                continue;
            Loops.Add(loop);
            try
            {
                loop.Setup(GlobalServices.Shared);
            }
            catch (Exception error)
            {
                Fail(loop, error);
            }
        }
    }

    // One throwing mod must not stop the other instances or the frame.
    private static void Fail(object instance, Exception error) =>
        GameBridgeHook.Trace("global " + instance.GetType().FullName + ": " + error);
}

internal sealed class GlobalServices : IGlobalServices
{
    internal static readonly GlobalServices Shared = new();

    public ICommonServices Common => CommonServices.Shared;
}
