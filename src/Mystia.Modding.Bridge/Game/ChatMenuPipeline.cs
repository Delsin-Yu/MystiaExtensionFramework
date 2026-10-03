using System.Runtime.InteropServices;
using DayScene.Interactables;
using DayScene.Interactables.Collections.BehaviourComponents;
using DayScene.UI;
using DEYU.AdpUISystem.Managers;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppInterop.Runtime.Runtime;
using Mystia.Data;
using Mystia.Listeners;

namespace Mystia.Modding.Bridge;

/// <summary>
/// Remembers which interactable opened the chat menu that is being built.
/// <para>
/// The general chat menu overload carries no character and no source, so the origin is taken from the last
/// entity the player interacted with. The record is overwritten on every interaction (an unmapped component
/// records <see cref="ChatMenuOrigin.Unknown"/>), and the menu build consumes it, so it can only describe an
/// interaction that happened before the menu opened.
/// </para>
/// <para>
/// The hook is <c>InteractableArea.OnInteract</c> and not the base <c>EntityBehaviourComponent.OnInteract</c>:
/// the map components that open the general chat menu override <c>OnInteract</c> and several of them
/// (<c>CollabBehaviourComponent</c>, <c>NitoriTelephoneComponent</c>) never call <c>base.OnInteract()</c>, so
/// a patch on the base method would miss exactly the components this pipeline exists for.
/// <c>InteractableArea.OnInteract</c> is what the player input generator calls with the behaviour it holds
/// (<c>DayScenePlayerInputGenerator.cs:288</c>).
/// </para>
/// </summary>
internal static class ChatMenuOrigins
{
    private static ChatMenuOrigin? _pending;

    internal static void Remember(EntityBehaviourComponent? behaviour) => _pending = Map(behaviour);

    internal static ChatMenuOrigin Take()
    {
        var origin = _pending ?? ChatMenuOrigin.Unknown;
        _pending = null;
        return origin;
    }

    private static ChatMenuOrigin Map(EntityBehaviourComponent? behaviour) => behaviour switch
    {
        CollabBehaviourComponent => ChatMenuOrigin.Collab,
        CreatorBoxBehaviourComponent => ChatMenuOrigin.CreatorBox,
        NueSlotMachineComponent => ChatMenuOrigin.NueSlotMachine,
        NitoriTelephoneComponent => ChatMenuOrigin.Telephone,
        LunarCapitalConsoleBehaviourComponent => ChatMenuOrigin.Console,
        MissionInteractBehaviourComponent => ChatMenuOrigin.Mission,
        _ => ChatMenuOrigin.Unknown,
    };

    [HarmonyPatch(typeof(InteractableArea), nameof(InteractableArea.OnInteract))]
    private static class Interacting
    {
        private static void Prefix(InteractableArea __instance) => Remember(__instance.behaviourComponent);
    }
}

/// <summary>
/// Appends mod supplied entries to the general chat menu.
/// <para>
/// The game builds that menu from a <c>GetSelectionConfigurationCallback[]</c> and appends its own end button
/// last, so the mod entries are appended to the array the prefix is handed. The three argument overload is
/// selected by its parameter types because <c>OpenAfterChatMenu</c> is overloaded three times at the same
/// name.
/// </para>
/// <para>
/// The context of a general menu has no character: the overload is handed no label and the character menus
/// use the other two overloads, so <c>CharacterLabel</c> stays null, <c>CharacterId</c> is -1 and the
/// <c>Origin</c> is the useful part of the context.
/// </para>
/// </summary>
internal static class ChatMenuPipeline
{
    [HarmonyPatch(
        typeof(DayScene.UI.UIManager),
        nameof(DayScene.UI.UIManager.OpenAfterChatMenu),
        [
            typeof(Il2CppReferenceArray<DaySceneChatSelectionPannel.GetSelectionConfigurationCallback>),
            typeof(string),
            typeof(DaySceneChatSelectionPannel.GeneralOpenContext.EndButtonCallback),
            typeof(Il2CppSystem.Action),
            typeof(int),
            typeof(AdpUIPanelManager.PanelVisualMode),
        ]
    )]
    private static class GeneralMenu
    {
        private static void Prefix(
            ref Il2CppReferenceArray<DaySceneChatSelectionPannel.GetSelectionConfigurationCallback> configurationCallbacks
        )
        {
            var context = new ChatMenuContext(ChatMenuOrigins.Take(), null, CharacterKind.Special, -1, false);
            try
            {
                Append(ref configurationCallbacks, context);
            }
            catch (Exception error)
            {
                // A failure here must not keep the game from opening its own menu.
                GameBridgeHook.Trace($"ChatMenuPipeline: cannot build entries: {error.GetBaseException().Message}");
            }
        }
    }

