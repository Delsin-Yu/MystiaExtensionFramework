using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using NightScene.UI.CookingUtility;

using Mystia.Listeners;

namespace Mystia.Modding.Bridge;

/// <summary>
/// Cooking submission of the selection panel. The game only offers the submit callback of the output button
/// as the <c>OnSubmit</c> local function of <c>WorkSceneCookingSelectionPannel.OnOutputSelected</c>, which the
/// compiler turns into <c>__c__DisplayClass79_0.Method_Internal_Void_PDM_0</c>; the seam therefore binds the
/// generated method by name and reads the submission out of the closure it belongs to: <c>solved</c> is the
/// matched combo whose recipe is cooked, and <c>selectedIngredients</c> on the panel behind <c>__4__this</c>
/// is what the player picked.
/// <para>
/// The panel path is the only one this hook covers, and the direct cooking entries do not carry a selection,
/// so <see cref="CookingRequest.FromSelectionPanel"/> is always true here.
/// </para>
/// </summary>
internal static class CookSelectionSeams
{
    [HarmonyPatch(typeof(WorkSceneCookingSelectionPannel.__c__DisplayClass79_0), "Method_Internal_Void_PDM_0")]
    private static class Submit
    {
        private static bool Prefix(WorkSceneCookingSelectionPannel.__c__DisplayClass79_0 __instance)
        {
            var recipe = __instance.solved?.Recipe;
            if (recipe is null)
                return true;

            var request = new CookingRequest(recipe, Selection(__instance), true);
            var cancelInvocation = false;
            Dispatch.Run<ICookSelectionListener>(listener => listener.OnPreCookingSubmit(ref request, ref cancelInvocation));
            return !cancelInvocation;
        }

        // The picked list is the ingredient list the game consumes on submit, so it wins over the combo's
        // leftover modifiers, which only describe what the recipe does not use.
        private static int[]? Selection(WorkSceneCookingSelectionPannel.__c__DisplayClass79_0 closure)
        {
            var picked = closure.__4__this?.selectedIngredients;
            if (picked is not null && picked.Count > 0)
            {
                var ids = new int[picked.Count];
                for (var index = 0; index < ids.Length; index++)
                    ids[index] = picked[index];
                return ids;
            }

            var modifiers = closure.solved?.Modifiers;
            if (modifiers is null || modifiers.Length == 0)
                return null;
            var extras = new int[modifiers.Length];
            for (var index = 0; index < extras.Length; index++)
                extras[index] = modifiers[index];
            return extras;
        }
    }
}
