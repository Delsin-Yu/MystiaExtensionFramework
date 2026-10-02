namespace Mystia;

public interface IMainThreadScheduler
{
    void RunOnMainThread(Action action);
}
