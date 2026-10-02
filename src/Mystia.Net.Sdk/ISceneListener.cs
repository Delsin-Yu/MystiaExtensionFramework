using Mystia;

namespace Mystia.Scenes;

[AutoWire]
public interface ISceneListener
{
    void OnSceneAwake(SceneId scene) { }

    void OnSceneStart(SceneId scene) { }
}
