using Mystia;
using Mystia.Scenes;

namespace SampleMod.B;

public sealed class SceneLog : ISceneListener, IPostInitialize
{
    private ILog? _log;

    public void PostInitialize(IModContext context) => _log = context.Log;

    public void OnSceneAwake(SceneId scene) => _log?.Info($"Sample B saw {scene}.");

    public void OnSceneStart(SceneId scene) => _log?.Info($"Sample B saw {scene}.");
}
