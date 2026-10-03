using System.Reflection;
using HarmonyLib;
using Mystia.Scenes;
using NightScene.GuestManagementUtility;

using RetakeContext = GameData.Profile.YuyukoBossData.__c__DisplayClass16_6;
using StandContext = GameData.Profile.YuyukoBossData.__c__DisplayClass16_9;
using StoryContext = GameData.Profile.YuyukoBossData.__c__DisplayClass16_0;

namespace Mystia.Modding.Bridge;

/// <summary>
/// The challenge's own evaluation callbacks: the three guest group evaluations the Yuyuko challenge overrides,
/// which is where the challenge reacts to an order the game scored.
/// <para>
/// The three are one local function of one shape, declared three times in <c>MainChallengeLoop</c> - the story
/// attempt's boss (<c>YuyukoOverrideEvaluationCallback</c>, its own closure), the retake's boss and the stand
/// the retake spawns (<c>GroupOverrideEvaluationCallback</c>, a closure of its own because it captures the
/// group it was spawned for) - and each is installed as a delegate on the group's
/// <c>OverrideEvaluationCallback</c>, which <c>GuestsManager.EvaluateOrder</c> invokes right before it switches
/// on the result. That is why the seam brackets the callback itself: everything a mod wants to see or steer -
/// the result the challenge scores the group with and the life it takes - happens inside it, and the
/// framework's own report of the evaluation comes after it.
/// </para>
/// <para>
/// The interop spells the three the same way (<c>Method_Internal_EvaluationResult_…_0</c>, because the
/// sanitiser names a member by its signature and its position in its own type, and each of the three is the
/// first of its shape in the closure it lives in), so the interop name alone says nothing about which callback
/// a patch reaches. Each seam therefore verifies, at patch time, that the member it patches is the runtime
/// member the game's own source named - the mangled local function name with its ordinal - which is what
/// <see cref="NamedSeams"/> is for; the closure each one lives in is pinned separately by
/// <see cref="ChallengeTargets"/>.
/// </para>
/// </summary>
internal static class ChallengeEvaluationSeams
{
    /// <summary>
    /// The interop name of every one of the three callbacks: the same signature, the same first position in
    /// its own closure, so the same sanitised name.
    /// </summary>
    internal const string MemberName =
        nameof(StoryContext.Method_Internal_EvaluationResult_EvaluationResult_GuestGroupController_Boolean_byref_String_byref_Boolean_0);

    /// <summary>The game's verdict, as the framework states it. The two ladders are the same one.</summary>
    internal static ChallengeEvaluationResult ToSdk(GuestGroupController.EvaluationResult result) => result switch
    {
        GuestGroupController.EvaluationResult.Exbad => ChallengeEvaluationResult.Exbad,
        GuestGroupController.EvaluationResult.Bad => ChallengeEvaluationResult.Bad,
        GuestGroupController.EvaluationResult.Normal => ChallengeEvaluationResult.Normal,
        GuestGroupController.EvaluationResult.Good => ChallengeEvaluationResult.Good,
        GuestGroupController.EvaluationResult.ExGood => ChallengeEvaluationResult.ExGood,
        _ => ChallengeEvaluationResult.Null,
    };

    /// <summary>The framework's verdict, as the game's own callback is handed it.</summary>
    internal static GuestGroupController.EvaluationResult ToGame(ChallengeEvaluationResult result) => result switch
    {
        ChallengeEvaluationResult.Exbad => GuestGroupController.EvaluationResult.Exbad,
        ChallengeEvaluationResult.Bad => GuestGroupController.EvaluationResult.Bad,
        ChallengeEvaluationResult.Normal => GuestGroupController.EvaluationResult.Normal,
        ChallengeEvaluationResult.Good => GuestGroupController.EvaluationResult.Good,
        ChallengeEvaluationResult.ExGood => GuestGroupController.EvaluationResult.ExGood,
        _ => GuestGroupController.EvaluationResult.Null,
    };

    /// <summary>The native group behind a group the game handed to a callback; zero when there is none.</summary>
    internal static nint Pointer(GuestGroupController? group) => group is null ? 0 : group.Pointer;

