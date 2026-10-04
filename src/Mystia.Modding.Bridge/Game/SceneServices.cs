using System.Reflection;
using Common.TimelineExtestion;
using Il2CppInterop.Runtime;
using Common.UI;
using Common.UI.GlobalMap;
using GameData.Core.Collections;
using GameData.Core.Collections.CharacterUtility;
using GameData.Core.Collections.NightSceneUtility;
using GameData.CoreLanguage.Collections;
using GameData.Profile;
using GameData.RunTime.Common;
using GameData.RunTime.NightSceneUtility;
using HarmonyLib;
using Mystia;
using Mystia.Assets;
using Mystia.Listeners;
using Mystia.Scenes;
using NightScene.CookingUtility;
using NightScene.GuestManagementUtility;
using PrepNightScene.UI;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Mystia.Modding.Bridge;

internal static class GameMembers
{
    internal static void Invoke(object target, string name, params object?[] args)
    {
        var method = AccessTools.Method(target.GetType(), name)
            ?? throw new MissingMethodException(target.GetType().FullName, name);
        method.Invoke(target, args);
    }

    internal static void Set(object target, string name, object? value)
    {
        var field = AccessTools.Field(target.GetType(), name)
            ?? throw new MissingFieldException(target.GetType().FullName, name);
        field.SetValue(target, value);
    }
}

// The always available half of the host capabilities: global loops and scene loops both see this instance, so
// nothing here asks for the scene scope (the scene scoped actions live on PresentationServices).
internal sealed class CommonServices : ICommonServices
{
    internal static readonly CommonServices Shared = new();

    // The host injects its queued scheduler before mods load; a host that never installed one (the test
    // host) falls back to an inline one, so the member stays callable instead of being null.
    public IMainThreadScheduler MainThread => BridgeInstaller.MainThread ?? (IMainThreadScheduler)InlineMainThread.Shared;

    // The process dispatcher: a mod's routines are not owned by a scene, so they outlive a scene change.
    public ICoroutineDispatcher Coroutines => CoroutineScheduler.Global;

    // The clock is the one capability a background thread may read, so its value is published by the main
    // thread pump every frame instead of being read from the engine on demand.
    public IClock Clock => HostClock.Shared;

    public IPlatformInfo Platform => PlatformInfo.Shared;

    // The engine's input state is main thread only, which is where a loop polls it.
    public IInputServices Input => KeyboardServices.Shared;

    public void LoadScene(Scene scene)
    {
        UniversalGameManager.LoadScene(scene);
    }

    public IDialogCatalog Dialogs => DialogCatalog.Shared;

    public IGuestRecords Records => GuestRecords.Shared;

    // The game's own character operations: it keeps its scene director and its day scene character table for the
    // whole process, so they are reached from a global loop, a console command and a scene loop alike.
    public ICharacterServices Characters => CharacterServices.Shared;

    // Both are process wide: the factory builds engine objects a mod then owns, and the locator files them
    // into the one asset pipeline the game itself loads through.
    public IAssetFactory Assets => UnityAssetFactory.Shared;

    public IAssetLocator Locator => AssetLocator.Shared;

    /// <summary>
    /// The game data objects a mod ships (dialog packages and scheduler nodes). The builder creates engine
    /// scriptables, so it is main thread only, exactly like the mod's own build step used to be.
    /// </summary>
    public IGameDataBuilder DataObjects => GameDataBuilders.Builder;

    // The map builder belongs to the day scene path, but what a mod does with it - describing a map while the
    // databases are collected and publishing it after they initialized - is not scene scoped, so it sits with
    // the assets rather than behind a scene loop.
    public IDayMapBuilder MapBuilder => AssetDayMapBuilder.Shared;

    public void OpenDialog(DialogPackage dialog, Action onFinished)
    {
        UniversalGameManager.OpenDialogMenu(dialog, onFinished);
    }

    public void OpenDialog(
        DialogPackage dialog,
        Action onFinished,
        Action<Il2CppSystem.Collections.Generic.Dictionary<int, string>>? replaceText)
    {
        if (replaceText is null)
        {
            UniversalGameManager.OpenDialogMenu(dialog, onFinished);
            return;
        }

        var callback = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<Il2CppSystem.Collections.Generic.Dictionary<int, string>>>(replaceText);
        UniversalGameManager.OpenDialogMenu(dialog, onFinished, callback);
    }

    public void FadeIn(Action onFinished)
    {
        UniversalGameManager.FadeIn(onFinished);
    }

    public void FadeOut(Action onFinished)
    {
        UniversalGameManager.FadeOut(onFinished);
    }

    public void SetInputEnabled(bool enabled)
    {
        UniversalGameManager.UpdatePlayerInputAvailability(enabled);
    }

