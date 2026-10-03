namespace Mystia;

public enum LogLevel
{
    Debug,
    Info,
    Message,
    Warning,
    Error,
    Fatal,
}

public interface ILog
{
    string Id { get; }

    string Version { get; }

    void Info(string message);

    void Warning(string message);

    void Error(string message);

    void Debug(string message);

    void Message(string message);

    void Fatal(string message);

    void Log(LogLevel level, string message);

    ILog Tag(string tag);
}