    /// <summary>
    /// The gate every one of the three seams runs around its callback. The group's pointer names the group
    /// under evaluation; the two values are the ones the callback is handed and are rewritten in place with
    /// what the listeners leave behind. False means the callback must not run, and the group's evaluation then
    /// stands as the listeners left it.
    /// </summary>
    internal static bool Intercept(
        nint group,
        ref GuestGroupController.EvaluationResult lastResult,
        ref bool oldComboProtect,
        out ChallengeBossEvaluation evaluation)
    {
        // No run the framework owns is behind this callback - another challenge's, or the game's own outside a
        // challenge - so nothing is asked and nothing the game handed over is touched.
        var timeline = ChallengeTimeline.Shared;
        if (!timeline.Running)
        {
            evaluation = default;
            return true;
        }

        var result = ToSdk(lastResult);
        evaluation = timeline.InterceptBossEvaluation(group, ChallengePhase.Three, ref result, ref oldComboProtect, out var cancel);
        if (cancel)
            return false;

        lastResult = ToGame(result);
        return true;
    }
}

/// <summary>
/// The story attempt's evaluation callback: the challenge plays its own ladder over the dishes the boss ate, so
/// the result this callback returns is what the boss's order scores with.
/// </summary>
internal static class ChallengeStoryEvaluationSeam
{
    /// <summary>
    /// The name the game's own source gives the callback:
    /// <c>&lt;MainChallengeLoop&gt;g__YuyukoOverrideEvaluationCallback|33</c>, the local function the story
    /// attempt installs on its boss (YuyukoBossData.cs:344). The ordinal belongs to the game build, so the
    /// seam insists on it.
    /// </summary>
    internal const string NativeName = "<MainChallengeLoop>g__YuyukoOverrideEvaluationCallback|33";

    // Resolved and checked by name when this type is first touched, i.e. from the Prepare below. A member whose
    // runtime name is another local function than the one the seam means throws here instead of patching the
    // wrong callback.
    private static readonly MethodInfo Located = NamedSeams.LocateIl2Cpp(typeof(StoryContext), ChallengeEvaluationSeams.MemberName, NativeName);

    [HarmonyPatch(typeof(StoryContext), ChallengeEvaluationSeams.MemberName)]
    private static class Story
    {
        [HarmonyPrepare]
        private static void Prepare() => _ = Located;

        private static bool Prefix(
            ref GuestGroupController.EvaluationResult lastResult,
            GuestGroupController __,
            ref bool oldComboProtect,
            out string message,
            out bool comboProtect,
            ref GuestGroupController.EvaluationResult __result,
            out ChallengeBossEvaluation __state)
        {
            // The game's own callback writes both of these itself; they only stand as written here when the
            // callback is cancelled below and the game is left with nothing else to use.
            message = string.Empty;
            comboProtect = false;

            // The game names this argument `__` in its own source: the story attempt's callback ignores which
            // group it was handed, but EvaluateOrder still passes the group it is evaluating.
            var run = ChallengeEvaluationSeams.Intercept(ChallengeEvaluationSeams.Pointer(__), ref lastResult, ref oldComboProtect, out __state);
            if (run)
                return true;

            __result = ChallengeEvaluationSeams.ToGame(__state.Result);
            comboProtect = __state.ComboProtect;
            return false;
        }

        [HarmonyPostfix]
        private static void Postfix(
            ChallengeBossEvaluation __state,
            GuestGroupController.EvaluationResult __result,
            bool comboProtect,
            bool __runOriginal) =>
            ChallengeTimeline.Shared.BossEvaluated(
                __state,
                ChallengeEvaluationSeams.ToSdk(__result),
                comboProtect,
                __runOriginal);
    }
}

/// <summary>
/// The retake's evaluation callback for its boss: the result the game scored is what the boss loses life over,
/// and a bad dish sends the boss off to lock a cooker.
/// </summary>
internal static class ChallengeRetakeEvaluationSeam
{
    /// <summary>
    /// The name the game's own source gives the callback:
    /// <c>&lt;MainChallengeLoop&gt;g__YuyukoOverrideEvaluationCallback|50</c>, the local function the retake
    /// installs on its boss (YuyukoBossData.cs:565), which the same-signed story attempt callback would answer
    /// to as well if the names were not checked.
    /// </summary>
    internal const string NativeName = "<MainChallengeLoop>g__YuyukoOverrideEvaluationCallback|50";