    // The game never writes this switch, so the property is the only owner of it. Without an event system
    // present (outside a UI built scene, in a test host) reading answers the engine's own default and writing
    // has no target, which is not an error.
    public bool UiNavigationEnabled
    {
        get => EventSystem.current is not { } system || system.sendNavigationEvents;
        set
        {
            if (EventSystem.current is { } system)
                system.sendNavigationEvents = value;
        }
    }

    // Only a browser URL is handed to the OS: anything else could run a local path or a scheme the platform
    // resolves to something the mod did not mean.
    public void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("Only an absolute http or https URL can be opened.", nameof(url));
        }

        Application.OpenURL(url);
    }

    public void SetNightTransitionEnabled(bool enabled)
    {
        StockGate.TransitionDialog = enabled;
    }

    // The game's own story director is the object every story animation plays on (the day scene's events, a
    // cutscene), and "delayed" is a director that was told to play and has not started yet. It is not the
    // game's separate "an event is running" flag, which its own event bookkeeping drives.
    public bool IsStoryPlaying
    {
        get
        {
            var director = Common.SceneDirector.instance?.playableDirector;
            return director != null
                && director.state is UnityEngine.Playables.PlayState.Playing or UnityEngine.Playables.PlayState.Delayed;
        }
    }

    // The dialog panel on top of the game's own panel stack, if that is what is on top: the game's own interrupt
    // entry only sets the panel's fast-forward flag, and the input event it takes is never read, so nothing here
    // builds one.
    public bool InterruptDialog()
    {
        var stacks = DEYU.AdpUISystem.Managers.AdpUIPanelManager.Instance?.m_PanelStack;
        if (stacks is null || stacks.Count == 0)
            return false;

        var panels = stacks.Peek();
        if (panels is null || panels.Count == 0)
            return false;

        if (panels.Peek()?.ControlledPanel is not Common.DialogUtility.DialogPannel dialog)
            return false;

        dialog.InterruptDialog(default);
        return true;
    }

    // The chat selection panel is the day scene's, but the game invokes a menu entry's action outside every scene
    // loop window, and opening the next menu is exactly what such an action does, so the panel sits with the
    // always available capabilities instead of behind a scene.
    public IChatSelectionServices ChatSelection => ChatSelectionServices.Shared;

    // The language tables are global data, not scene state, so these two read them without the scene scope.
    public string FoodTagText(int tagId) => DataBaseLanguage.GetFoodTag(tagId);

    public string EvaluationText(int evaluation) => DataBaseLanguage.GetEvalText(evaluation);
}

internal sealed class SplashSceneServices : ISplashSceneServices
{
    internal static readonly SplashSceneServices Shared = new();

    public ICommonServices Common => CommonServices.Shared;

    public IPresentationServices Presentation => PresentationServices.Shared;
}

internal sealed class MainSceneServices : IMainSceneServices
{
    internal static readonly MainSceneServices Shared = new();

    public IMainSceneSessionServices Session { get; } = new SessionServices();

    public ICommonServices Common => CommonServices.Shared;

    public IPresentationServices Presentation => PresentationServices.Shared;

    private sealed class SessionServices : IMainSceneSessionServices
    {
        public void GotoDay()
        {
            ServiceScope.Require();
            MainScene.SceneManager.instance.GotoDayScene(
                DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(new Action(static () => { })));
        }
    }
}

internal sealed class DaySceneServices : IDaySceneServices
{
    internal static readonly DaySceneServices Shared = new();

    public IDaySceneMapServices Map { get; } = new MapServices();

    public IDaySceneScheduleServices Schedule { get; } = new ScheduleServices();

    public IDaySceneGuestServices Guests { get; } = new GuestServices();

    public IDaySceneInputServices Input { get; } = new InputServices();

    public ICommonServices Common => CommonServices.Shared;

    public IPresentationServices Presentation => PresentationServices.Shared;

    private sealed class MapServices : IDaySceneMapServices
    {
        public void RefreshSpawnMarkers()
        {
            ServiceScope.Require();
            SpawnMarkerPipeline.RefreshCurrent();
        }

        public void Swap(string mapLabel, string markerName, int travelCount, Action? onFinished = null)
        {
            ServiceScope.Require();
            var manager = DayScene.SceneManager.instance;
            if (onFinished is null)
            {
                manager.SwapMap(mapLabel, markerName, travelCount);
                return;
            }

            // The game only ever passes its own callback in, so ours goes in as onSwapFinish and every
            // other argument keeps the defaults the three argument call produces.
            manager.SwapMap(
                mapLabel,
                markerName,
                travelCount,
                onSwapFinish: DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(onFinished));
        }
    }

