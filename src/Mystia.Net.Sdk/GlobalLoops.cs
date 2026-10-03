using Mystia.Scenes;

namespace Mystia;

[AutoWire]
public interface IGlobalGameLoop
{
    void Setup(IGlobalServices services) { }

    void Update(IGlobalServices services, float delta) { }

    void FixedUpdate(IGlobalServices services, float delta) { }

    void Shutdown(IGlobalServices services) { }
}

public interface IGlobalServices
{
    ICommonServices Common { get; }
}
