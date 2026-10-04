using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Il2CppInterop.Common.Attributes;
using NightScene;
using NightScene.CookingUtility;
using NightScene.EventUtility;
using NightScene.GuestManagementUtility;
using NightScene.Tiles;
using NightScene.UI.CookingUtility;
using NightScene.UI.HUDUtility;
using UnityEngine;

using LockLoop = GameData.Profile.YuyukoBossData.__c__DisplayClass16_6.ObjectCompilerGeneratedNPrivateSealedIEnumerator1ObjectIEnumeratorIDisposableInObSpCoObObUnique;
using OnFailLoop = GameData.Profile.YuyukoBossData.__c__DisplayClass16_0.ObjectCompilerGeneratedNPrivateSealedIEnumerator1ObjectIEnumeratorIDisposableInObObObUnique;
using Phase1SpawnLoop = GameData.Profile.YuyukoBossData.__c__DisplayClass16_0.ObjectCompilerGeneratedNPrivateSealedIEnumerator1ObjectIEnumeratorIDisposableInObWaVoObMoInVoBoOb0;
using Phase2SpawnLoop = GameData.Profile.YuyukoBossData.__c__DisplayClass16_0.ObjectCompilerGeneratedNPrivateSealedIEnumerator1ObjectIEnumeratorIDisposableInObWaVoObMoInVoBoOb1;
using Phase3SpawnLoop = GameData.Profile.YuyukoBossData.__c__DisplayClass16_6.ObjectCompilerGeneratedNPrivateSealedIEnumerator1ObjectIEnumeratorIDisposableInObWaVoObMoInVoBoOb0;
using PhaseClock = GameData.Profile.YuyukoBossData.__c__DisplayClass16_0.ObjectCompilerGeneratedNPrivateSealedIEnumerator1ObjectIEnumeratorIDisposableInObFu1BoexSiInObObUnique;
using Retake = GameData.Profile.YuyukoBossData.__c__DisplayClass16_6;
using RetakeHelper = GameData.Profile.YuyukoBossData.__c__DisplayClass16_7;
using RunLoop = GameData.Profile.YuyukoBossData._MainChallengeLoop_d__16;
using StandContext = GameData.Profile.YuyukoBossData.__c__DisplayClass16_9;
using StoryContext = GameData.Profile.YuyukoBossData.__c__DisplayClass16_0;

namespace Mystia.Modding.Bridge;

/// <summary>
/// The startup check of every member the challenge seams hook.
/// <para>
/// The seams name compiler generated members, and those names carry the build the interop was generated from -
/// the mangled type name says which closure and which state machine it is, and inside a closure the sanitised
/// member names are numbered in the order the generator met them. A member that moved build therefore keeps
/// compiling while it means something else, so each hooked target is looked up at startup and each hooked
/// compiler generated type is checked against the one mangled name the seam means; a missing member or a type
/// that belongs to another build is an error, thrown before any seam is applied.
/// </para>
/// <para>
/// Nothing here touches the IL2CPP runtime: the checks are reflection over the generated assembly's metadata, so
/// they run with the assembly and before the game is touched.
/// </para>
/// </summary>
internal static class ChallengeTargets
{
    // The check must run when the bridge loads, before any seam is applied, which is exactly what a module
    // initializer is for; the analyser's advice about application code does not apply to a plugin assembly.
#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void VerifyAtStartup() => Verify();
#pragma warning restore CA2255

