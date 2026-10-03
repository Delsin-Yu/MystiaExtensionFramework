using Mystia.Modding.Bridge;
using Mystia;

namespace Mystia.Modding.Host;

internal sealed class TextLog(Action<string> write, string tag = "", string id = "", string version = "")
    : ILog
{
    public string Id { get; } = id;

    public string Version { get; } = version;

    public void Info(string message) => Write("INFO", message);

    public void Warning(string message) => Write("WARN", message);

    public void Error(string message) => Write("ERROR", message);

    public void Debug(string message) => Write("DEBUG", message);

    public void Message(string message) => Write("MESSAGE", message);

    public void Fatal(string message) => Write("FATAL", message);

    public void Log(LogLevel level, string message)
    {
        switch (level)
        {
            case LogLevel.Debug:
                Debug(message);
                break;
            case LogLevel.Info:
                Info(message);
                break;
            case LogLevel.Message:
                Message(message);
                break;
            case LogLevel.Warning:
                Warning(message);
                break;
            case LogLevel.Error:
                Error(message);
                break;
            case LogLevel.Fatal:
                Fatal(message);
                break;
            default:
                Info(message);
                break;
        }
    }

    public ILog Tag(string tag) => new TextLog(write, tag, Id, Version);

    private void Write(string level, string message)
    {
        var prefix = string.IsNullOrEmpty(tag) ? level : level + " [" + tag + "]";
        write(prefix + " " + message);
    }
}

/// <summary>
/// The host itself, in the role of a mod. It is not loaded from a mod folder, so it carries the host's own
/// identity, the log every mod's log is tagged from, and the storage the host keeps for itself.
/// </summary>
internal sealed class HostContext(string modsDirectory, ILog log)
    : IMod
{
    /// <summary>The id that names the host wherever a mod id is asked for.</summary>
    internal const string HostId = "Mystia.Host";

    public string Id { get; } = HostId;

    public string Version { get; } = typeof(HostContext).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    public string Directory { get; } = modsDirectory;

    public ILog Log { get; } = log;

    public IModStorage Storage { get; } = new ModStorage(Path.Combine(modsDirectory, "host-state"));
}
