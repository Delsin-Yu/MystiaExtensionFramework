using Cysharp.Threading.Tasks;
using DEYU.AssetHandleUtility;
using GameData.Core.Collections.CharacterUtility;
using GameData.Profile;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace Mystia.Modding.Bridge;

internal static class PortraitSprites
{
    private static readonly Dictionary<nint, Sprite[]> Faces = new();

    internal static void RegisterHandles()
    {
        ClassInjector.RegisterTypeInIl2Cpp<SpriteAssetHandle>(new RegisterTypeOptions
        {
            Interfaces = new[] { typeof(IAssetHandle<Sprite>) },
        });
        ClassInjector.RegisterTypeInIl2Cpp<SpriteAssetHandleArray>(new RegisterTypeOptions
        {
            Interfaces = new[] { typeof(IAssetHandleArray<Sprite>) },
        });
    }

    internal static GuestProfilePair Attach(int id, Sprite[] portraits, int notebook, int positive, int negative, CharacterSkinSets pixels)
    {
        var portrayal = ScriptableObject.CreateInstance<CharacterPortrayal>();
        portrayal.faceInNoteBook = notebook;
        portrayal.positiveSpellCardFace = positive;
        portrayal.negativeSpellCardFace = negative;
        Faces[portrayal.Pointer] = portraits;
        var set = ScriptableObject.CreateInstance<CharacterProtrayalSet>();
        set.defaultPortrayal = portrayal;
        return new GuestProfilePair(
            id,
            DataBaseCharacter.UnifiedNormalGuestBGColor,
            DataBaseCharacter.UnifiedNormalGuestTextColor,
            set,
            pixels);
    }

    internal static bool TryGet(CharacterPortrayal portrayal, out Sprite[] sprites) =>
        Faces.TryGetValue(portrayal.Pointer, out sprites!);
}

internal class SpriteAssetHandle : Il2CppSystem.Object
{
    private readonly Sprite? _asset;

    public SpriteAssetHandle(nint pointer) : base(pointer)
    {
    }

    public SpriteAssetHandle(Sprite asset) : base(ClassInjector.DerivedConstructorPointer<SpriteAssetHandle>())
    {
        ClassInjector.DerivedConstructorBody(this);
        _asset = asset;
    }

    public static implicit operator IAssetHandle<Sprite>(SpriteAssetHandle handle) => new(handle.Pointer);

    public bool IsPersistentAsset => true;

    public Sprite Asset => _asset!;
}

internal class SpriteAssetHandleArray : Il2CppSystem.Object
{
    private readonly Sprite[]? _assets;

    public SpriteAssetHandleArray(nint pointer) : base(pointer)
    {
    }

    public SpriteAssetHandleArray(Sprite[] assets) : base(ClassInjector.DerivedConstructorPointer<SpriteAssetHandleArray>())
    {
        ClassInjector.DerivedConstructorBody(this);
        _assets = assets;
    }

    public static implicit operator IAssetHandleArray<Sprite>(SpriteAssetHandleArray handle) => new(handle.Pointer);

    public int Count => _assets?.Length ?? 0;

    public Sprite this[int index] => _assets![index];

    public Il2CppArrayBase<Sprite> ToAssetArray()
    {
        var assets = _assets ?? [];
        var array = new Il2CppReferenceArray<Sprite>(assets.Length);
        for (var index = 0; index < assets.Length; index++)
            array[index] = assets[index];
        return array;
    }
}

internal static class PortraitSeams
{
    [HarmonyPatch(typeof(CharacterPortrayal), nameof(CharacterPortrayal.LoadVisualHandle))]
    private static class One
    {
        private static void Postfix(CharacterPortrayal __instance, ref UniTask<IAssetHandle<Sprite>> __result, int index)
        {
            if (!TryFace(__instance, index, out var sprite))
                return;
            IAssetHandle<Sprite> handle = new SpriteAssetHandle(sprite);
            __result = UniTask.FromResult(handle);
        }
    }

    [HarmonyPatch(typeof(CharacterPortrayal), nameof(CharacterPortrayal.LoadAllVisualHandles))]
    private static class All
    {
        private static void Postfix(CharacterPortrayal __instance, ref UniTask<IAssetHandleArray<Sprite>> __result)
        {
            if (!PortraitSprites.TryGet(__instance, out var faces))
                return;
            IAssetHandleArray<Sprite> handle = new SpriteAssetHandleArray(faces);
            __result = UniTask.FromResult(handle);
        }
    }

    [HarmonyPatch(typeof(CharacterPortrayal), nameof(CharacterPortrayal.LoadNotebookVisual))]
    private static class Notebook
    {
        private static void Postfix(CharacterPortrayal __instance, ref UniTask<IAssetHandle<Sprite>> __result)
        {
            if (!TryFace(__instance, __instance.faceInNoteBook, out var sprite))
                return;
            IAssetHandle<Sprite> handle = new SpriteAssetHandle(sprite);
            __result = UniTask.FromResult(handle);
        }
    }

    [HarmonyPatch(typeof(CharacterPortrayal), nameof(CharacterPortrayal.LoadSpellPortrayal))]
    private static class Spell
    {
        private static void Postfix(CharacterPortrayal __instance, ref Il2CppSystem.ValueTuple<UniTask<IAssetHandle<Sprite>>, UniTask<IAssetHandle<Sprite>>> __result)
        {
            if (!PortraitSprites.TryGet(__instance, out var faces))
                return;
            // The game builds a pair, which the interop spells Il2CppSystem.ValueTuple: the two handles are its
            // Item1 and Item2, and only that type is the one the patched method really returns.
            __result = new Il2CppSystem.ValueTuple<UniTask<IAssetHandle<Sprite>>, UniTask<IAssetHandle<Sprite>>>(
                Face(faces, __instance.positiveSpellCardFace),
                Face(faces, __instance.negativeSpellCardFace));
        }

        private static UniTask<IAssetHandle<Sprite>> Face(Sprite[] faces, int index)
        {
            if ((uint)index >= (uint)faces.Length || faces[index] is null)
                return UniTask.FromResult<IAssetHandle<Sprite>>(new SpriteAssetHandle(null!));
            IAssetHandle<Sprite> handle = new SpriteAssetHandle(faces[index]);
            return UniTask.FromResult(handle);
        }
    }

    private static bool TryFace(CharacterPortrayal portrayal, int index, out Sprite sprite)
    {
        sprite = null!;
        if (!PortraitSprites.TryGet(portrayal, out var faces) || (uint)index >= (uint)faces.Length)
            return false;
        sprite = faces[index];
        return sprite is not null;
    }
}