    private sealed class ScheduleServices : IDaySceneScheduleServices
    {
        public void SetEndEnabled(bool enabled)
        {
            ServiceScope.Require();
            StockGate.DayEnd = enabled;
        }

        public void End()
        {
            ServiceScope.Require();
            StockGate.Bypass(() => GameMembers.Invoke(DayScene.SceneManager.instance, "OnDayOver"));
        }

        public void Chat(string characterLabel)
        {
            ServiceScope.Require();
            DayScene.SceneManager.instance.Chat(characterLabel, false);
        }

        public void ReplayReward(in SchedulerNode.Reward reward)
        {
            ServiceScope.Require();
            var value = reward;
            StockGate.Bypass(() => RunTimeScheduler.ProcessReward(value));
        }
    }

    private sealed class GuestServices : IDaySceneGuestServices
    {
        public void RecordInvited(int id)
        {
            ServiceScope.Require();
            StatusTracker.Instance.RecordInvitedGuest(id);
        }
    }

    private sealed class InputServices : IDaySceneInputServices
    {
        public void SetMoveEnabled(bool enabled)
        {
            ServiceScope.Require();
            StockGate.Move = enabled;
        }

        public void SetSprintEnabled(bool enabled)
        {
            ServiceScope.Require();
            StockGate.Sprint = enabled;
        }

        public void SetInteractEnabled(bool enabled)
        {
            ServiceScope.Require();
            StockGate.Interact = enabled;
        }
    }
}

internal static class PrepPanels
{
    internal static IzakayaSelectorPanel_New? Map { get; set; }

    internal static IzakayaConfigPannel? Config { get; set; }
}

internal sealed class PrepNightSceneServices : IPrepNightSceneServices
{
    internal static readonly PrepNightSceneServices Shared = new();

    public IPrepNightMapServices Map { get; } = new MapServices();

    public IPrepNightMenuServices Menu { get; } = new MenuServices();

    public IPrepNightSessionServices Session { get; } = new SessionServices();

    public ICommonServices Common => CommonServices.Shared;

    public IPresentationServices Presentation => PresentationServices.Shared;

    private sealed class MapServices : IPrepNightMapServices
    {
        public void SetConfirmEnabled(bool enabled)
        {
            ServiceScope.Require();
            StockGate.MapConfirm = enabled;
        }

        public void Confirm(IGuideMapSpot spot, IzakayaLevel level)
        {
            ServiceScope.Require();
            var panel = PrepPanels.Map ?? throw new InvalidOperationException("The guide map is not open.");
            GameMembers.Set(panel, "m_CurrentSelectedSpot", spot);
            GameMembers.Set(panel, "m_CurrentSelectedIzakayaLevel", level);
            StockGate.Bypass(() => GameMembers.Invoke(panel, "_OnGuideMapInitialize_b__21_0"));
        }
    }

    private sealed class MenuServices : IPrepNightMenuServices
    {
        public void AddRecipe(int id)
        {
            ServiceScope.Require();
            IzakayaConfigure.Instance.RegisterToDailyRecipes(id, true);
        }

        public void RemoveRecipe(int id)
        {
            ServiceScope.Require();
            IzakayaConfigure.Instance.LogoffFromDailyRecipes(id);
        }

        public void AddBeverage(int id)
        {
            ServiceScope.Require();
            IzakayaConfigure.Instance.RegisterToDailyBeverages(id, true);
        }

        public void RemoveBeverage(int id)
        {
            ServiceScope.Require();
            IzakayaConfigure.Instance.LogoffFromDailyBeverages(id);
        }

        public void AssignCooker(int id, int index)
        {
            ServiceScope.Require();
            IzakayaConfigure.Instance.RegisterToCookers(id, index, true);
        }

        public void RemoveCooker(int index)
        {
            ServiceScope.Require();
            IzakayaConfigure.Instance.LogOffFromCookers(index);
        }
    }

    private sealed class SessionServices : IPrepNightSessionServices
    {
        public void SetCompleteEnabled(bool enabled)
        {
            ServiceScope.Require();
            StockGate.PrepComplete = enabled;
        }

        public void Confirm()
        {
            ServiceScope.Require();
            var panel = PrepPanels.Config ?? throw new InvalidOperationException("The prep panel is not open.");
            var method = AccessTools.Method(typeof(IzakayaConfigPannel), "_SolveDailyCompletion_b__61_7")
                ?? AccessTools.Method(typeof(IzakayaConfigPannel), "SolveDailyCompletion")
                ?? throw new MissingMethodException(typeof(IzakayaConfigPannel).FullName, "SolveDailyCompletion");
            // The callback carries the SetCompleteEnabled gate, so the service's own press bypasses it.
            StockGate.Bypass(() => method.Invoke(panel, null));
        }

