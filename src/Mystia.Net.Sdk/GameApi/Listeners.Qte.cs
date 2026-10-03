using Mystia;

namespace Mystia.Listeners;

/// <summary>The five reward buffs a QTE can hand out.</summary>
public enum RewardBuffKind
{
    ThrowDeliver,
    InstantEvaluation,
    PatientFreeze,
    Fever,
    InfiniteFever,
}

/// <summary>
/// One QTE reward slot. <paramref name="Buff"/> is filled in only when the bridge can decide it, so a
/// listener must not depend on it: reward buff triggers are reported through
/// <see cref="IQteListener.OnRewardBuffTriggered"/>.
/// </summary>
public readonly record struct QteReward(int Index, bool MustSuccess, RewardBuffKind? Buff);

[AutoWire]
public interface IQteListener
{
    void OnPreQteSucceeded(ref QteReward reward, ref bool cancelInvocation) { }

    void OnQteSucceeded(in QteReward reward) { }

    void OnRewardBuffTriggered(RewardBuffKind kind) { }
}
