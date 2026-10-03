using Mystia;

namespace Mystia.Modding.Bridge;

internal sealed class QueuedMainThread : IMainThreadScheduler
{
    private readonly Queue<Action> _queue = new();
    private readonly object _gate = new();

    public void RunOnMainThread(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (_gate)
            _queue.Enqueue(action);
    }

    public void Drain()
    {
        while (true)
        {
            Action action;
            lock (_gate)
            {
                if (_queue.Count == 0)
                    return;
                action = _queue.Dequeue();
            }

            action();
        }
    }
}

internal static class Dispatch
{
    public static void Run<T>(Action<T> invoke) where T : class
    {
        GameBridgeHook.EnsurePump();
        foreach (var listener in Instances<T>())
            invoke(listener);
    }

    internal static IEnumerable<T> Instances<T>() where T : class
    {
        var registry = BridgeInstaller.Registry;
        if (registry is null)
            yield break;

        foreach (var listener in registry.GetInstances<T>())
            yield return listener;
    }
}

internal static class BridgeInstaller
{
    public static ModRegistry? Registry { get; private set; }

    public static QueuedMainThread? MainThread { get; private set; }

    public static void Install(QueuedMainThread mainThread, BridgeComponents components, string gameRoot)
    {
        MainThread = mainThread;
        components.Bind(mainThread);
        CoroutinePump.SetLogSink(GameBridgeHook.Trace);
        GamePatches.TryInstall(gameRoot);
    }

    public static void Bind(ModRegistry? registry) => Registry = registry;
}

internal sealed class BridgeComponents : IIl2CppComponentHost
{
    private QueuedMainThread? _mainThread;

    internal void Bind(QueuedMainThread mainThread) => _mainThread = mainThread;

    public void RegisterBehaviour(Type behaviourType) =>
        GamePatches.RegisterBehaviour(behaviourType);

    public void CreatePersistent(string name, Type behaviourType) =>
        GamePatches.CreatePersistent(name, behaviourType);
}
