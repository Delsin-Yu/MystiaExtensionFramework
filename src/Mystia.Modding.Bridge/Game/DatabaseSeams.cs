using System.Runtime.InteropServices;
using GameData.Core.Collections;
using GameData.Core.Collections.NightSceneUtility;
using GameData.CoreLanguage.Collections;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

namespace Mystia.Modding.Bridge;

// These three initializations rebuild their target tables wholesale (night spell handles, the
// scheduler node table, the day scene language tables), so injection has to happen after them.
// The class name carries the "initialization" marker because HarmonySeams already owns a
// DatabaseSeams class for the five original database passes.
internal static class DatabaseInitializationSeams
{
    [HarmonyPatch(typeof(DataBaseNight), nameof(DataBaseNight.Initialize))]
    private static class Night
    {
        private static void Postfix() => DatabaseInject.ApplySpells();
    }

    [HarmonyPatch(typeof(DataBaseScheduler), nameof(DataBaseScheduler.Initialize))]
    private static class Scheduler
    {
        private static void Postfix() => DatabaseInject.ApplyScheduler();
    }

    [HarmonyPatch(typeof(DaySceneLanguage), nameof(DaySceneLanguage.Initialize))]
    private static class DayLanguage
    {
        private static void Postfix() => DatabaseInject.ApplyDayLanguage();
    }
}

/// <summary>
/// Dictionary writes for native value types.
/// <para>
/// Il2CppInterop maps a native structure that contains references onto a managed class
/// (<c>Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase</c>) instead of a C# struct, so the
/// generated <c>Dictionary.set_Item</c> takes the reference-type branch and hands the native code the
/// pointer of the <b>boxed</b> object: the object header lands in the first fields of the value slot.
/// The same defect is documented for the mod that previously owned this data (RC 1:1: field
/// misalignment, invalid pointers and crashes on read back).
/// </para>
/// <para>
/// The bypass below is deliberately narrow: it only looks up the two argument <c>set_Item</c> of the
/// given dictionary and passes the raw structure data (<c>il2cpp_object_unbox</c>) for native value
/// types. Reference types, strings and C# structures (which the interop marshals correctly) never
/// need it - call sites use the generated indexer for those.
/// </para>
/// </summary>
internal static unsafe class InteropTables
{
    private static readonly Dictionary<nint, nint> Setters = [];

    /// <summary>Writes a native value type into an il2cpp dictionary without copying the object header.</summary>
    internal static void SetBoxedValue<TKey, TValue>(
        Il2CppSystem.Collections.Generic.Dictionary<TKey, TValue> table,
        TKey key,
        TValue value)
    {
        if (table is null)
            return;
        var klass = IL2CPP.il2cpp_object_get_class(table.Pointer);
        var setter = Setter(klass);
        if (setter == nint.Zero)
        {
            GameBridgeHook.Trace($"InteropTables: no two argument set_Item on {typeof(TKey).Name}->{typeof(TValue).Name}");
            return;
        }

        nint* args = stackalloc nint[2];
        var keyHandle = default(GCHandle);
        var valueHandle = default(GCHandle);
        try
        {
            args[0] = Pointee(key, ref keyHandle);
            args[1] = Pointee(value, ref valueHandle);
            var exception = nint.Zero;
            IL2CPP.il2cpp_runtime_invoke(setter, table.Pointer, (void**)args, ref exception);
            Il2CppException.RaiseExceptionIfNecessary(exception);
        }
        finally
        {
            if (keyHandle.IsAllocated)
                keyHandle.Free();
            if (valueHandle.IsAllocated)
                valueHandle.Free();
        }
    }

