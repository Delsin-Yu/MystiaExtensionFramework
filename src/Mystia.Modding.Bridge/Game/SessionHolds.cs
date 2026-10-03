using HarmonyLib;
using NightScene.GuestManagementUtility;
using PrepNightScene.UI;

namespace Mystia.Modding.Bridge;

// Gates IPrepNightSessionServices.SetCompleteEnabled: the prep panel's "start the night" callback, which
// PrepSeams.Ready notifies as IPrepListener.OnPrepConfirmed on the same method. Skipping the original still
// runs that postfix, so mods keep being told about the press. PrepNightSceneServices' own Confirm() runs
// inside StockGate.Bypass.
internal static class PrepCompleteHolds
{
    private const string CompletionCallback = "_SolveDailyCompletion_b__61_7";

    [HarmonyPatch(typeof(IzakayaConfigPannel), CompletionCallback)]
    private static class Confirm
    {
        private static bool Prefix() => StockGate.Allow(StockGate.PrepComplete);
    }

    // The callback above is compiler generated, so its name does not survive every game build;
    // PrepNightSceneServices.SessionServices.Confirm looks the pair up the same way. This class is inert as
    // long as the named callback resolves, and only gates the public refresh method when it does not.
    [HarmonyPatch(typeof(IzakayaConfigPannel), "SolveDailyCompletion")]
    private static class ConfirmFallback
    {
        private static readonly bool NamedCallbackExists =
            AccessTools.Method(typeof(IzakayaConfigPannel), CompletionCallback) is not null;

        private static bool Prefix() => NamedCallbackExists || StockGate.Allow(StockGate.PrepComplete);
    }
}

// Gates IWorkSceneGuests.SetEvaluationEnabled: the game's own evaluation entry point. The services' own
// IWorkSceneGuests.Evaluate runs inside StockGate.Bypass.
internal static class EvaluationHolds
{
    [HarmonyPatch(typeof(GuestsManager), nameof(GuestsManager.EvaluateOrder))]
    private static class Evaluation
    {
        private static bool Prefix() => StockGate.Allow(StockGate.Evaluation);
    }
}
