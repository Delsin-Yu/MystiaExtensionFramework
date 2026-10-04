using Mystia.Scenes;

namespace Mystia.Modding.Bridge;

internal static class ServiceScope
{
    [ThreadStatic]
    private static int _depth;

    internal static void Enter() => _depth++;

    internal static void Exit() => _depth--;

    internal static void Require()
    {
        if (_depth == 0)
            throw new InvalidOperationException("Scene services are only available inside Setup, Update, and Shutdown.");
    }
}

internal static class SceneLoopHost
{
    private enum Phase
    {
        Setup,
        Update,
        Shutdown,
    }

    private static SceneId? _active;

    /// <summary>
    /// The scene whose loop is running, or null when no scene loop was entered. A capability that is always
    /// available still has to know which scene it is acting on — the running map, for instance — and this is
    /// where it reads that from.
    /// </summary>
    internal static SceneId? Active => _active;

    internal static void Enter(SceneId scene)
    {
        if (_active == scene)
            return;
        Shutdown();
        _active = scene;
        StockGate.Reset(scene);
        // One new scene session per scene, so a routine started on the scene dispatcher dies with the scene
        // even before the next Unity scene is measured. Shutdown() above is the only other call site, which
        // keeps LeaveScene and EnterScene from rotating the session twice for one transition.
        CoroutinePump.EnterScene();
        Run(scene, Phase.Setup, 0f);
    }

    internal static void Reset() => Shutdown();

    internal static void Tick(float delta)
    {
        if (_active is not SceneId scene)
            return;
        Run(scene, Phase.Update, delta);
    }

    private static void Shutdown()
    {
        if (_active is not SceneId scene)
            return;
        _active = null;
        Run(scene, Phase.Shutdown, 0f);
        // The shutdown callbacks still ran inside the scene session; the session ends once they are done, so
        // the routines bound to it stop on the next pump tick.
        CoroutinePump.LeaveScene();
        // The same boundary ends the scene's entity session: every guest, order and dish handle minted in it
        // stops resolving here, so a handle a mod kept past its night is refused instead of naming an engine
        // object the game already destroyed (see Mystia.Scenes.EntitySession).
        EntitySession.Rotate();
    }

    private static void Run(SceneId scene, Phase phase, float delta)
    {
        ServiceScope.Enter();
        try
        {
            switch (scene)
            {
                case SceneId.Splash:
                    Dispatch.Run<ISplashSceneGameLoop>(loop => Call(loop, SplashSceneServices.Shared, phase, delta));
                    break;
                case SceneId.Main:
                    Dispatch.Run<IMainSceneGameLoop>(loop => Call(loop, MainSceneServices.Shared, phase, delta));
                    break;
                case SceneId.Day:
                    Dispatch.Run<IDaySceneGameLoop>(loop => Call(loop, DaySceneServices.Shared, phase, delta));
                    break;
                case SceneId.PrepNight:
                    Dispatch.Run<IPrepNightSceneGameLoop>(loop => Call(loop, PrepNightSceneServices.Shared, phase, delta));
                    break;
                case SceneId.Night:
                    Dispatch.Run<IWorkSceneGameLoop>(loop => Call(loop, WorkSceneServices.Shared, phase, delta));
                    break;
                case SceneId.Staff:
                    Dispatch.Run<IStaffSceneGameLoop>(loop => Call(loop, StaffSceneServices.Shared, phase, delta));
                    break;
                case SceneId.Result:
                    Dispatch.Run<IResultSceneGameLoop>(loop => Call(loop, ResultSceneServices.Shared, phase, delta));
                    break;
            }
        }
        finally
        {
            ServiceScope.Exit();
        }
    }

    private static void Call(ISplashSceneGameLoop loop, ISplashSceneServices services, Phase phase, float delta)
    {
        switch (phase)
        {
            case Phase.Setup:
                loop.Setup(services);
                break;
            case Phase.Update:
                loop.Update(services, delta);
                break;
            default:
                loop.Shutdown(services);
                break;
        }
    }

    private static void Call(IMainSceneGameLoop loop, IMainSceneServices services, Phase phase, float delta)
    {
        switch (phase)
        {
            case Phase.Setup:
                loop.Setup(services);
                break;
            case Phase.Update:
                loop.Update(services, delta);
                break;
            default:
                loop.Shutdown(services);
                break;
        }
    }

    private static void Call(IDaySceneGameLoop loop, IDaySceneServices services, Phase phase, float delta)
    {
        switch (phase)
        {
            case Phase.Setup:
                loop.Setup(services);
                break;
            case Phase.Update:
                loop.Update(services, delta);
                break;
            default:
                loop.Shutdown(services);
                break;
        }
    }

    private static void Call(IPrepNightSceneGameLoop loop, IPrepNightSceneServices services, Phase phase, float delta)
    {
        switch (phase)
        {
            case Phase.Setup:
                loop.Setup(services);
                break;
            case Phase.Update:
                loop.Update(services, delta);
                break;
            default:
                loop.Shutdown(services);
                break;
        }
    }

    private static void Call(IWorkSceneGameLoop loop, IWorkSceneServices services, Phase phase, float delta)
    {
        switch (phase)
        {
            case Phase.Setup:
                loop.Setup(services);
                break;
            case Phase.Update:
                loop.Update(services, delta);
                break;
            default:
                loop.Shutdown(services);
                break;
        }
    }

    private static void Call(IStaffSceneGameLoop loop, IStaffSceneServices services, Phase phase, float delta)
    {
        switch (phase)
        {
            case Phase.Setup:
                loop.Setup(services);
                break;
            case Phase.Update:
                loop.Update(services, delta);
                break;
            default:
                loop.Shutdown(services);
                break;
        }
    }

    private static void Call(IResultSceneGameLoop loop, IResultSceneServices services, Phase phase, float delta)
    {
        switch (phase)
        {
            case Phase.Setup:
                loop.Setup(services);
                break;
            case Phase.Update:
                loop.Update(services, delta);
                break;
            default:
                loop.Shutdown(services);
                break;
        }
    }
}
