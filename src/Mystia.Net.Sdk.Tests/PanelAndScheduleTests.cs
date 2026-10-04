using System.Reflection;
using Common.TimelineExtestion;
using Common.UI.NoteBookUtility;
using GameData.Core.Collections;
using GameData.Core.Collections.CharacterUtility;
using GameData.Profile;
using GameData.RunTime.Common;
using HarmonyLib;
using Mystia;
using Mystia.Listeners;
using Mystia.Modding.Bridge;
using Mystia.Scenes;
using NightScene.GuestManagementUtility;
using UnityEngine;
using Xunit;

namespace Mystia.Tests;

// The listener pipelines read the registry BridgeInstaller hands out, a process wide static, so this class must
// not run in parallel with the other suites that bind one (the xunit default parallelises test classes).
[CollectionDefinition("Panel and schedule interceptions", DisableParallelization = true)]
public sealed class PanelAndScheduleCollection
{
}

/// <summary>
/// The serve panel's deferred callbacks, the night scene's fast forward, the node reward and the Reimu protection
/// window: who is asked, in which order, what a verdict does to the listeners after it, and which member each
/// seam actually binds. The dispatch is the framework's own pure code, so none of this needs the game running;
/// the members themselves are read off the interop the bridge compiles against.
/// </summary>
[Collection("Panel and schedule interceptions")]
public sealed class PanelAndScheduleTests : IDisposable
{
    private readonly ModRegistry _registry = new();

    public PanelAndScheduleTests() => BridgeInstaller.Bind(_registry);

    public void Dispose() => BridgeInstaller.Bind(null);

    // One opening of the serve panel. The order stays null here: the pipeline is what this suite exercises, and
    // the scope is never dereferenced for it.
    private static ServeCallbackView Callbacks() => new(null!, null!);

    // ---- the serve panel's deferred callbacks ---------------------------------------------------------

    [Fact]
    public void EveryListenerSeesTheCallbacksOfOneServePanelOpening()
    {
        var trace = new List<string>();
        _registry.Add<IWorkListener>(new CallbackListener(trace, "first"));
        _registry.Add<IWorkListener>(new CallbackListener(trace, "second"));

        ServeCallbackPipeline.Registered(Callbacks());

        Assert.Equal(new[] { "first:registered", "second:registered" }, trace);
    }

    [Fact]
    public void ACancelledServeCallbackIsDroppedAndTheNextListenerStillSeesIt()
    {
        var trace = new List<string>();
        var cancelling = new CallbackListener(trace, "first").Cancel(ServeCallbackKind.OrderEvaluate);
        var later = new CallbackListener(trace, "second");
        _registry.Add<IWorkListener>(cancelling);
        _registry.Add<IWorkListener>(later);

        var ran = ServeCallbackPipeline.Allowed(Callbacks(), ServeCallbackKind.OrderEvaluate);

        Assert.False(ran);
        // Cancelling never hides the callback from the listeners after the one that cancelled, and each of them
        // sees the verdict the previous one left behind.
        Assert.Equal(new[] { "first:OrderEvaluate", "second:OrderEvaluate" }, trace);
        Assert.False(cancelling.SawCancel);
        Assert.True(later.SawCancel);
    }

    [Fact]
    public void EachDeferredCallbackOfAnOpeningIsJudgedOnItsOwn()
    {
        var trace = new List<string>();
        _registry.Add<IWorkListener>(
            new CallbackListener(trace, "first").Cancel(ServeCallbackKind.FoodDeliverStatusUpdated));

        Assert.False(ServeCallbackPipeline.Allowed(Callbacks(), ServeCallbackKind.FoodDeliverStatusUpdated));
        Assert.True(ServeCallbackPipeline.Allowed(Callbacks(), ServeCallbackKind.BeverageDeliverStatusUpdated));
        Assert.True(ServeCallbackPipeline.Allowed(Callbacks(), ServeCallbackKind.OrderEvaluate));
        Assert.True(ServeCallbackPipeline.Allowed(Callbacks(), ServeCallbackKind.PatientRecover));
        Assert.Equal(4, trace.Count);
    }

