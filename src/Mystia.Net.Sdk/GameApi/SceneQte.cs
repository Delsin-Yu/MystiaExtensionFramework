using Mystia.Listeners;

namespace Mystia.Scenes;

/// <summary>QTE rewards, replayed through the game's own reward path.</summary>
public interface IQteServices
{
    /// <summary>
    /// Applies one reward slot: a non-null <c>Buff</c> triggers that reward buff, otherwise the slot is
    /// replayed as <c>QTERewardManager.OnQTESucceeded(Index, MustSuccess)</c>.
    /// </summary>
    void ApplyQteReward(in QteReward reward);

    /// <summary>Triggers a reward buff directly; <paramref name="infinite"/> only applies to the fever buff.</summary>
    void TriggerRewardBuff(RewardBuffKind kind, bool infinite = false);
}
