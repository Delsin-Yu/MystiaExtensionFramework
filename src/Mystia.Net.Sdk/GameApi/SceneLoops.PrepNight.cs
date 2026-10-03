using Common.UI;
using Common.UI.GlobalMap;

using Mystia;

namespace Mystia.Scenes;

[AutoWire]
public interface IPrepNightSceneGameLoop
{
    void Setup(IPrepNightSceneServices services);

    void Update(IPrepNightSceneServices services, float delta);

    void Shutdown(IPrepNightSceneServices services);
}

public interface IPrepNightSceneServices
{
    IPrepNightMapServices Map { get; }

    IPrepNightMenuServices Menu { get; }

    IPrepNightSessionServices Session { get; }

    ICommonServices Common { get; }

    IPresentationServices Presentation { get; }
}

public interface IPrepNightMapServices
{
    void SetConfirmEnabled(bool enabled);

    void Confirm(IGuideMapSpot spot, IzakayaLevel level);
}

public interface IPrepNightMenuServices
{
    void AddRecipe(int id);

    void RemoveRecipe(int id);

    void AddBeverage(int id);

    void RemoveBeverage(int id);

    void AssignCooker(int id, int index);

    void RemoveCooker(int index);
}

public interface IPrepNightSessionServices
{
    void SetCompleteEnabled(bool enabled);

    void Confirm();

    void ToWork();
}