    [Fact]
    public void AScopeCoversOnlyTheOpeningItWasRegisteredFor()
    {
        // The use case the scope exists for: the callbacks of the order the panel was opened for are dropped
        // once that order moved on, while the same callbacks of the opening that is current run as usual.
        var stale = Callbacks();
        var current = Callbacks();
        var trace = new List<string>();
        _registry.Add<IWorkListener>(new StaleOpeningListener(trace, stale));

        Assert.False(ServeCallbackPipeline.Allowed(stale, ServeCallbackKind.OrderEvaluate));
        Assert.True(ServeCallbackPipeline.Allowed(current, ServeCallbackKind.OrderEvaluate));
        Assert.Equal(new[] { "stale", "current" }, trace);
    }

    // ---- the night scene's fast forward ---------------------------------------------------------------

    [Fact]
    public void EveryWorkUiListenerIsAskedBeforeTheNightFastForward()
    {
        var trace = new List<string>();
        _registry.Add<IWorkUiListener>(new FastForwardListener(trace, "first"));
        _registry.Add<IWorkUiListener>(new FastForwardListener(trace, "second").Cancel());

        var allowed = WorkFastForwardSeams.Allowed();

        Assert.False(allowed);
        Assert.Equal(new[] { "first", "second" }, trace);
    }

    [Fact]
    public void TheNightFastForwardIsItsOwnMemberBesideTheDayOne()
    {
        // Two scenes, two members on two interfaces: a mod can be asked about the night's fast forward without
        // being asked about the day's, which is why the work one is not a second member on IDayUiListener.
        Assert.NotNull(typeof(IWorkUiListener).GetMethod(nameof(IWorkUiListener.OnPreFastForward)));
        var day = typeof(IDayUiListener).GetMethod(nameof(IDayUiListener.OnPreFastForward));

        Assert.NotNull(day);
        Assert.Equal(typeof(IDayUiListener), day!.DeclaringType);
    }

    // ---- the node reward ------------------------------------------------------------------------------

    [Fact]
    public void EveryListenerSeesTheRewardAndTheLastVerdictDecides()
    {
        var trace = new List<string>();
        var keeping = new RewardListener(trace, "first");
        _registry.Add<IScheduleListener>(keeping);
        _registry.Add<IScheduleListener>(new RewardListener(trace, "second"));

        SchedulerNode.Reward reward = null!;
        var allowed = SchedulePipeline.AllowReward(ref reward);

        Assert.True(allowed);
        Assert.Equal(new[] { "first", "second" }, trace);
        Assert.False(keeping.SawCancel);
    }

    [Fact]
    public void ADroppedRewardIsStoppedBeforeTheGameProcessesIt()
    {
        var trace = new List<string>();
        _registry.Add<IScheduleListener>(new RewardListener(trace, "first").Cancel());
        _registry.Add<IScheduleListener>(new RewardListener(trace, "second"));

        SchedulerNode.Reward reward = null!;
        var allowed = SchedulePipeline.AllowReward(ref reward);

        Assert.False(allowed);
        // The reward travels by ref, so the listener that dropped it can hand the same one to
        // IDaySceneScheduleServices.ReplayReward afterwards; the reward itself is untouched by the pipeline.
        Assert.Null(reward);
    }

