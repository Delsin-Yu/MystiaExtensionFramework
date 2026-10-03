using HarmonyLib;
using Mystia.Listeners;
using NightScene.EventUtility;

namespace Mystia.Modding.Bridge;

// The four work scene value edits. Each prefix reports the edit before the game applies it and lets a
// listener cancel it; each postfix reports the amount the game actually applied (the methods round/scale
// their argument themselves, so the postfix reads the final value, not the caller's). There is no gate
// switch here: IWorkSceneEconomyServices replays through these same methods, so a mod's own edit is observed
// here exactly like a game side edit.
internal static class MetricsSeams
{
    [HarmonyPatch(typeof(EventManager), nameof(EventManager.FundEdit))]
    private static class Fund
    {
        private static bool Prefix(ref float value, ref EventManager.MathOperation mathOperation)
        {
            var cancel = false;
            foreach (var listener in Dispatch.Instances<IWorkMetricsListener>())
                listener.OnPreFundEdit(ref value, ref mathOperation, ref cancel);
            return !cancel;
        }

        private static void Postfix(float value) =>
            Dispatch.Run<IWorkMetricsListener>(listener => listener.OnFundEdited(value));
    }

    [HarmonyPatch(typeof(EventManager), nameof(EventManager.TipEdit))]
    private static class Tip
    {
        private static bool Prefix(
            ref int value,
            ref EventManager.ServeType serveType,
            ref float comboBuff,
            ref float moodBuff,
            ref float extraBuff)
        {
            var cancel = false;
            foreach (var listener in Dispatch.Instances<IWorkMetricsListener>())
                listener.OnPreTipEdit(ref value, ref serveType, ref comboBuff, ref moodBuff, ref extraBuff, ref cancel);
            return !cancel;
        }

        private static void Postfix(
            int value,
            EventManager.ServeType serveType,
            float comboBuff,
            float moodBuff,
            float extraBuff) =>
            Dispatch.Run<IWorkMetricsListener>(listener => listener.OnTipEdited(value, serveType, comboBuff, moodBuff, extraBuff));
    }

    [HarmonyPatch(typeof(EventManager), nameof(EventManager.ExpEdit))]
    private static class Experience
    {
        private static bool Prefix(ref float value, ref EventManager.MathOperation mathOperation)
        {
            var cancel = false;
            foreach (var listener in Dispatch.Instances<IWorkMetricsListener>())
                listener.OnPreExperienceEdit(ref value, ref mathOperation, ref cancel);
            return !cancel;
        }

        private static void Postfix(float value) =>
            Dispatch.Run<IWorkMetricsListener>(listener => listener.OnExperienceEdited(value));
    }

    [HarmonyPatch(typeof(EventManager), nameof(EventManager.PassionEdit))]
    private static class Passion
    {
        private static bool Prefix(ref float value, ref EventManager.MathOperation mathOperation)
        {
            var cancel = false;
            foreach (var listener in Dispatch.Instances<IWorkMetricsListener>())
                listener.OnPrePassionEdit(ref value, ref mathOperation, ref cancel);
            return !cancel;
        }

        private static void Postfix(float value) =>
            Dispatch.Run<IWorkMetricsListener>(listener => listener.OnPassionEdited(value));
    }
}
