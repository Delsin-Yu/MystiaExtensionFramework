using Mystia;
using Mystia.Listeners;
using Mystia.Scenes;

namespace SampleMod.A;

public sealed class SceneLog : ISceneListener, IGuestGroupListener, IInitialization
{
    private ILog? _log;

    public void Initialize(IMod mod) => _log = mod.Log;

    public void OnSceneAwake(SceneId scene) => _log?.Info($"Sample A saw {scene}.");

    public void OnSceneStart(SceneId scene) => _log?.Info($"Sample A saw {scene}.");

    public void OnGroupSpawned(
        NightScene.GuestManagementUtility.GuestGroupController group,
        GuestSpawnRequest request) =>
        _log?.Info($"Sample A saw a guest group at desk {request.DeskCode} leaving by {request.LeaveType}.");
}