    /// <summary>
    /// Checks every hooked target. Throws <see cref="InvalidOperationException"/> naming the first target that is
    /// missing or belongs to a build the seams do not know.
    /// </summary>
    internal static void Verify()
    {
        NamedType("the run's shared closure", typeof(StoryContext), "GameData.Profile.YuyukoBossData+<>c__DisplayClass16_0");
        NamedType("the retake's closure", typeof(Retake), "GameData.Profile.YuyukoBossData+<>c__DisplayClass16_6");
        NamedType("the retake's cooker helper", typeof(RetakeHelper), "GameData.Profile.YuyukoBossData+<>c__DisplayClass16_7");
        NamedType(
            "the stand's evaluation closure",
            typeof(StandContext),
            "GameData.Profile.YuyukoBossData+<>c__DisplayClass16_9");
        NamedType("the main loop", typeof(RunLoop), "GameData.Profile.YuyukoBossData+<MainChallengeLoop>d__16");
        NamedType(
            "the phase clock",
            typeof(PhaseClock),
            "GameData.Profile.YuyukoBossData+<>c__DisplayClass16_0+<<MainChallengeLoop>g__Timing|2>d");
        NamedType(
            "the failure story",
            typeof(OnFailLoop),
            "GameData.Profile.YuyukoBossData+<>c__DisplayClass16_0+<<MainChallengeLoop>g__OnFail|4>d");
        NamedType(
            "phase one's guest spawn loop",
            typeof(Phase1SpawnLoop),
            "GameData.Profile.YuyukoBossData+<>c__DisplayClass16_0+<<MainChallengeLoop>g__Phase1GuestSpawnLoop|7>d");
        NamedType(
            "phase two's guest spawn loop",
            typeof(Phase2SpawnLoop),
            "GameData.Profile.YuyukoBossData+<>c__DisplayClass16_0+<<MainChallengeLoop>g__Phase2GuestSpawnLoop|15>d");
        NamedType(
            "phase three's stand spawn loop",
            typeof(Phase3SpawnLoop),
            "GameData.Profile.YuyukoBossData+<>c__DisplayClass16_6+<<MainChallengeLoop>g__Phase3GuestSpawnLoop|43>d");
        NamedType(
            "the retake's cooker swallow",
            typeof(LockLoop),
            "GameData.Profile.YuyukoBossData+<>c__DisplayClass16_6+<<MainChallengeLoop>g__LockCookersYuyuko|41>d");

        Field("the run's retake flag", typeof(RunLoop), "_isRetake_5__2");
        Field("the run's data", typeof(RunLoop), "__4__this");
        Field("the challenge data's phase length", typeof(GameData.Profile.YuyukoBossData), "singleRoundDuration");
        Field("the run's shared closure", typeof(RunLoop), "__8__1");
        Field("the run's retake closure", typeof(RunLoop), "__8__3");
        Field("the boss's life", typeof(StoryContext), "yuyukoTotalLife");
        Field("the run's positive spell count", typeof(StoryContext), "positiveSpellCount");
        Field("the third phase's panel", typeof(StoryContext), "statusDisplayer");
        Field("the event manager", typeof(StoryContext), "eventManager");
        Field("the boss's order flag", typeof(Retake), "ifYuyukoCouldOrder");
        Field("the picked cooker's position", typeof(RetakeHelper), "cookerPosition");

        Method("the phase clock's step", typeof(PhaseClock), "MoveNext");
        Method("the failure story's step", typeof(OnFailLoop), "MoveNext");
        Method("phase one's spawn step", typeof(Phase1SpawnLoop), "MoveNext");
        Method("phase two's spawn step", typeof(Phase2SpawnLoop), "MoveNext");
        Method("phase three's spawn step", typeof(Phase3SpawnLoop), "MoveNext");
        Method("the retake's swallow step", typeof(LockLoop), "MoveNext");
        Method(
            "the story attempt's evaluation callback",
            typeof(StoryContext),
            ChallengeEvaluationSeams.MemberName,
            typeof(GuestGroupController.EvaluationResult),
            EvaluationCallbackParameters);
        Method(
            "the retake's evaluation callback",
            typeof(Retake),
            ChallengeEvaluationSeams.MemberName,
            typeof(GuestGroupController.EvaluationResult),
            EvaluationCallbackParameters);
        Method(
            "the stand's evaluation callback",
            typeof(StandContext),
            ChallengeEvaluationSeams.MemberName,
            typeof(GuestGroupController.EvaluationResult),
            EvaluationCallbackParameters);
        Method("the retake's buff cleanup", typeof(Retake), "Method_Internal_Void_0", typeof(void), Type.EmptyTypes);
        Method("the challenge's boss lookup", typeof(NightSceneDirector), nameof(NightSceneDirector.GetControlled));
        Method("the night scene's leave", typeof(NightSceneDirector), nameof(NightSceneDirector.TryLeaveSession));
        Method(
            "the challenge's close",
            typeof(GuestsManager),
            nameof(GuestsManager.CloseIzakayaAndLeaveChallengeMode));
        Method("the phase panel's context", typeof(IncomeControllerYuyuko), nameof(IncomeControllerYuyuko.SetContext));
        Method(
            "the phase panel's progress",
            typeof(IncomeControllerYuyuko),
            nameof(IncomeControllerYuyuko.SetTargetProgress));
        Method("the cooker lookup", typeof(CookSystemManager), nameof(CookSystemManager.GetCooker), null, new[] { typeof(Vector3Int) });
        Property("the cooker desks", typeof(TileManager), nameof(TileManager.CookerDesks));
        Property("the locked cookers", typeof(EventManager), nameof(EventManager.LockedCookersRaw));
        Method("the cook interrupt", typeof(CookController), nameof(CookController.InterruptCook));
        Property("the cooker's visual", typeof(CookController), nameof(CookController.visual));
        Property("the cooker's desk index", typeof(CookController), nameof(CookController.GridIndex));
        Method("the permanent hide", typeof(CookAnimator), nameof(CookAnimator.HideCookerPermanent));
    }

