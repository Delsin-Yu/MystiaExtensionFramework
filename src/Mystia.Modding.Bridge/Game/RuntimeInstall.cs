using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using HarmonyLib.Public.Patching;
using Il2CppInterop.Common;
using Il2CppInterop.HarmonySupport;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.Startup;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mystia.Data;
using Mystia.Listeners;
using Mystia.Scenes;

namespace Mystia.Modding.Bridge;

/// <summary>
/// Where Il2CppInterop's own reporting goes.
///
/// A native to managed trampoline answers a failure - the patch method threw, or anything it called did - by
/// reporting it to this logger and returning the default value to the caller, which is native code that cannot
/// see a managed exception. The default logger is a NullLogger, so the reason went nowhere: the engine received
/// a null, a zero or an empty string and failed somewhere else entirely, or asked the same uncompileable method
/// again, and a run that died this way left nothing behind that named the cause. Everything the interop reports
/// now reaches host.log, including the stack of every swallowed trampoline failure.
/// </summary>
/// <remarks>
/// Microsoft.Extensions.Logging.LogLevel and .EventId are written out in full on purpose. This namespace is
/// nested inside <c>Mystia</c>, whose own <see cref="LogLevel"/> is the framework's mod facing log level: the
/// plain name binds to that one - the enclosing namespace is searched before the file's using directives - and
/// the class then fails to implement the interface with CS0535, which names neither the type nor the reason.
/// </remarks>
internal sealed class HostLog : ILogger
{
    public IDisposable BeginScope<TState>(TState state) => NullLogger.Instance.BeginScope(state);

    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

    public void Log<TState>(
        Microsoft.Extensions.Logging.LogLevel logLevel,
        Microsoft.Extensions.Logging.EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        GameBridgeHook.Trace($"{logLevel}: {formatter(state, exception)}{(exception is null ? "" : Environment.NewLine + exception)}");
}

internal static class GameBridgeHook
{
    [ModuleInitializer]
    internal static void Hook()
    {
        GamePatches.InstallOverride = Install;
        GamePatches.RegisterOverride = Register;
        GamePatches.CreateOverride = Create;
    }

    private static void Install(string gameRoot)
    {
        _ = gameRoot;
        Il2CppInteropRuntime.Create(new RuntimeConfiguration
        {
            UnityVersion = new Version(2021, 3, 28),
            DetourProvider = new X64DetourProvider(),
        }).AddLogger(new HostLog()).AddHarmonySupport().Start();

        ClassInjector.RegisterTypeInIl2Cpp<MainThreadPump>();
        PortraitSprites.RegisterHandles();
        System.Threading.Volatile.Write(ref _runtimeReady, 1);
        // Every seam is installed by Il2CppInterop's patcher, which ends in the bridge's own x64 detour.
        // Both of MonoMod's entry points stay out of this process: the native detour resolver installs
        // MonoMod's JIT hook before it even looks at the method, and so does the managed resolver, which is
        // only reached for a patch Il2CppInterop declines - a closed generic, for instance. Once that hook
        // is installed, a patch whose target carries a struct has to resolve the interop class pointer
        // while Patch() runs, and the hook throws 0x8007000B instead, so the seam never installs.
        // Keeping both out is also why no seam may target a closed generic: see SingletonAwake below.
        PatchManager.ResolvePatcher -= NativeDetourMethodPatcher.TryResolve;
        PatchManager.ResolvePatcher -= ManagedMethodPatcher.TryResolve;
        var harmony = new Harmony("dev.mystia.modding.bridge");
        var applied = 0;
        var failed = 0;
        foreach (var type in typeof(GameBridgeHook).Assembly.GetTypes())
        {
            try
            {
                applied += new PatchClassProcessor(harmony, type).Patch()?.Count ?? 0;
            }
            catch (Exception error)
            {
                failed++;
                var failure = error.GetBaseException();
                Trace($"{type.FullName}: {failure.GetType().Name}: {failure.Message}{Environment.NewLine}{failure.StackTrace}");
                if (failure is BadImageFormatException)
                {
                    // Established by experiment: a target whose signature carries a non-primitive struct
                    // (OnSprintPerformed(CallbackContext), WriteCurrentPlayerDataToSlotAsync -> UniTask<...>)
                    // cannot be detoured on this runtime - MonoMod's JIT hook throws while compiling the
                    // wrapper. Patch a funnel instead: the same setter or a parameterless entry point.
                    Trace($"{type.FullName}: the target signature carries a struct, which this runtime cannot detour; patch a funnel with primitive or reference parameters.");
                }
            }
        }

        // One line a real run can be judged by: every seam of this build either patches or names itself above.
        Trace($"seams: {applied} patch methods applied, {failed} failed");
    }

