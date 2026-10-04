using DayScene.UI;
using GameData.Core.Collections.DaySceneUtility.Collections;
using GameData.RunTime.Common;
using GameData.RunTime.DaySceneUtility;
using HarmonyLib;
using Mystia.Data;
using Mystia.Listeners;

namespace Mystia.Modding.Bridge;

/// <summary>
/// Availability seams for the entries of a special guest's chat menu.
/// <para>
/// The menu is built by <c>DaySceneChatSelectionPannel.GetConfigurationSet</c>, which declares one local
/// function per entry. The compiler keeps those local functions in the display class the method's own
/// closure needs, and the interop generator names them by position:
/// <c>Method_Internal_Void_SpecialNPCInteractData_byref_String_byref_Boolean_byref_Action_n</c>. Only
/// the first two are pinned to a known entry — FreeChat is <c>_0</c> and Shop is <c>_1</c>, in that
/// order in the source — while Mission/Invite/RequestIngredient/RequestBeverage/Commission are <c>_2</c>
/// to <c>_6</c> with the order not confirmed, so they stay unhooked and report nothing.
/// </para>
/// <para>
/// The interop keeps those methods public, so they are addressed by <c>typeof</c>/<c>nameof</c> and a changed
/// compiler layout fails the bridge build instead of silently skipping the hook.
/// </para>
/// </summary>
internal static class ChatOptionSeams
{
    [HarmonyPatch(
        typeof(DaySceneChatSelectionPannel.__c__DisplayClass17_0),
        nameof(DaySceneChatSelectionPannel.__c__DisplayClass17_0
            .Method_Internal_Void_SpecialNPCInteractData_byref_String_byref_Boolean_byref_Action_0)
    )]
    private static class FreeChat
    {
        // FreeChat's own availability is merchantData == null && HasChatData && not skipping the greeting;
        // the postfix runs after it and sees what the game decided.
        private static void Postfix(
            DaySceneChatSelectionPannel.SpecialNPCInteractData specialNPCInteractData,
            ref bool availability
        ) => ChatOptions.Run(ChatOptionKind.FreeChat, specialNPCInteractData, ref availability);
    }

    [HarmonyPatch(
        typeof(DaySceneChatSelectionPannel.__c__DisplayClass17_0),
        nameof(DaySceneChatSelectionPannel.__c__DisplayClass17_0
            .Method_Internal_Void_SpecialNPCInteractData_byref_String_byref_Boolean_byref_Action_1)
    )]
    private static class Shop
    {
        // Shop's availability is merchantData != null plus a non empty product list (or the Kourindou
        // special case), so the context reports the tracked merchant record the game used.
        private static void Postfix(
            DaySceneChatSelectionPannel.SpecialNPCInteractData specialNPCInteractData,
            ref bool availability
        ) => ChatOptions.Run(ChatOptionKind.Shop, specialNPCInteractData, ref availability);
    }
}

/// <summary>
/// Builds the context one menu entry is judged by and hands it to <see cref="IChatOptionListener"/>.
/// </summary>
internal static class ChatOptions
{
    // Every field is what the game itself knows at the point the entry's local function runs, taken from the
    // interact data the panel passes to it:
    //   CharacterLabel/CharacterId - SpecialNPCInteractData.characterLabel and its npc id
    //   Kind                       - this menu is only built for special guests (SpecialGuestOpenContext)
    //   IsMerchant                 - a merchant definition exists in DataBaseDay.allMerchants
    //   HasMerchantData/ProductCount - the day's TrackedMerchant record (SpecialNPCInteractData.merchantData)
    //   HasChatData                - RunTimeDayScene's own chat data query; FreeChat additionally requires
    //                                merchantData == null and the greeting not to be skipped, so the listener
    //                                sees the raw fact while "available" carries the game's decision
    //   IsIgnored                  - StatusTracker.IgnoredGuests holds the character id
    private static ChatOptionContext Build(ChatOptionKind option, DaySceneChatSelectionPannel.SpecialNPCInteractData data)
    {
        var label = data.characterLabel;
        var merchant = data.merchantData;
        var hasLabel = !string.IsNullOrEmpty(label);
        var characterId = hasLabel ? RunTimeAlbum.RefSpecialNPCId(label) : -1;
        var statusTracker = data.statusTracker;
        var ignored = characterId >= 0 && statusTracker?.IgnoredGuests?.Contains(characterId) == true;

        return new ChatOptionContext(
            option,
            label,
            CharacterKind.Special,
            characterId,
            MerchantPipeline.IsMerchant(label),
            merchant is not null,
            merchant?.products?.Length ?? 0,
            hasLabel && RunTimeDayScene.HasChatData(label),
            ignored
        );
    }

    internal static void Run(
        ChatOptionKind option,
        DaySceneChatSelectionPannel.SpecialNPCInteractData? data,
        ref bool available
    )
    {
        if (data is null)
            return;

        var context = Build(option, data);
        foreach (var listener in Dispatch.Instances<IChatOptionListener>())
            listener.OnChatOptionAvailability(in context, ref available);
    }
}

/// <summary>
/// The confirmations the day scene's own chat flow acts on. A confirmation is a compiler generated callback the
/// panel invokes with the player's verdict; the callback's body is what carries the confirmation out (for the
/// Yuyuko challenge: schedule the challenge's event and start the challenge session), so the callback itself is
/// what travels to the listeners - a mod that holds the confirmation keeps the action and runs it once the
/// machine it talks to has decided.
/// </summary>
internal static class ChatConfirmationSeams
{
    // The name is the one the interop gives the callback of YuyukoExtraDialogData.Yuyuko_Challenge; a build whose
    // compiler layout moved fails this file's build instead of silently hooking nothing.
    [HarmonyPatch(
        typeof(YuyukoExtraDialogData.__c__DisplayClass4_0),
        nameof(YuyukoExtraDialogData.__c__DisplayClass4_0._Yuyuko_Challenge_b__2)
    )]
    private static class YuyukoChallenge
    {
        private static bool Prefix(YuyukoExtraDialogData.__c__DisplayClass4_0 __instance, bool confirm) =>
            ChatConfirmations.Allow(
                ChatConfirmationKind.YuyukoChallenge,
                confirm,
                () => __instance._Yuyuko_Challenge_b__2(confirm));
    }
}

/// <summary>
/// The dispatch of a chat confirmation. It is pure dispatch, so the order the listeners are asked in, what they
/// see of each other's verdict and the action they are handed stay testable without the game running (see the
/// chat confirmation suite).
/// </summary>
internal static class ChatConfirmations
{
    /// <summary>
    /// Asks every listener whether one confirmation may act; false holds the game's own action. The action travels
    /// inside the view, so a listener that held the confirmation can run it later - and running it is the same
    /// call the game was about to make, which is what makes the confirmation reach its listeners again.
    /// </summary>
    internal static bool Allow(ChatConfirmationKind kind, bool confirmed, Action confirm)
    {
        var view = new ChatConfirmationView(kind, confirmed, confirm);
        var cancel = false;
        foreach (var listener in Dispatch.Instances<IChatConfirmationListener>())
            listener.OnPreChatConfirmation(view, ref cancel);
        return !cancel;
    }
}
