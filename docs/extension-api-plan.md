# Extension API plan

Additions to the public contract, driven by porting a large existing mod onto this framework. Every item is meant for general use: no capability below exists only to serve one mod, and nothing below introduces multiplayer or mod-specific concepts.

Design rules applied here:

- Observation and interception points are `[AutoWire]` interfaces in `Mystia.Listeners`; the bridge runs them as a pipeline in player order.
- Interceptions are `void OnPreXxx(..., ref bool cancelInvocation)`. A value the original is about to consume is passed by `ref` so every implementation sees the previous one's change.
- Services are only for actions a mod initiates, plus host capabilities (storage, coroutines, logging, catalog queries).
- Handles and await tokens are opaque; a mod never touches Unity coroutine types.
- Private game members are reachable through the generated interop shell, so the framework passes game objects instead of inventing per-mod views.

## Host capabilities

`ICommonServices` gains members that are valid outside any scene loop:

```csharp
ICoroutineDispatcher Coroutines { get; }
IModStorage Storage { get; }          // OpenRead / OpenWrite / OpenText / CreateText / Exists / Delete(relativePath)
ILog Log { get; }
IDialogCatalog Dialogs { get; }     // enumerate package names, resolve a name to a DialogPackage
IGuestRecords Records { get; }      // RecordInvited / HasInvited / IsIgnored / Reset
void OpenDialog(DialogPackage dialog, Action onFinished, Action<IDictionary<int, string>>? replaceText = null);
```

`ILog` gains `Message`, `Fatal`, `Log(LogLevel, string)` and the module's `Id` / `Version`.

## Global loop and IMGUI

```csharp
[AutoWire] public interface IGlobalGameLoop
{
    void Setup(IGlobalServices services) { }
    void Update(IGlobalServices services, float delta) { }
    void FixedUpdate(IGlobalServices services, float delta) { }
    void Shutdown(IGlobalServices services) { }
}
public interface IGlobalServices { ICommonServices Common { get; } }

[AutoWire] public interface IIMGUIProvider { void OnGui(IIMGUIDrawer drawer); }
public interface IIMGUIDrawer { /* forwards GUI, GUILayout, GUIUtility, Event, Screen */ }
```

## Coroutines

```csharp
public readonly struct CoroutineHandle { }   // opaque
public readonly struct CoroutineAwait { }    // opaque, used as a yield value

public interface ICoroutineDispatcher
{
    CoroutineHandle Start(Func<ICoroutineDispatcher, IEnumerator> routine);
    CoroutineHandle StartOn(ICoroutineOwner owner, Func<ICoroutineDispatcher, IEnumerator> routine);
    void Stop(CoroutineHandle handle);
    void StopAll();
    CoroutineAwait NextFrame { get; }
    CoroutineAwait AfterSeconds(float seconds);
    CoroutineAwait AfterSecondsRealtime(float seconds);
    CoroutineAwait AfterFixedUpdate { get; }
    CoroutineAwait AtEndOfFrame { get; }
    CoroutineAwait Until(Func<bool> condition);
    CoroutineAwait While(Func<bool> condition);
    CoroutineAwait Nested(Func<ICoroutineDispatcher, IEnumerator> routine);
}
```

The host runs its own managed pump and interprets `null`, `CoroutineAwait`, the `YieldInstruction` waits, nested `IEnumerator` and `Il2CppSystem.Collections.IEnumerator`, and drops routines whose `StartOn` owner is gone.

## Listener pipeline

```csharp
public interface IDayListener    { void OnDayFirstEntered() { } void OnDialogOpened(DialogPackage package) { } }
public interface IDayInputListener { void OnMoveInput(CharacterControllerUnit unit, Vector2 direction) { } }
public interface IWorkListener
{
    void OnServePanelOpened(WorkSceneServePannel panel) { }
    void OnPreServePanelClosed(WorkSceneServePannel panel, ref bool cancelInvocation) { }
    void OnPreDishServed(WorkSceneServePannel panel, ref Sellable dish, ref bool cancelInvocation) { }
    void OnPreDishCancelled(WorkSceneServePannel panel, ref Sellable dish, ref bool cancelInvocation) { }
    void OnPreStorageExtracted(ref Sellable sellable, ref bool cancelInvocation) { }
    void OnPreTimeModeSet(GameTimeManager manager, ref GameTimeManager.TimeMode mode, ref bool cancelInvocation) { }
}
public interface ICookListener
{
    void OnPreCookStarted(CookController controller, ref Sellable result, ref Recipe recipe, ref bool cancelInvocation) { }
    void OnPreCookCountdownStarted(CookController controller, ref float qteScore, ref bool cancelInvocation) { }
}
public interface IPrepListener
{
    void OnPreRecipeAdded(int id, ref bool cancelInvocation) { }
    void OnPreBeverageAdded(int id, ref bool cancelInvocation) { }
    void OnPreCookerAssigned(int id, int index, ref bool cancelInvocation) { }
    void OnConfigTabSelected(IzakayaConfigPannel panel) { }
    void OnConfigureUpdated(IzakayaConfigure configure) { }
}
public interface IGuestGroupListener
{
    void OnGroupOrderGenerated(GuestGroupController group, GuestsManager.OrderGenerationResult result, ref GuestsManager.OrderBase order) { }
    void OnGroupArrived(GuestGroupController group) { }
    void OnGroupMovingToDesk(GuestGroupController group, int desk) { }
    void OnGroupQueued(GuestGroupController group) { }
}
[AutoWire] public interface IPortraitProvider
{
    bool TryResolvePortrait(GameData.Profile.ClothesProfile.Clothes clothes, out Sprite sprite);
}
```

