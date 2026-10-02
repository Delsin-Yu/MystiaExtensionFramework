namespace Mystia;

public partial interface IModContext
{
    ILog Log { get; }

    IMainThreadScheduler MainThread { get; }

    IGamePaths Paths { get; }

    IIl2CppComponentHost Components { get; }
}
