using System;
using GameData.Core.Collections;
using GameData.Profile;
using HarmonyLib;
using Mystia.Listeners;
using Mystia.Scenes;
using NightScene.CookingUtility;
using NightScene.EventUtility;

namespace Mystia.Modding.Bridge;

internal sealed class WorkSceneEconomyServices : IWorkSceneEconomyServices
{
    internal static readonly WorkSceneEconomyServices Shared = new();

    public void EditFund(float value, EventManager.MathOperation operation = EventManager.MathOperation.Add)
    {
        ServiceScope.Require();
        EventManager.Instance.FundEdit(value, operation);
    }

    public void EditTip(
        int value,
        EventManager.ServeType serveType,
        float comboBuff = 0f,
        float moodBuff = 0f,
        float extraBuff = 0f)
    {
        ServiceScope.Require();
        EventManager.Instance.TipEdit(value, serveType, comboBuff, moodBuff, extraBuff);
    }

    public void EditExperience(float value, EventManager.MathOperation operation = EventManager.MathOperation.Add)
    {
        ServiceScope.Require();
        EventManager.Instance.ExpEdit(value, operation);
    }

    public void EditPassion(float value, EventManager.MathOperation operation = EventManager.MathOperation.Add)
    {
        ServiceScope.Require();
        EventManager.Instance.PassionEdit(value, operation);
    }

    public void SetPopularityTagsEnabled(bool enabled)
    {
        ServiceScope.Require();
        BridgeGates.PopularityTags = enabled;
    }
}

// Gates the popularity tag edit: with the switch off Sellable.Tags stops folding the player's like/hate tags
// into the sellable, which is the same shortcut the mod used to run for a room client.
internal static class PopularityTagSeam
{
    [HarmonyPatch(typeof(Sellable), nameof(Sellable.GetPopTag))]
    private static class Edit
    {
        private static bool Prefix(
            Il2CppSystem.Collections.Generic.IEnumerable<int> sourceTag,
            ref Il2CppSystem.Collections.Generic.IEnumerable<int> __result)
        {
            if (StockGate.Allow(BridgeGates.PopularityTags))
                return true;

            __result = sourceTag;
            return false;
        }
    }
}

internal sealed class QteServices : IQteServices
{
    internal static readonly QteServices Shared = new();

    public void ApplyQteReward(in QteReward reward)
    {
        ServiceScope.Require();
        if (reward.Buff is { } kind)
        {
            TriggerRewardBuff(kind, kind == RewardBuffKind.InfiniteFever);
            return;
        }

        QTERewardManager.Instance.OnQTESucceeded(reward.Index, reward.MustSuccess);
    }

    public void TriggerRewardBuff(RewardBuffKind kind, bool infinite = false)
    {
        ServiceScope.Require();
        if (QTERewardManager.Instance.CurrentBuffReward is not MystiaQTEBuffReward reward)
            throw new InvalidOperationException("The running QTE reward set is not the Mystia reward set.");

        switch (kind)
        {
            case RewardBuffKind.ThrowDeliver:
                reward.Player_ThrowDeliver();
                break;
            case RewardBuffKind.InstantEvaluation:
                reward.Player_InstantEvaluation();
                break;
            case RewardBuffKind.PatientFreeze:
                reward.Player_PatientFreeze();
                break;
            case RewardBuffKind.Fever when infinite:
            case RewardBuffKind.InfiniteFever:
                reward.Player_Fever_Infinite();
                break;
            case RewardBuffKind.Fever:
                reward.Player_Fever();
                break;
        }
    }
}
