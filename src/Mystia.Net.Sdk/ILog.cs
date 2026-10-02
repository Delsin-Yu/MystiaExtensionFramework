namespace Mystia;

public interface ILog
{
    void Info(string message);

    void Warning(string message);

    void Error(string message);

    void Debug(string message);

    ILog Tag(string tag);
}
