using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using HarmonyLib.Public.Patching;
using Mystia.Data;
using Mystia.Listeners;
using Mystia.Scenes;
using Il2CppInterop.HarmonySupport;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.Startup;

namespace Mystia.Modding.Bridge;

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
        }).AddHarmonySupport().Start();

        ClassInjector.RegisterTypeInIl2Cpp<MainThreadPump>();
        PortraitSprites.RegisterHandles();
        System.Threading.Volatile.Write(ref _runtimeReady, 1);
        // IL2CPP methods are patched through X64DetourProvider. Harmony's native detour
        // resolver installs MonoMod's JIT hook before it looks at the method.
        PatchManager.ResolvePatcher -= NativeDetourMethodPatcher.TryResolve;
        var harmony = new Harmony("dev.mystia.modding.bridge");
        var singletonAwake = AccessTools.Method(
            typeof(DEYU.Singletons.MonoSingletonPersistant<>).MakeGenericType(typeof(Common.UI.UniversalGameManager)),
            "Awake");
        harmony.Patch(singletonAwake, postfix: new HarmonyMethod(typeof(GameBridgeHook), nameof(OnSingletonAwake), Type.EmptyTypes));
        foreach (var type in typeof(GameBridgeHook).Assembly.GetTypes())
        {
            try
            {
                new PatchClassProcessor(harmony, type).Patch();
            }
            catch (Exception error)
            {
                Trace(type.FullName + ": " + error.GetBaseException().Message);
            }
        }
    }

    private static int _runtimeReady;
    private static int _pumpCreated;
    private static bool _singletonLogged;

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
