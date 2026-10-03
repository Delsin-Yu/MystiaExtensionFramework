using Mystia.Scenes;

namespace Mystia.Modding.Bridge;

internal static class StockGate
{
    internal static bool DayEnd = true;

    internal static bool Move = true;

    internal static bool Sprint = true;

    internal static bool Interact = true;

    internal static bool MapConfirm = true;

    internal static bool TransitionDialog = true;

    internal static bool Leave = true;

    internal static bool Order = true;

    internal static bool ServeClose = true;

    internal static bool Seating = true;

    internal static bool CookCall = true;

    internal static bool IzakayaClose = true;

    internal static bool PrepComplete = true;

    internal static bool Evaluation = true;

    [ThreadStatic]
    private static int _bypass;

    internal static bool Allow(bool open) => open || _bypass > 0;

    internal static void Bypass(Action action)
    {
        _bypass++;
        try
        {
            action();
        }
        finally
        {
            _bypass--;
        }
    }

    internal static void Reset(SceneId scene)
    {
        switch (scene)
        {
            case SceneId.Day:
                DayEnd = true;
                Move = true;
                Sprint = true;
                Interact = true;
                TransitionDialog = true;
                break;
            case SceneId.PrepNight:
                MapConfirm = true;
                PrepComplete = true;
                break;
            case SceneId.Night:
                Leave = true;
                Order = true;
                ServeClose = true;
                Seating = true;
                CookCall = true;
                IzakayaClose = true;
                Evaluation = true;
                break;
        }
    }
}