    private static int _runtimeReady;
    private static int _pumpCreated;
    private static bool _singletonLogged;

    /// <summary>
    /// The game's universal manager derives from a closed generic singleton whose Awake runs OnAwake.
    /// Il2CppInterop cannot detour a closed generic, so the seam hangs on the concrete OnAwake instead -
    /// and it stays a seam (not a special case) so it is patched, counted and reported with the rest.
    /// </summary>
    [HarmonyPatch(typeof(Common.UI.UniversalGameManager), nameof(Common.UI.UniversalGameManager.OnAwake))]
    private static class SingletonAwake
    {
        private static void Postfix() => OnSingletonAwake();
    }

    private static void OnSingletonAwake()
    {
        if (_singletonLogged || BridgeInstaller.Registry is null)
            return;
        _singletonLogged = true;
        Dispatch.Run<ISceneListener>(listener => listener.OnSceneAwake(SceneId.Splash));
    }

    internal static void EnsurePump()
    {
        if (System.Threading.Volatile.Read(ref _runtimeReady) == 0)
            return;
        if (System.Threading.Interlocked.Exchange(ref _pumpCreated, 1) != 0)
            return;
        try
        {
            var host = new UnityEngine.GameObject("MystiaModHost");
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.AddComponent(Il2CppType.Of<MainThreadPump>());
        }
        catch (Exception error)
        {
            Trace(error.ToString());
        }
    }

    internal static void Trace(string message)
    {
        try
        {
            var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "host.log"));
            File.AppendAllText(path, DateTime.Now.ToString("HH:mm:ss.fff ") + message + Environment.NewLine);
        }
        catch
        {
        }
    }

    private static void Register(Type behaviourType) =>
        ClassInjector.RegisterTypeInIl2Cpp(behaviourType);

    private static void Create(string name, Type behaviourType)
    {
        ClassInjector.RegisterTypeInIl2Cpp(behaviourType);
        var host = new UnityEngine.GameObject(name);
        UnityEngine.Object.DontDestroyOnLoad(host);
        host.AddComponent(Il2CppType.From(behaviourType));
    }
}

internal sealed class MainThreadPump : UnityEngine.MonoBehaviour
{
    public MainThreadPump(nint pointer) : base(pointer)
    {
    }

    private void Update()
    {
        BridgeInstaller.MainThread?.Drain();
        // The one place IPlatformInfo is filled: the engine is asked for the store front's keys on the frame
        // the platform exists, which is long before a mod looks at the answer.
        PlatformInfo.TryResolve();
        // The one publish point of IClock.Now: this pump already runs on the main thread every frame, so the
        // unscaled time is read here rather than by the readers, which may be on any thread.
        HostClock.Publish(UnityEngine.Time.unscaledTime);
        CoroutinePump.Tick(UnityEngine.Time.deltaTime);
        GlobalHost.Tick(UnityEngine.Time.deltaTime);
        SceneLoopHost.Tick(UnityEngine.Time.deltaTime);
    }

    private void FixedUpdate()
    {
        CoroutinePump.FixedTick(UnityEngine.Time.fixedDeltaTime);
        GlobalHost.FixedTick(UnityEngine.Time.fixedDeltaTime);
    }

    private void OnGUI() => GlobalHost.DrawGui();

    private void OnDestroy()
    {
        GlobalHost.Shutdown();
        CoroutinePump.Drain();
    }
}
