using GameData.Profile;
using HarmonyLib;
using Mystia.Listeners;
using NightScene.CookingUtility;

namespace Mystia.Modding.Bridge;

// The QTE reward path. The pre hook can rewrite the slot index and the must-success flag; the reward buff
// field is informational there (a listener must not depend on it, and the bridge does not honour a write to
// it: use IQteServices.TriggerRewardBuff instead), because which of the five Mystia reward methods actually
// runs depends on runtime state the pre hook cannot see — the buffs are therefore reported on their own
// methods, and a replay decides the buff through IQteServices.
internal static class QteSeams
{
    [HarmonyPatch(typeof(QTERewardManager), nameof(QTERewardManager.OnQTESucceeded))]
    private static class Succeeded
    {
        private static bool Prefix(QTERewardManager __instance, ref int index, ref bool mustSuccess)
        {
            var reward = new QteReward(index, mustSuccess, DecideBuff(__instance, index));
            var cancel = false;
            foreach (var listener in Dispatch.Instances<IQteListener>())
                listener.OnPreQteSucceeded(ref reward, ref cancel);
            if (cancel)
                return false;

            index = reward.Index;
            mustSuccess = reward.MustSuccess;
            return true;
        }

        private static void Postfix(QTERewardManager __instance, int index, bool mustSuccess) =>
            Dispatch.Run<IQteListener>(listener =>
                listener.OnQteSucceeded(new QteReward(index, mustSuccess, DecideBuff(__instance, index))));
    }

    // Slot 0/1/2 map to the three named buffs and slot 3 to fever on the Mystia reward set; a random slot
    // (-1) or any other reward set is not decidable from the index alone, so the field stays null.
    private static RewardBuffKind? DecideBuff(QTERewardManager manager, int index)
    {
        if (manager is null || manager.CurrentBuffReward is not MystiaQTEBuffReward)
            return null;

        return index switch
        {
            0 => RewardBuffKind.InstantEvaluation,
            1 => RewardBuffKind.PatientFreeze,
            2 => RewardBuffKind.ThrowDeliver,
            3 => RewardBuffKind.Fever,
            _ => null,
        };
    }

    [HarmonyPatch(typeof(MystiaQTEBuffReward), nameof(MystiaQTEBuffReward.Player_ThrowDeliver))]
    private static class ThrowDeliverBuff
    {
        private static void Postfix() =>
            Dispatch.Run<IQteListener>(listener => listener.OnRewardBuffTriggered(RewardBuffKind.ThrowDeliver));
    }

    [HarmonyPatch(typeof(MystiaQTEBuffReward), nameof(MystiaQTEBuffReward.Player_InstantEvaluation))]
    private static class InstantEvaluationBuff
    {
        private static void Postfix() =>
            Dispatch.Run<IQteListener>(listener => listener.OnRewardBuffTriggered(RewardBuffKind.InstantEvaluation));
    }

    [HarmonyPatch(typeof(MystiaQTEBuffReward), nameof(MystiaQTEBuffReward.Player_PatientFreeze))]
    private static class PatientFreezeBuff
    {
        private static void Postfix() =>
            Dispatch.Run<IQteListener>(listener => listener.OnRewardBuffTriggered(RewardBuffKind.PatientFreeze));
    }

    [HarmonyPatch(typeof(MystiaQTEBuffReward), nameof(MystiaQTEBuffReward.Player_Fever))]
    private static class FeverBuff
    {
        private static void Postfix() =>
            Dispatch.Run<IQteListener>(listener => listener.OnRewardBuffTriggered(RewardBuffKind.Fever));
    }

    [HarmonyPatch(typeof(MystiaQTEBuffReward), nameof(MystiaQTEBuffReward.Player_Fever_Infinite))]
    private static class InfiniteFeverBuff
    {
        private static void Postfix() =>
            Dispatch.Run<IQteListener>(listener => listener.OnRewardBuffTriggered(RewardBuffKind.InfiniteFever));
    }
}