    private static void Append(
        ref Il2CppReferenceArray<DaySceneChatSelectionPannel.GetSelectionConfigurationCallback> callbacks,
        ChatMenuContext context
    )
    {
        var entries = new List<ChatMenuEntry>();
        foreach (var provider in Dispatch.Instances<IChatMenuProvider>())
            provider.ProvideChatMenuEntries(in context, entries);
        if (entries.Count == 0)
            return;

        var original = callbacks;
        var count = original?.Length ?? 0;
        var combined = new Il2CppReferenceArray<DaySceneChatSelectionPannel.GetSelectionConfigurationCallback>(
            count + entries.Count
        );
        for (var i = 0; i < count; i++)
            combined[i] = original![i];
        for (var i = 0; i < entries.Count; i++)
            combined[count + i] = Callback(entries[i]);
        callbacks = combined;
    }

    // One game callback per entry. The callback is invoked once per menu build (twice per entry: the panel
    // asks for the whole configuration and then again for the title), so the label and the availability are
    // captured as values and the action is converted to the game's delegate only when the entry is picked.
    private static DaySceneChatSelectionPannel.GetSelectionConfigurationCallback Callback(ChatMenuEntry entry) =>
        ChatMenuCallbacks.Create(
            (
                DaySceneChatSelectionPannel.BaseInteractData? interactData,
                out string title,
                out bool availability,
                out Il2CppSystem.Action? onInteract
            ) =>
            {
                title = entry.Label;
                availability = entry.Available;
                onInteract = entry.OnSelected is null
                    ? null
                    : DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(entry.OnSelected);
            }
        );
}

/// <summary>
/// Builds the game's <c>GetSelectionConfigurationCallback</c> out of a managed handler.
/// <para>
/// The entry delegate carries three <c>out</c> parameters (<c>out string</c>, <c>out bool</c>,
/// <c>out Il2CppSystem.Action</c>). <c>DelegateSupport.ConvertDelegate</c> marshals a managed delegate
/// through a generated trampoline, and the mod side already found that shape unreliable for this exact
/// delegate and built the delegate by hand instead (its <c>Il2CppOutDelegate</c>, which this factory is the
/// bridge side of). The bridge therefore builds the il2cpp delegate directly: one native invoker, with the
/// handler looked up from the per delegate method info the delegate is created with.
/// </para>
/// <para>
/// The entry action is a plain delegate, so it is converted with
/// <see cref="DelegateSupport.ConvertDelegate{T}"/> at the call site (see <c>ChatMenuPipeline.Callback</c>).
/// </para>
/// </summary>
internal static unsafe class ChatMenuCallbacks
{
    internal delegate void SelectionHandler(
        DaySceneChatSelectionPannel.BaseInteractData? interactData,
        out string title,
        out bool availability,
        out Il2CppSystem.Action? onInteract
    );

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void NativeSelectionInvoker(
        IntPtr thisPointer,
        IntPtr interactDataPointer,
        IntPtr* titleOut,
        byte* availabilityOut,
        IntPtr* onInteractOut,
        Il2CppMethodInfo* methodInfo
    );

