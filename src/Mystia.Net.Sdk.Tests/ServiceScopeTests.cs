using System.Collections;
using Mystia.Modding.Bridge;
using Mystia.Scenes;
using Xunit;

namespace Mystia.Tests;

// BridgeInstaller.Registry / SceneLoopHost / GlobalHost are process wide statics, so this class must not run
// in parallel with the other suites that bind a registry (the xunit default parallelises test classes).
[CollectionDefinition("Scene loop services", DisableParallelization = true)]
public sealed class SceneLoopServicesCollection
{
}

/// <summary>
/// The capability split: what a mod may use at any time (<see cref="ICommonServices"/>, a global loop
/// included) versus what only exists while a scene loop runs (<see cref="IPresentationServices"/>, reached
/// through the <c>Presentation</c> member of the scene services).
/// </summary>
[Collection("Scene loop services")]
public sealed class ServiceScopeTests
{
    [Fact]
    public void AGlobalLoopCanUseMainThreadCoroutinesAndPlatform()
    {
        ICommonServices? seen = null;
        var registry = new ModRegistry();
        registry.Add<IGlobalGameLoop>(new CapturingGlobalLoop(services => seen = services.Common));
        BridgeInstaller.Bind(registry);
        try
        {
            GlobalHost.Setup();
            var common = seen ?? throw new InvalidOperationException("The global loop did not receive services.");

            Assert.NotNull(common.MainThread);
            // A global loop must not get a dispatcher whose routines die with the next scene.
            Assert.Same(CoroutineScheduler.Global, common.Coroutines);
            Assert.Same(PlatformInfo.Shared, common.Platform);
            Assert.False(common.Platform.KeysResolved);

            var ran = false;
            common.MainThread.RunOnMainThread(() => ran = true);
            Assert.True(ran);

            var started = false;
            _ = common.Coroutines.Start(_ => RunsOnce(() => started = true));
            Assert.True(started);
        }
        finally
        {
            GlobalHost.Shutdown();
            BridgeInstaller.Bind(null);
        }
    }

    [Fact]
    public void PresentationMembersThrowOutsideASceneLoop()
    {
        var presentation = SplashSceneServices.Shared.Presentation;

        Assert.Throws<InvalidOperationException>(() => { presentation.ShakeCamera(0.1f, 0.1f, 0.1f); });
        Assert.Throws<InvalidOperationException>(() => { presentation.PlayVfx("effect", default); });
        Assert.Throws<InvalidOperationException>(() => { presentation.PlayAudio("sound"); });
        Assert.Throws<InvalidOperationException>(() => { _ = presentation.PlayerPosition; });
        Assert.Throws<InvalidOperationException>(() => { _ = presentation.TablePosition(0); });
    }

    [Fact]
    public void PresentationIsUsableInsideTheSceneLoop()
    {
        var loop = new PresentationDayLoop();
        var registry = new ModRegistry();
        registry.Add<IDaySceneGameLoop>(loop);
        BridgeInstaller.Bind(registry);
        try
        {
            SceneLoopHost.Enter(SceneId.Day);

            // The presentation member answered inside the loop without throwing; nothing is filed under the
            // key, so the miss answers null rather than a handle to something that never played.
            Assert.Null(loop.Handle);
            // The scene loop is handed the same always available services a global loop gets.
            Assert.Same(GlobalServices.Shared.Common, loop.Common);
        }
        finally
        {
            SceneLoopHost.Reset();
            BridgeInstaller.Bind(null);
        }
    }

    [Fact]
    public void ThePreviousScenesPresentationIsInvalidAfterASceneSwitch()
    {
        var loop = new CapturingDayLoop();
        var registry = new ModRegistry();
        registry.Add<IDaySceneGameLoop>(loop);
        BridgeInstaller.Bind(registry);
        try
        {
            SceneLoopHost.Enter(SceneId.Day);
            // Inside the day loop's Setup the day presentation answered (the key is unfiled, so it answered
            // null without throwing; what matters here is that the member was reachable at all).
            Assert.Null(loop.Handle);
            var day = loop.Services ?? throw new InvalidOperationException("Setup did not receive services.");

            // The day scene is replaced: the services of the previous scene are dead outside its own loop.
            SceneLoopHost.Enter(SceneId.Main);

            Assert.Throws<InvalidOperationException>(() => { day.Presentation.PlayAudio("sound"); });
            Assert.Throws<InvalidOperationException>(() => { day.Presentation.PlayVfx("effect", default); });
        }
        finally
        {
            SceneLoopHost.Reset();
            BridgeInstaller.Bind(null);
        }
    }

    private static IEnumerator RunsOnce(Action run)
    {
        run();
        yield break;
    }

    private sealed class CapturingGlobalLoop(Action<IGlobalServices> capture) : IGlobalGameLoop
    {
        public void Setup(IGlobalServices services) => capture(services);
    }

    private sealed class CapturingDayLoop : IDaySceneGameLoop
    {
        internal IDaySceneServices? Services;

        internal IVfxHandle? Handle;

        public void Setup(IDaySceneServices services)
        {
            Services = services;
            Handle = services.Presentation.PlayVfx("effect", default);
        }

        public void Update(IDaySceneServices services, float delta)
        {
        }

        public void Shutdown(IDaySceneServices services)
        {
        }
    }

    private sealed class PresentationDayLoop : IDaySceneGameLoop
    {
        internal IVfxHandle? Handle;

        internal ICommonServices? Common;

        public void Setup(IDaySceneServices services)
        {
            Common = services.Common;
            Handle = services.Presentation.PlayVfx("effect", default);
        }

        public void Update(IDaySceneServices services, float delta)
        {
        }

        public void Shutdown(IDaySceneServices services)
        {
        }
    }
}