    [Fact]
    public void TheRewardInterceptionCarriesTheRewardItDrops()
    {
        // The gap this closes: without the reward in the notification a listener cannot tell which reward it is
        // asked about, and therefore cannot replay it either.
        var member = typeof(IScheduleListener).GetMethod(nameof(IScheduleListener.OnPreRewardProcessed));

        Assert.NotNull(member);
        var parameters = member!.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(SchedulerNode.Reward).MakeByRefType(), parameters[0].ParameterType);
        Assert.Equal(typeof(bool).MakeByRefType(), parameters[1].ParameterType);
    }

    // ---- the Reimu protection window -----------------------------------------------------------------

    [Fact]
    public void EveryListenerIsToldWhenTheProtectionWindowOpensAndCloses()
    {
        var trace = new List<string>();
        _registry.Add<IScheduleListener>(new WindowListener(trace, "first"));
        _registry.Add<IScheduleListener>(new WindowListener(trace, "second"));

        SchedulePipeline.EnterReimuProtection();
        SchedulePipeline.ExitReimuProtection();

        Assert.Equal(new[] { "first:enter", "second:enter", "first:exit", "second:exit" }, trace);
    }

    // ---- what the seams bind -------------------------------------------------------------------------

    [Fact]
    public void TheWindowSeamBindsTheNamedLocalFunctionAfterCheckingIt()
    {
        var window = AccessTools.Method(typeof(RunTimeScheduler), ReimuProtectionWindow.MemberName);

        // The interop member the seam patches, in the shape of the game's own local function
        // void ReimuProtection(Action onFinish) inside AddReimuPositiveSpellToWorkScene.
        Assert.NotNull(window);
        Assert.True(window!.IsStatic);
        Assert.Equal(typeof(void), window.ReturnType);
        Assert.Equal(typeof(Il2CppSystem.Action), Assert.Single(window.GetParameters()).ParameterType);

        // The name the game's own source gives that member. A build that numbers the local functions differently
        // is exactly what the startup check refuses.
        Assert.Equal("<AddReimuPositiveSpellToWorkScene>g__ReimuProtection|160_0", ReimuProtectionWindow.NativeName);
    }

    [Fact]
    public void TheWindowSeamValidatesItsMemberBeforeItIsInstalled()
    {
        var container = typeof(ReimuProtectionWindow).GetNestedType("Window", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The Reimu window patch class is gone.");

        // A missing landing point has to be reported at patch time instead of patching something else, which is
        // what this Prepare is for and what the installer logs.
        var prepare = container.GetMethod("Prepare", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(prepare);
        Assert.NotNull(prepare!.GetCustomAttribute<HarmonyPrepare>());

        var patch = Assert.Single(container.GetCustomAttributes<HarmonyPatch>());
        Assert.Equal(typeof(RunTimeScheduler), patch.info.declaringType);
        Assert.Equal(ReimuProtectionWindow.MemberName, patch.info.methodName);
    }

    [Fact]
    public void ALocatedMemberMustBeTheOneTheSeamNames()
    {
        var member = nameof(Probe);

        // The check itself: the member the interop name reaches is only accepted when the game's own name for it
        // is the expected one.
        Assert.Equal(member, NamedSeams.Locate(typeof(PanelAndScheduleTests), member, "Game::Local", _ => "Game::Local").Name);

        var renamed = Assert.Throws<InvalidOperationException>(
            () => NamedSeams.Locate(typeof(PanelAndScheduleTests), member, "Game::Local", _ => "Game::Other"));
        Assert.Contains("Game::Other", renamed.Message);
        Assert.Contains("Game::Local", renamed.Message);

        Assert.Throws<MissingMethodException>(
            () => NamedSeams.Locate(typeof(PanelAndScheduleTests), member, "Game::Local", _ => null));
        Assert.Throws<MissingMethodException>(
            () => NamedSeams.Locate(typeof(PanelAndScheduleTests), "NoSuchSeamMember", "Game::Local", _ => "Game::Local"));
    }

    [Fact]
    public void AWindowThatRefusesItsLocationIsNeverInstalled()
    {
        // The convention the window seam leans on, checked here because the game is what is missing: Harmony runs
        // the patch class's Prepare before it applies anything, so a landing point that cannot be verified is
        // reported and the seam stays uninstalled instead of patching whatever answers to the interop name.
        var harmony = new Harmony("dev.mystia.modding.bridge.tests");

        Assert.Throws<HarmonyException>(() => new PatchClassProcessor(harmony, typeof(RefusingWindow)).Patch());

        RefusingWindow.Target();
        Assert.False(RefusingWindow.Ran);
    }

    [HarmonyPatch(typeof(RefusingWindow), nameof(RefusingWindow.Target))]
    private static class RefusingWindow
    {
        internal static bool Ran;

        internal static void Target()
        {
        }

        [HarmonyPrepare]
        private static void Prepare() =>
            throw new InvalidOperationException("the landing point is not this member");

        [HarmonyPrefix]
        private static void Prefix() => Ran = true;
    }

    [Fact]
    public void EveryPrefixOfTheNewSeamsBindsByNameAgainstItsTarget()
    {
        // Harmony binds a prefix argument by name; a name the target does not have keeps the whole patch from
        // installing, and the installer only logs that. Every prefix this work added is checked here instead.
        Binds(
            typeof(ServeCallbackSeams),
            "CallbacksRegistering",
            typeof(NightScene.UI.WorkSceneSustainedPannel),
            nameof(NightScene.UI.WorkSceneSustainedPannel.OpenServePanel));
        Binds(
            typeof(WorkFastForwardSeams),
            "Submitting",
            typeof(NightScene.UI.WorkSceneSustainedPannel),
            nameof(NightScene.UI.WorkSceneSustainedPannel.OnFastForwardSubmit));
        Binds(typeof(ScheduleSeams), "Rewarding", typeof(RunTimeScheduler), nameof(RunTimeScheduler.ProcessReward));
        Binds(typeof(ReimuProtectionWindow), "Window", typeof(RunTimeScheduler), ReimuProtectionWindow.MemberName, "Enter");
        Binds(typeof(ReimuProtectionWindow), "Window", typeof(RunTimeScheduler), ReimuProtectionWindow.MemberName, "Exit");
    }

    // ---- the note book's portrait ---------------------------------------------------------------------

    [Fact]
    public void TheNoteBookProfilePageIsCoveredByThePortraitProviderSeam()
    {
        // The note book's profile page hands its own mystiaPic to DataBaseCharacter.SetupPortrayalVisual inside
        // OnPanelOpen (game source: NoteBookProfilePannel.cs:72), the very method the provider seam is patched
        // on, so an IPortraitProvider covers the page as it covers the day HUD and no note book portrait
        // interface is needed.
        Assert.NotNull(AccessTools.Method(typeof(NoteBookProfilePannel), nameof(NoteBookProfilePannel.OnPanelOpen)));

        var container = typeof(PortraitProviderSeams).GetNestedType("Setup", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The portrait provider seam is gone.");
        var patch = Assert.Single(container.GetCustomAttributes<HarmonyPatch>());

        Assert.Equal(typeof(DataBaseCharacter), patch.info.declaringType);
        Assert.Equal(nameof(DataBaseCharacter.SetupPortrayalVisual), patch.info.methodName);

        // The panel that asks travels with the call (the method takes it as its coroutine runner), and the seam
        // reads it to tell the note book apart: the prefix must therefore ask for it.
        var prefix = container.GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("The portrait provider seam has no prefix.");
        Assert.Contains(prefix.GetParameters(), parameter => parameter.ParameterType == typeof(MonoBehaviour));
    }

    // ---- helpers --------------------------------------------------------------------------------------

    private void Binds(Type seam, string patchClass, Type targetType, string targetName, string patchMethod = "Prefix")
    {
        var container = seam.GetNestedType(patchClass, BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"{seam.Name}.{patchClass} is gone.");
        var method = container.GetMethod(patchMethod, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"{seam.Name}.{patchClass}.{patchMethod} is gone.");
        var target = AccessTools.Method(targetType, targetName);

        Assert.NotNull(target);
        foreach (var parameter in method.GetParameters())
        {
            // Harmony's own injections.
            if (parameter.Name!.StartsWith("__", StringComparison.Ordinal))
                continue;

            var bound = target!.GetParameters().FirstOrDefault(candidate => candidate.Name == parameter.Name);
            Assert.True(
                bound is not null,
                $"{seam.Name}.{patchClass}.{patchMethod} binds '{parameter.Name}', which {targetType.Name}.{targetName} does not carry.");
            // A by ref binding of an argument the game passes by value is how a prefix rewrites it.
            Assert.Equal(Plain(bound!.ParameterType), Plain(parameter.ParameterType));
        }
    }

    private static Type Plain(Type type) => type.IsByRef ? type.GetElementType()! : type;

    // A member for NamedSeams.Locate to resolve; it only has to exist and be reachable by name.
    private static void Probe()
    {
    }

    /// <summary>IWorkListener carries the serving panel interceptions as required members; only the callbacks this
    /// suite is about are interesting here, so the stubs live in one place.</summary>
    private abstract class WorkListener : IWorkListener
    {
        public virtual void OnServePanelOpened(ServePannelView view)
        {
        }

        public virtual void OnPreServePanelClosed(ServePannelView view, ref bool cancelInvocation)
        {
        }

        public virtual void OnPreDishServed(ServePannelView view, ref Sellable dish, ref bool cancelInvocation)
        {
        }

        public virtual void OnPreDishCancelled(ServePannelView view, ref Sellable dish, ref bool cancelInvocation)
        {
        }

        public virtual void OnPreStorageExtracted(ref Sellable sellable, ref bool cancelInvocation)
        {
        }

        public virtual void OnPreTimeModeSet(GameTimeManager manager, ref GameTimeManager.TimeMode mode, ref bool cancelInvocation)
        {
        }

        public virtual void OnServeCallbacksRegistered(ServeCallbackView callbacks)
        {
        }

        public virtual void OnPreServeCallback(ServeCallbackView callbacks, ServeCallbackKind kind, ref bool cancelInvocation)
        {
        }
    }

    private sealed class CallbackListener(List<string> trace, string name) : WorkListener
    {
        internal bool SawCancel { get; private set; }

        private ServeCallbackKind? _cancelled;

        internal CallbackListener Cancel(ServeCallbackKind kind)
        {
            _cancelled = kind;
            return this;
        }

        public override void OnServeCallbacksRegistered(ServeCallbackView callbacks) =>
            trace.Add($"{name}:registered");

        public override void OnPreServeCallback(ServeCallbackView callbacks, ServeCallbackKind kind, ref bool cancelInvocation)
        {
            trace.Add($"{name}:{kind}");
            SawCancel = cancelInvocation;
            if (_cancelled == kind)
                cancelInvocation = true;
        }
    }

    private sealed class StaleOpeningListener(List<string> trace, ServeCallbackView stale) : WorkListener
    {
        public override void OnPreServeCallback(ServeCallbackView callbacks, ServeCallbackKind kind, ref bool cancelInvocation)
        {
            trace.Add(ReferenceEquals(callbacks, stale) ? "stale" : "current");
            if (ReferenceEquals(callbacks, stale))
                cancelInvocation = true;
        }
    }

    private sealed class FastForwardListener(List<string> trace, string name) : IWorkUiListener
    {
        private bool _cancel;

        internal FastForwardListener Cancel()
        {
            _cancel = true;
            return this;
        }

        public void OnPreFastForward(ref bool cancelInvocation)
        {
            trace.Add(name);
            cancelInvocation |= _cancel;
        }
    }

    private sealed class RewardListener(List<string> trace, string name) : IScheduleListener
    {
        internal bool SawCancel { get; private set; }

        private bool _cancel;

        internal RewardListener Cancel()
        {
            _cancel = true;
            return this;
        }

        public void OnPreRewardProcessed(ref SchedulerNode.Reward reward, ref bool cancelInvocation)
        {
            trace.Add(name);
            SawCancel = cancelInvocation;
            cancelInvocation |= _cancel;
        }
    }

    private sealed class WindowListener(List<string> trace, string name) : IScheduleListener
    {
        public void OnReimuProtectionEntered() => trace.Add($"{name}:enter");

        public void OnReimuProtectionExited() => trace.Add($"{name}:exit");
    }
}
