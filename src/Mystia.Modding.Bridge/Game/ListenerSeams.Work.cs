using Common.UI;
using Common.TimelineExtestion;
using GameData.Core.Collections;
using GameData.RunTime.NightSceneUtility;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;
using Mystia.Listeners;
using Mystia.Scenes;
using NightScene.CookingUtility;
using NightScene.UI.CookingUtility;
using NightScene.UI.GuestManagementUtility;
using PrepNightScene.UI;

namespace Mystia.Modding.Bridge;

/// <summary>
/// One view per open panel. The first callback of a panel builds its view and every later callback of the
/// same panel reuses it, so a pre hook and its matching post hook write into the same object. The entry is
/// dropped when the panel closes, so a reopened panel starts from a fresh view.
/// </summary>
internal static class PanelViews<TPanel, TView>
    where TPanel : Il2CppObjectBase
    where TView : class
{
    private static readonly Dictionary<IntPtr, TView> Views = [];

    internal static TView Of(TPanel panel, Func<TPanel, TView> create) =>
        Views.TryGetValue(panel.Pointer, out var view) ? view : Views[panel.Pointer] = create(panel);

    internal static void Drop(TPanel panel) => Views.Remove(panel.Pointer);
}

/// <summary>The views the listener seams dispatch, each cached per open panel.</summary>
internal static class PanelViewCache
{
    internal static ServePannelView Serve(WorkSceneServePannel panel) =>
        PanelViews<WorkSceneServePannel, ServePannelView>.Of(panel, static p => new ServePannelView(p));

    internal static void DropServe(WorkSceneServePannel panel) =>
        PanelViews<WorkSceneServePannel, ServePannelView>.Drop(panel);

    internal static GuideMapView GuideMap(IzakayaSelectorPanel_New panel) =>
        PanelViews<IzakayaSelectorPanel_New, GuideMapView>.Of(panel, static p => new GuideMapView(p));

    internal static void DropGuideMap(IzakayaSelectorPanel_New panel) =>
        PanelViews<IzakayaSelectorPanel_New, GuideMapView>.Drop(panel);

    internal static PrepConfigView PrepConfig(IzakayaConfigPannel panel) =>
        PanelViews<IzakayaConfigPannel, PrepConfigView>.Of(panel, static p => new PrepConfigView(p));

    internal static void DropPrepConfig(IzakayaConfigPannel panel) =>
        PanelViews<IzakayaConfigPannel, PrepConfigView>.Drop(panel);
}

internal static class WorkListenerSeams
{
    [HarmonyPatch(typeof(WorkSceneServePannel), nameof(WorkSceneServePannel.OnPanelOpen))]
    private static class PanelOpened
    {
        // The original clears its own pending dish fields in OnPanelOpen, so the notification
        // has to run after it. A reopened panel starts from a fresh view.
        private static void Postfix(WorkSceneServePannel __instance)
        {
            PanelViewCache.DropServe(__instance);
            var view = PanelViewCache.Serve(__instance);
            Dispatch.Run<IWorkListener>(listener => listener.OnServePanelOpened(view));
        }
    }

    [HarmonyPatch(typeof(WorkSceneServePannel), nameof(WorkSceneServePannel.OnPanelClose))]
    private static class PanelClosing
    {
        // Runs before the original settles the order. Same method also carries ServeHolds.Close,
        // which gates the close itself; Harmony fixes the relative order of the two prefixes only
        // by priority, which neither side sets.
        private static bool Prefix(WorkSceneServePannel __instance)
        {
            var view = PanelViewCache.Serve(__instance);
            var cancel = false;
            foreach (var listener in Dispatch.Instances<IWorkListener>())
                listener.OnPreServePanelClosed(view, ref cancel);
            return !cancel;
        }
    }

    [HarmonyPatch(typeof(WorkSceneServePannel), nameof(WorkSceneServePannel.Send))]
    private static class DishSending
    {
        private static bool Prefix(WorkSceneServePannel __instance, ref Sellable toSend)
        {
            var view = PanelViewCache.Serve(__instance);
            var cancel = false;
            foreach (var listener in Dispatch.Instances<IWorkListener>())
                listener.OnPreDishServed(view, ref toSend, ref cancel);
            return !cancel;
        }
    }