    /// <summary>
    /// The shape of every one of the three evaluation callbacks: the result it is handed, the group it
    /// evaluates, the combo protection it is handed, and the message and combo protection it hands back. The
    /// three are one local function of one shape, so one array serves all three checks.
    /// </summary>
    private static readonly System.Type[] EvaluationCallbackParameters =
    [
        typeof(GuestGroupController.EvaluationResult),
        typeof(GuestGroupController),
        typeof(bool),
        typeof(string).MakeByRefType(),
        typeof(bool).MakeByRefType(),
    ];

    private static void NamedType(string what, System.Type type, string originalName)
    {
        var attribute = type.GetCustomAttribute<ObfuscatedNameAttribute>();
        if (attribute is null)
            throw new InvalidOperationException($"The challenge timeline's {what} ({type.FullName}) carries no original name.");
        if (attribute.ObfuscatedName != originalName)
        {
            throw new InvalidOperationException(
                $"The challenge timeline's {what} is {attribute.ObfuscatedName}, not {originalName}: the interop was generated " +
                "for another build and the seam would hook the wrong piece.");
        }
    }

    private static void Field(string what, System.Type type, string name)
    {
        if (AccessTools.Field(type, name) is null && AccessTools.Property(type, name) is null)
            Missing(what, type, name, "field");
    }

    private static void Property(string what, System.Type type, string name)
    {
        if (AccessTools.Property(type, name) is null)
            Missing(what, type, name, "property");
    }

    private static void Method(string what, System.Type type, string name) =>
        Method(what, type, name, null, null);

    private static void Method(string what, System.Type type, string name, System.Type? returnType, System.Type[]? parameters)
    {
        var method = AccessTools.Method(type, name, parameters);
        if (method is null)
            Missing(what, type, name, "method");
        if (returnType is not null && method!.ReturnType != returnType)
        {
            throw new InvalidOperationException(
                $"The challenge timeline's {what} ({type.FullName}.{name}) returns {method.ReturnType}, not {returnType}.");
        }
    }

    private static void Missing(string what, System.Type type, string name, string kind) =>
        throw new InvalidOperationException(
            $"The challenge timeline's {what} ({type.FullName}.{name}) is not the {kind} the seam hooks: the interop was " +
            "generated for another build.");
}