    private static readonly MethodInfo Located = NamedSeams.LocateIl2Cpp(typeof(RetakeContext), ChallengeEvaluationSeams.MemberName, NativeName);

    [HarmonyPatch(typeof(RetakeContext), ChallengeEvaluationSeams.MemberName)]
    private static class Retake
    {
        [HarmonyPrepare]
        private static void Prepare() => _ = Located;

        private static bool Prefix(
            ref GuestGroupController.EvaluationResult lastResult,
            GuestGroupController thisGuestGroup,
            ref bool oldComboProtect,
            out string message,
            out bool comboProtect,
            ref GuestGroupController.EvaluationResult __result,
            out ChallengeBossEvaluation __state)
        {
            message = string.Empty;
            comboProtect = false;

            var run = ChallengeEvaluationSeams.Intercept(ChallengeEvaluationSeams.Pointer(thisGuestGroup), ref lastResult, ref oldComboProtect, out __state);
            if (run)
                return true;

            __result = ChallengeEvaluationSeams.ToGame(__state.Result);
            comboProtect = __state.ComboProtect;
            return false;
        }

        [HarmonyPostfix]
        private static void Postfix(
            ChallengeBossEvaluation __state,
            GuestGroupController.EvaluationResult __result,
            bool comboProtect,
            bool __runOriginal) =>
            ChallengeTimeline.Shared.BossEvaluated(
                __state,
                ChallengeEvaluationSeams.ToSdk(__result),
                comboProtect,
                __runOriginal);
    }
}

/// <summary>
/// The retake's evaluation callback for the stand it spawned: the stand's own order is scored, the boss loses
/// life over how it scored (and a stand whose guest refused the order sends the boss off to lock a cooker), and
/// the result the callback was handed is the one it hands straight back.
/// </summary>
internal static class ChallengeStandEvaluationSeam
{
    /// <summary>
    /// The name the game's own source gives the callback:
    /// <c>&lt;MainChallengeLoop&gt;g__GroupOverrideEvaluationCallback|70</c>, the local function the retake
    /// installs on every stand it spawns (YuyukoBossData.cs:524).
    /// </summary>
    internal const string NativeName = "<MainChallengeLoop>g__GroupOverrideEvaluationCallback|70";

    private static readonly MethodInfo Located = NamedSeams.LocateIl2Cpp(typeof(StandContext), ChallengeEvaluationSeams.MemberName, NativeName);

    [HarmonyPatch(typeof(StandContext), ChallengeEvaluationSeams.MemberName)]
    private static class Stand
    {
        [HarmonyPrepare]
        private static void Prepare() => _ = Located;

        private static bool Prefix(
            ref GuestGroupController.EvaluationResult lastResult,
            GuestGroupController thisGuestGroup,
            ref bool oldComboProtect,
            out string message,
            out bool comboProtect,
            ref GuestGroupController.EvaluationResult __result,
            out ChallengeBossEvaluation __state)
        {
            message = string.Empty;
            comboProtect = false;

            var run = ChallengeEvaluationSeams.Intercept(ChallengeEvaluationSeams.Pointer(thisGuestGroup), ref lastResult, ref oldComboProtect, out __state);
            if (run)
                return true;

            // The stand's own callback hands the result it was given straight back, so the listeners' verdict is
            // already the one to return; the flag is the one they left behind.
            __result = ChallengeEvaluationSeams.ToGame(__state.Result);
            comboProtect = __state.ComboProtect;
            return false;
        }

        [HarmonyPostfix]
        private static void Postfix(
            ChallengeBossEvaluation __state,
            GuestGroupController.EvaluationResult __result,
            bool comboProtect,
            bool __runOriginal) =>
            ChallengeTimeline.Shared.BossEvaluated(
                __state,
                ChallengeEvaluationSeams.ToSdk(__result),
                comboProtect,
                __runOriginal);
    }
}