        public void ToWork()
        {
            ServiceScope.Require();
            PrepNightScene.SceneManager.instance.ToWork();
        }
    }
}

internal sealed class WorkSceneServices : IWorkSceneServices
{
    internal static readonly WorkSceneServices Shared = new();

    public IWorkSceneGuests Guests { get; } = WorkSceneGuestServing.Shared;

    public IWorkSceneCook Cook { get; } = new CookServices();

    public IWorkSceneStorage Storage { get; } = new StorageServices();

    public IWorkSceneDishes Dishes { get; } = WorkSceneGuestServing.Shared;

    public IWorkSceneTray Tray { get; } = new TrayServices();

    public IWorkSceneTime Time { get; } = WorkSceneTimeServices.Shared;

    public IWorkSceneEconomyServices Economy { get; } = WorkSceneEconomyServices.Shared;

    public IQteServices Qte { get; } = QteServices.Shared;

    public IWorkSceneBuffs Buffs { get; } = WorkSceneBuffsServices.Shared;

    public ISpellHost Spells { get; } = WorkSceneSpellHost.Shared;

    public IWorkSceneChallengeServices Challenge { get; } = ChallengeServices.Shared;

    public IWorkSceneIzakaya Izakaya { get; } = new IzakayaServices();

    public ICommonServices Common => CommonServices.Shared;

    public IPresentationServices Presentation => PresentationServices.Shared;

    private sealed class CookServices : IWorkSceneCook
    {
        public void SetCallEnabled(bool enabled)
        {
            ServiceScope.Require();
            StockGate.CookCall = enabled;
        }

        public void Start(int cookerIndex, Sellable result, Recipe recipe)
        {
            ServiceScope.Require();
            Cooker(cookerIndex).SetCook(result, recipe, false);
        }

        public void Extract(int cookerIndex)
        {
            ServiceScope.Require();
            Cooker(cookerIndex).Extract(null);
        }

        public void Store(int cookerIndex, Sellable value)
        {
            ServiceScope.Require();
            Cooker(cookerIndex).Store(value);
        }

        public void StartCountdown(int cookerIndex, float qteScore)
        {
            ServiceScope.Require();
            Cooker(cookerIndex).StartCookCountDown(qteScore, false);
        }

        private static CookController Cooker(int cookerIndex)
        {
            foreach (var item in (System.Collections.IEnumerable)CookSystemManager.instance.AllCookerControllers)
            {
                if (item is CookController cooker && cooker.GridIndex == cookerIndex)
                    return cooker;
            }

            throw new InvalidOperationException($"No cooker at index {cookerIndex}.");
        }
    }

    private sealed class StorageServices : IWorkSceneStorage
    {
        public void Store(Sellable sellable)
        {
            ServiceScope.Require();
            IzakayaConfigure.Instance.StoreFood(sellable);
        }

        public void Store(DishProxy dish)
        {
            ServiceScope.Require();
            if (dish?.Native is Sellable sellable)
                IzakayaConfigure.Instance.StoreFood(sellable);
        }
    }

    private sealed class TrayServices : IWorkSceneTray
    {
        public void SetCloseEnabled(bool enabled)
        {
            ServiceScope.Require();
            StockGate.ServeClose = enabled;
        }

        public void Receive(Sellable sellable)
        {
            ServiceScope.Require();
            IzakayaTray.Instance.Receive(sellable);
        }
    }

    private sealed class TimeServices : IWorkSceneTime
    {
        public void SetMode(GameTimeManager.TimeMode mode)
        {
            ServiceScope.Require();
            GameTimeManager.instance.SetGameTimeMode(mode);
        }
    }

    private sealed class IzakayaServices : IWorkSceneIzakaya
    {
        public void SetCloseEnabled(bool enabled)
        {
            ServiceScope.Require();
            StockGate.IzakayaClose = enabled;
        }

        public void SetTimeCloseEnabled(bool enabled)
        {
            ServiceScope.Require();
            BridgeGates.TimeClose = enabled;
        }

        public void Close()
        {
            ServiceScope.Require();
            StockGate.Bypass(() => GuestsManager.instance.TryCloseIzakaya());
        }
    }
}

internal sealed class StaffSceneServices : IStaffSceneServices
{
    internal static readonly StaffSceneServices Shared = new();

    public ICommonServices Common => CommonServices.Shared;

    public IPresentationServices Presentation => PresentationServices.Shared;
}

internal sealed class ResultSceneServices : IResultSceneServices
{
    internal static readonly ResultSceneServices Shared = new();

    public ICommonServices Common => CommonServices.Shared;

    public IPresentationServices Presentation => PresentationServices.Shared;
}
