using Mystia.Modding.Bridge;
using Mystia;
using UnityEngine;

namespace Mystia.Modding.Host;

internal sealed class TextLog(Action<string> write, string tag = "")
    : ILog
{
    public void Info(string message) => Write("INFO", message);

    public void Warning(string message) => Write("WARN", message);

    public void Error(string message) => Write("ERROR", message);

    public void Debug(string message) => Write("DEBUG", message);

    public ILog Tag(string tag) => new TextLog(write, tag);

    private void Write(string level, string message)
    {
        var prefix = string.IsNullOrEmpty(tag) ? level : level + " [" + tag + "]";
        write(prefix + " " + message);
    }
}

internal sealed class UnavailableComponents : IIl2CppComponentHost
{
    public void RegisterBehaviour(Type behaviourType) =>
        throw new InvalidOperationException("IL2CPP component registration is available only inside an injected game.");

    public void CreatePersistent(string name, Type behaviourType) =>
        throw new InvalidOperationException("IL2CPP component registration is available only inside an injected game.");
}

internal sealed class HostContext(string gameRoot, string modsDirectory, ILog log, IMainThreadScheduler mainThread, IIl2CppComponentHost components)
    : IModContext
{
    public ILog Log { get; } = log;

    public IMainThreadScheduler MainThread { get; } = mainThread;

    public IGamePaths Paths { get; } = new HostPaths(gameRoot, modsDirectory);

    public IIl2CppComponentHost Components { get; } = components;

    public Sprite LoadSprite(string path) => SpriteFiles.Load(Paths.ModDirectory, path);

    private sealed class HostPaths(string gameRoot, string modDirectory)
        : IGamePaths
    {
        public string GameRoot { get; } = gameRoot;

        public string ModDirectory { get; } = modDirectory;
    }
}
