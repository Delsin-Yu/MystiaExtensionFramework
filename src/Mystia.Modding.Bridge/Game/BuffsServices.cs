using GameData.CoreLanguage.Collections;
using Il2CppInterop.Runtime;
using Mystia.Scenes;
using NightScene.EventUtility;

namespace Mystia.Modding.Bridge;

/// <summary>
/// Timed buffs of the running work scene, on top of the game's <see cref="EventManager"/> buff table.
/// <para>
/// <c>isPositive</c> has no parameter of its own on the game side, but it is not dropped: it selects the
/// <c>BuffTimeType</c> exactly like the stock <c>SpellBase.RegisterTimedBuff</c> does, so a positive buff runs
/// through <c>RewardCard</c> (level and Murasa bonuses apply) and a negative one through <c>NegativeCard</c>
/// (the duration is used as given).
/// </para>
/// A buff id is declared when its language entry exists - stock ids and ids injected through
/// <c>IDatabaseExtension.OnInjectBuffs</c>. The game reads that entry while registering, so an undeclared id
/// is rejected here instead of throwing from inside the game.
/// </summary>
internal sealed class WorkSceneBuffsServices : IWorkSceneBuffs
{
    internal static readonly WorkSceneBuffsServices Shared = new();

    public bool RegisterTimedBuff(
        int buffType,
        int durationSeconds,
        Action? onBuffEnd = null,
        Func<int, string, string>? description = null,
        bool isPositive = true)
    {
        ServiceScope.Require();
        var type = (EventManager.BuffType)buffType;
        if (!Declared(type))
        {
            GameBridgeHook.Trace($"BuffsServices: buff {buffType} is not declared, RegisterTimedBuff ignored");
            return false;
        }

        // TryOverrideTimedBuff is the game's own "register or extend": an existing buff keeps its elapsed part
        // and gets this duration added, a missing one is registered with the final (possibly bonus adjusted)
        // time. It hands the final seconds to the registering callback.
        var manager = EventManager.Instance;
        var end = Convert(onBuffEnd);
        var register = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<int>>(
            new Action<int>(seconds => Register(manager, type, seconds, end, description)));
        manager.TryOverrideTimedBuff(
            type,
            durationSeconds,
            register,
            isPositive ? EventManager.BuffTimeType.RewardCard : EventManager.BuffTimeType.NegativeCard,
            EventManager.BuffRegisterType.Additive,
            -1,
            null!);
        return true;
    }

    public bool HasTimedBuff(int buffType)
    {
        ServiceScope.Require();
        return EventManager.Instance.CheckTimedBuffExists((EventManager.BuffType)buffType);
    }

    public void ExtendTimedBuff(int buffType, int extraSeconds)
    {
        ServiceScope.Require();
        var manager = EventManager.Instance;
        var type = (EventManager.BuffType)buffType;
        if (!manager.CheckTimedBuffExists(type))
        {
            GameBridgeHook.Trace($"BuffsServices: buff {buffType} is not running, ExtendTimedBuff ignored");
            return;
        }

        manager.SetExtraBuffRemainingTime(type, extraSeconds);
    }

    public void LimitEvalLevel(
        int buffType,
        int durationSeconds,
        int maxEvaluation,
        IReadOnlyList<int>? tags,
        bool food,
        bool containsOrNot,
        Action? onBuffEnd = null,
        Func<int, string, string>? description = null)
    {
        ServiceScope.Require();
        var type = (EventManager.BuffType)buffType;
        if (!Declared(type))
        {
            GameBridgeHook.Trace($"BuffsServices: buff {buffType} is not declared, LimitEvalLevel ignored");
            return;
        }

        var tagIds = new Il2CppSystem.Collections.Generic.List<int>(tags?.Count ?? 0);
        if (tags is not null)
            foreach (var tag in tags)
                tagIds.Add(tag);

        EventManager.Instance.MaxEvalLevelSet(
            durationSeconds,
            maxEvaluation,
            (Il2CppSystem.Collections.Generic.IEnumerable<int>)(object)tagIds,
            out _,
            Convert(onBuffEnd),
            type,
            food,
            Convert(description),
            null!,
            null!,
            containsOrNot);
    }

    private static void Register(
        EventManager manager,
        EventManager.BuffType buffType,
        int seconds,
        Il2CppSystem.Action? onBuffEnd,
        Func<int, string, string>? description) =>
        manager.RegisterTimedBuff(seconds, buffType, out _, onBuffEnd, Convert(description), null!);

    private static bool Declared(EventManager.BuffType buffType) =>
        DataBaseLanguage.BuffDescription?.ContainsKey(buffType) == true;

    private static Il2CppSystem.Action? Convert(Action? callback) =>
        callback is null ? null : DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(callback);

    private static Il2CppSystem.Func<int, string, string>? Convert(Func<int, string, string>? callback) =>
        callback is null ? null : DelegateSupport.ConvertDelegate<Il2CppSystem.Func<int, string, string>>(callback);
}