## Services and switches

```csharp
IDaySceneMapServices : void Swap(string mapLabel, string markerName, int travelCount, Action onFinished = null);
IWorkSceneGuests     : void SetSeatingEnabled(bool enabled);
                       void BeginOrderSession(GuestGroupController group, GuestsManager.OrderGenerationResult result, GuestsManager.OrderBase order);
IWorkSceneCook       : void SetCallEnabled(bool enabled);
IWorkSceneIzakaya    : void SetCloseEnabled(bool enabled);
```

`SetLeaveEnabled` already covers every leave path (`PayAndLeave`, `ExBadLeave`, `RepellAndLeavePay`, `RepellAndLeaveNoPay`, `PlayerRepell`, `PatientDepletedLeave`, `LeaveFromDesk`) and `SetOrderingEnabled` already covers `GenerateOrderSession` and `GenerateOrder`, so no further gates are added for those.

## Data injection

New extension hooks:

```csharp
void OnInjectClothes(List<ClothesData> clothes);
void OnInjectSpells(List<SpellData> spells);
void OnInjectBuffs(List<BuffData> buffs);
void OnInjectMissionNodes(List<MissionNodeData> nodes);
void OnInjectEventNodes(List<EventNodeData> nodes);
void OnInjectDayMaps(List<DayMapData> maps);
```

New seams: `DataBaseNight.Initialize`, `DataBaseScheduler.Initialize`, `DaySceneLanguage.Initialize`. Injected merchant entries also get a runtime tracking record with generated products, a missing-key safe lookup, and the same owned-recipe filtering the stock game applies, so an injected merchant behaves like a stock one.

Structure additions: `NpcData.Name`/`Description` (written to the day-scene name table), `SpecialGuestData` extra flags plus a kizuna block, `GuestRequestLine.Enable`, `DialogLine` actions and per-line flags.

Mapping tables `FoodsMapping`, `BeveragesMapping` and `RecipesMapping` record the injecting module's own id.

## Tooling

`Mystia.InteropGen` accepts a Unity project's `Library/ScriptAssemblies` as the managed source and takes Unity base libraries from a separate directory, so interop can be generated without an IL2CPP symbols build.

## Delivered

Everything below the design sections is implemented, except where noted:

- Host: `IGlobalGameLoop`/`IGlobalServices`, `IIMGUIProvider`/`IIMGUIDrawer`, coroutines with a managed pump and opaque handles, `IModStorage`, `IDialogCatalog`, `IGuestRecords`, extended `ILog`, `IPlatformInfo` (not exposed yet; moves to `ICommonServices`).
- Listeners: session, status, mission, day/work UI, metrics, QTE, schedule, chat option/menu, cook selection, post-evaluation, guest spawn requests, leave dispatch for `LeaveFromDesk`.
- Services: economy (metrics edits + popularity tags), time (whole night seconds, timing gate), QTE, buffs, spell host, spawn marker refresh, reward replay.
- Data: clothes, spells, buffs, mission/event nodes, day maps and the extension seams they need; merchants now carry a full runtime pipeline.
- Spells: mods implement `ISpell`; the bridge wraps it in its own `SpellBase` subclass, drives the managed routine on the framework pump and enters the scene scope per resume step.

Still open: `PlayVfx`/`PlayAudio` are placeholders (a mod-scoped asset path context is missing), the spell declaration portrait pivot is carried by `SpellData` but not consumed yet, and `SceneLoops.cs` keeps scaffolding defaults for members whose inner implementations live in `SceneServices.cs`.
