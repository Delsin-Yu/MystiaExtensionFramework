using Mystia;

namespace Mystia.Scenes;

[AutoWire]
public interface IMainSceneGameLoop
{
    void Setup(IMainSceneServices services);

    void Update(IMainSceneServices services, float delta);

    void Shutdown(IMainSceneServices services);
}

public interface IMainSceneServices
{
    IMainSceneSessionServices Session { get; }

    ICommonServices Common { get; }

    IPresentationServices Presentation { get; }
}

public interface IMainSceneSessionServices
{
    void GotoDay();
}