    [HarmonyPatch(typeof(WorkSceneServePannel), nameof(WorkSceneServePannel.Cancel))]
    private static class DishCancelling
    {
        private static bool Prefix(WorkSceneServePannel __instance, ref Sellable toCancel)
        {
            var view = PanelViewCache.Serve(__instance);
            var cancel = false;
            foreach (var listener in Dispatch.Instances<IWorkListener>())
                listener.OnPreDishCancelled(view, ref toCancel, ref cancel);
            return !cancel;
        }
    }

    [HarmonyPatch(typeof(WorkSceneStoragePannel), nameof(WorkSceneStoragePannel.Extract))]
    private static class StorageExtracting
    {
        private static bool Prefix(ref Sellable toExtract)
        {
            var cancel = false;
            foreach (var listener in Dispatch.Instances<IWorkListener>())
                listener.OnPreStorageExtracted(ref toExtract, ref cancel);
            return !cancel;
        }
    }

    [HarmonyPatch(typeof(GameTimeManager), nameof(GameTimeManager.SetGameTimeMode))]
    private static class TimeModeSetting
    {
        private static bool Prefix(GameTimeManager __instance, ref GameTimeManager.TimeMode mode)
        {
            var cancel = false;
            foreach (var listener in Dispatch.Instances<IWorkListener>())
                listener.OnPreTimeModeSet(__instance, ref mode, ref cancel);
            return !cancel;
        }
    }
}

internal static class CookListenerSeams
{
    [HarmonyPatch(typeof(CookController), nameof(CookController.SetCook))]
    private static class CookStarting
    {
        private static bool Prefix(CookController __instance, ref Sellable thisResult, ref Recipe recipe)
        {
            var cancel = false;
            foreach (var listener in Dispatch.Instances<ICookListener>())
                listener.OnPreCookStarted(__instance, ref thisResult, ref recipe, ref cancel);
            return !cancel;
        }
    }

    [HarmonyPatch(typeof(CookController), nameof(CookController.StartCookCountDown))]
    private static class CountdownStarting
    {
        private static bool Prefix(CookController __instance, ref float qteScore)
        {
            var cancel = false;
            foreach (var listener in Dispatch.Instances<ICookListener>())
                listener.OnPreCookCountdownStarted(__instance, ref qteScore, ref cancel);
            return !cancel;
        }
    }
}

internal static class PrepListenerSeams
{
    [HarmonyPatch(typeof(IzakayaConfigure), nameof(IzakayaConfigure.RegisterToDailyRecipes))]
    private static class RecipeRegistering
    {
        private static bool Prefix(int id)
        {
            var cancel = false;
            foreach (var listener in Dispatch.Instances<IPrepListener>())
                listener.OnPreRecipeAdded(id, ref cancel);
            return !cancel;
        }
    }

    [HarmonyPatch(typeof(IzakayaConfigure), nameof(IzakayaConfigure.RegisterToDailyBeverages))]
    private static class BeverageRegistering
    {
        private static bool Prefix(int id)
        {
            var cancel = false;
            foreach (var listener in Dispatch.Instances<IPrepListener>())
                listener.OnPreBeverageAdded(id, ref cancel);
            return !cancel;
        }
    }

    [HarmonyPatch(typeof(IzakayaConfigure), nameof(IzakayaConfigure.RegisterToCookers))]
    private static class CookerRegistering
    {
        private static bool Prefix(int id, int index)
        {
            var cancel = false;
            foreach (var listener in Dispatch.Instances<IPrepListener>())
                listener.OnPreCookerAssigned(id, index, ref cancel);
            return !cancel;
        }
    }

    [HarmonyPatch(typeof(IzakayaConfigPannel), nameof(IzakayaConfigPannel.GoToSpecific))]
    private static class TabSelected
    {
        private static void Postfix(IzakayaConfigPannel __instance)
        {
            var view = PanelViewCache.PrepConfig(__instance);
            Dispatch.Run<IPrepListener>(listener => listener.OnConfigTabSelected(view));
        }
    }

    [HarmonyPatch(typeof(IzakayaConfigure), nameof(IzakayaConfigure.Initialize))]
    private static class ConfigureInitialized
    {
        private static void Postfix(IzakayaConfigure __instance) =>
            Dispatch.Run<IPrepListener>(listener => listener.OnConfigureUpdated(__instance));
    }

    [HarmonyPatch(typeof(IzakayaConfigure), nameof(IzakayaConfigure.UpdateValue))]
    private static class ConfigureUpdated
    {
        private static void Postfix(IzakayaConfigure __instance) =>
            Dispatch.Run<IPrepListener>(listener => listener.OnConfigureUpdated(__instance));
    }
}