    private static nint Setter(nint klass)
    {
        lock (Setters)
        {
            if (Setters.TryGetValue(klass, out var cached))
                return cached;
        }

        var method = IL2CPP.il2cpp_class_get_method_from_name(klass, "set_Item", 2);
        if (method == nint.Zero)
        {
            var iterator = nint.Zero;
            while ((method = IL2CPP.il2cpp_class_get_methods(klass, ref iterator)) != nint.Zero)
            {
                if (Marshal.PtrToStringAnsi(IL2CPP.il2cpp_method_get_name(method)) == "set_Item"
                    && IL2CPP.il2cpp_method_get_param_count(method) == 2)
                    break;
            }
        }

        if (method != nint.Zero)
            lock (Setters)
                Setters[klass] = method;
        return method;
    }

    // Marshals one argument: raw structure data for native value types, the object pointer for
    // reference types, the il2cpp string for strings, and a pinned copy for C# structures.
    private static nint Pointee<T>(T value, ref GCHandle handle)
    {
        if (value is null)
            return nint.Zero;
        if (value is string text)
            return IL2CPP.ManagedStringToIl2Cpp(text);
        if (value is Il2CppObjectBase instance)
        {
            var pointer = instance.Pointer;
            if (pointer == nint.Zero)
                return nint.Zero;
            var klass = IL2CPP.il2cpp_object_get_class(pointer);
            return IL2CPP.il2cpp_class_is_valuetype(klass) ? IL2CPP.il2cpp_object_unbox(pointer) : pointer;
        }

        handle = GCHandle.Alloc(value, GCHandleType.Pinned);
        return handle.AddrOfPinnedObject();
    }
}

// BridgeSpell - the injected SpellBase that holds the mod's ISpell and runs its routine - lives in
// SpellPipeline.cs now, next to the ManagedEnumerator adapter it hands to the game. The empty routine below
// stays here because it is the fallback of that bridge instance as well as the shape of an effect-free card.

// An empty routine handed back to the game's spell queue. The queue iterates the returned value, so it
// has to be an IL2CPP object that is registered as an Il2CppSystem.Collections.IEnumerator.
internal sealed class EmptyCoroutine : Il2CppSystem.Object
{
    static EmptyCoroutine() =>
        ClassInjector.RegisterTypeInIl2Cpp<EmptyCoroutine>(new RegisterTypeOptions
        {
            Interfaces = new[] { typeof(Il2CppSystem.Collections.IEnumerator) },
        });

    public EmptyCoroutine(nint pointer) : base(pointer)
    {
    }

    public EmptyCoroutine() : base(ClassInjector.DerivedConstructorPointer<EmptyCoroutine>()) =>
        ClassInjector.DerivedConstructorBody(this);

    public Il2CppSystem.Object Current => null!;

    public bool MoveNext() => false;

    public void Reset()
    {
    }

    [HideFromIl2Cpp]
    internal static Il2CppSystem.Collections.IEnumerator Empty() =>
        new EmptyCoroutine().Cast<Il2CppSystem.Collections.IEnumerator>();
}

/// <summary>Handle for a spell asset created by the bridge (<c>IAssetHandle&lt;SpellBase&gt;</c>).</summary>
internal class SpellAssetHandle : Il2CppSystem.Object
{
    private readonly SpellBase? _asset;

    static SpellAssetHandle() =>
        ClassInjector.RegisterTypeInIl2Cpp<SpellAssetHandle>(new RegisterTypeOptions
        {
            Interfaces = new[] { typeof(DEYU.AssetHandleUtility.IAssetHandle<SpellBase>) },
        });

    public SpellAssetHandle(nint pointer) : base(pointer)
    {
    }

    public SpellAssetHandle(SpellBase asset) : base(ClassInjector.DerivedConstructorPointer<SpellAssetHandle>())
    {
        ClassInjector.DerivedConstructorBody(this);
        _asset = asset;
    }

    public static implicit operator DEYU.AssetHandleUtility.IAssetHandle<SpellBase>(SpellAssetHandle handle) => new(handle.Pointer);

    public bool IsPersistentAsset => true;

    public SpellBase Asset => _asset!;
}
