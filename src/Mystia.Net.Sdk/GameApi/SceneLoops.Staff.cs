using Mystia;

namespace Mystia.Scenes;

[AutoWire]
public interface IStaffSceneGameLoop
{
    void Setup(IStaffSceneServices services);

    void Update(IStaffSceneServices services, float delta);

    void Shutdown(IStaffSceneServices services);
}

public interface IStaffSceneServices
{
    ICommonServices Common { get; }

    IPresentationServices Presentation { get; }
}
