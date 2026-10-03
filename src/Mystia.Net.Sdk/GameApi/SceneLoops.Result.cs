using Mystia;

namespace Mystia.Scenes;

[AutoWire]
public interface IResultSceneGameLoop
{
    void Setup(IResultSceneServices services);

    void Update(IResultSceneServices services, float delta);

    void Shutdown(IResultSceneServices services);
}

public interface IResultSceneServices
{
    ICommonServices Common { get; }

    IPresentationServices Presentation { get; }
}
