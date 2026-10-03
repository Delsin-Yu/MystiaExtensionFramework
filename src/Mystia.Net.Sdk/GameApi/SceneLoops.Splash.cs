using Mystia;

namespace Mystia.Scenes;

[AutoWire]
public interface ISplashSceneGameLoop
{
    void Setup(ISplashSceneServices services);

    void Update(ISplashSceneServices services, float delta);

    void Shutdown(ISplashSceneServices services);
}

public interface ISplashSceneServices
{
    ICommonServices Common { get; }

    IPresentationServices Presentation { get; }
}
