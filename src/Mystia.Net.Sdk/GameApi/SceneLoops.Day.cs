using GameData.Profile;

using Mystia;

namespace Mystia.Scenes;

[AutoWire]
public interface IDaySceneGameLoop
{
    void Setup(IDaySceneServices services);

    void Update(IDaySceneServices services, float delta);

    void Shutdown(IDaySceneServices services);
}

public interface IDaySceneServices
{
    IDaySceneMapServices Map { get; }

    IDaySceneScheduleServices Schedule { get; }

    IDaySceneGuestServices Guests { get; }

    IDaySceneInputServices Input { get; }

    ICommonServices Common { get; }

    IPresentationServices Presentation { get; }
}

public interface IDaySceneMapServices
{
    void Swap(string mapLabel, string markerName, int travelCount, Action? onFinished = null);

    /// <summary>Rebuilds the spawn markers of the current map from the day map data.</summary>
    void RefreshSpawnMarkers() => throw new NotSupportedException();
}

public interface IDaySceneScheduleServices
{
    void SetEndEnabled(bool enabled);

    void End();

    void Chat(string characterLabel);

    /// <summary>Replays one node reward through the game's own reward path.</summary>
    void ReplayReward(in SchedulerNode.Reward reward) => throw new NotSupportedException();
}

public interface IDaySceneInputServices
{
    void SetMoveEnabled(bool enabled);

    void SetSprintEnabled(bool enabled);

    void SetInteractEnabled(bool enabled);
}

public interface IDaySceneGuestServices
{
    void RecordInvited(int id);
}