    private static readonly NativeSelectionInvoker NativeEntry = Invoke;
    private static readonly IntPtr NativeEntryPointer = Marshal.GetFunctionPointerForDelegate(NativeEntry);

    // The handler of every live delegate, keyed by the method info the delegate was created with, plus the
    // managed wrappers themselves: the il2cpp object holds pointers into both, so neither may be collected.
    private static readonly Dictionary<IntPtr, SelectionHandler> Handlers = [];
    private static readonly List<DaySceneChatSelectionPannel.GetSelectionConfigurationCallback> KeepAlive = [];

    internal static DaySceneChatSelectionPannel.GetSelectionConfigurationCallback Create(SelectionHandler handler)
    {
        var classPointer = Il2CppClassPointerStore<
            DaySceneChatSelectionPannel.GetSelectionConfigurationCallback
        >.NativeClassPtr;
        if (classPointer == IntPtr.Zero)
            throw new InvalidOperationException("GetSelectionConfigurationCallback is not initialized yet.");

        var methodInfo = UnityVersionHandler.NewMethod();
        methodInfo.MethodPointer = NativeEntryPointer;
        methodInfo.ParametersCount = 4;
        methodInfo.Slot = ushort.MaxValue;
        methodInfo.IsMarshalledFromNative = true;
        Handlers[methodInfo.Pointer] = handler;

        Il2CppSystem.Delegate converted;
        var target = new Il2CppSystem.Object();
        if (UnityVersionHandler.MustUseDelegateConstructor)
        {
            var created = Activator.CreateInstance(
                typeof(DaySceneChatSelectionPannel.GetSelectionConfigurationCallback),
                target,
                methodInfo.Pointer
            )!;
            converted = ((DaySceneChatSelectionPannel.GetSelectionConfigurationCallback)created)
                .Cast<Il2CppSystem.Delegate>();
        }
        else
        {
            converted = new Il2CppSystem.Delegate(IL2CPP.il2cpp_object_new(classPointer));
        }

        converted.method_ptr = methodInfo.MethodPointer;
        converted.method = methodInfo.Pointer;
        converted.m_target = target;
        if (UnityVersionHandler.MustUseDelegateConstructor)
        {
            converted.invoke_impl = converted.method_ptr;
            converted.method_code = target.Pointer;
        }

        var result = converted.Cast<DaySceneChatSelectionPannel.GetSelectionConfigurationCallback>();
        KeepAlive.Add(result);
        return result;
    }

    // Called from native code: nothing may escape, and the out values are zeroed on failure so the game
    // sees an unavailable entry instead of a half written one.
    private static void Invoke(
        IntPtr thisPointer,
        IntPtr interactDataPointer,
        IntPtr* titleOut,
        byte* availabilityOut,
        IntPtr* onInteractOut,
        Il2CppMethodInfo* methodInfo
    )
    {
        try
        {
            if (!Handlers.TryGetValue((IntPtr)methodInfo, out var handler))
                return;

            var interactData = interactDataPointer != IntPtr.Zero
                ? new DaySceneChatSelectionPannel.BaseInteractData(interactDataPointer)
                : null;

            handler(interactData, out var title, out var availability, out var onInteract);

            if (titleOut != null)
                *titleOut = title is not null ? IL2CPP.ManagedStringToIl2Cpp(title) : IntPtr.Zero;
            if (availabilityOut != null)
                *availabilityOut = (byte)(availability ? 1 : 0);
            if (onInteractOut != null)
                *onInteractOut = onInteract is not null ? onInteract.Pointer : IntPtr.Zero;
        }
        catch (Exception error)
        {
            GameBridgeHook.Trace($"ChatMenuPipeline: chat entry callback failed: {error.GetBaseException().Message}");
            if (titleOut != null)
                *titleOut = IntPtr.Zero;
            if (availabilityOut != null)
                *availabilityOut = 0;
            if (onInteractOut != null)
                *onInteractOut = IntPtr.Zero;
        }
    }
}
