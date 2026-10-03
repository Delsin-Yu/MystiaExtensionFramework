using Mystia;
using Mystia.Scenes;

namespace SampleMod.B;

public sealed class SceneLog : ISceneListener, IInitialization
{
    private ILog? _log;

    public void Initialize(IMod mod) => _log = mod.Log;

    public void OnSceneAwake(SceneId scene) => _log?.Info($"Sample B saw {scene}.");

    public void OnSceneStart(SceneId scene) => _log?.Info($"Sample B saw {scene}.");
}
