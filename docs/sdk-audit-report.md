# SDK audit report — `Mystia.Net.Sdk` → `Mystia.Extension.Sdk`

Status: findings only. No code changed by this report. The plan in §7 is a proposal; the decisions in §8 are open.

Scope: `src/Mystia.Net.Sdk` (~11.8k lines), `src/Mystia.Modding.Bridge` + `src/Mystia.Modding.Host` (~28k lines),
`src/Mystia.Net.Sdk.SourceGenerators`, `src/Mystia.Net.Sdk.Analyzers`, `src/Mystia.Net.Sdk.Tests`, `samples/*`,
`sdk/Mystia.Extension.Sdk`. Read-only, plus a cross-check of every SDK service member against the bridge seam
that implements it.

Method: one public-contract pass, one listener/wiring pass, one service-surface pass, one loader/packaging/sample
pass, one stage-steering pass, then a first-hand re-read of the claims that drive decisions (scene seams,
`SceneLoopHost`, `BridgeInstaller`, `CookSelectionSeams`, the sample signatures, the analyzer ban list).
Every claim below carries a `file:line`. Three items are marked **UNVERIFIED** because they cannot be settled
from this repository alone.

---

## 0. Verdict

| Goal | State | Verdict |
| --- | --- | --- |
| **G1** no internal / interop type reaches the extension user | `Mystia.Modding.*` (bridge/host internals) leaks nowhere; `Il2CppSystem.*` leaks in exactly one member; **game interop types are on ~30 public members** | ✗ half done — the handle/mirror layer exists but covers only part of the surface |
| **G2** every listener is pipeline style, wired by interface declaration | `[AutoWire]` + `Add<IFace>(impl)` + hand-written dispatch loops works and every one of the 36 interfaces has a dispatcher; `ref bool cancelInvocation` is honoured at all 30 interception sites. Defaults are missing on 30 members | ◐ mechanism right, contract incomplete |
| **G3** no API exists only for MetaMystia | A large slice of `IWorkSceneGuests`, `IWorkSceneChallengeServices` and `IChallengeListener` is specified in replay/peer/"another machine" terms; `ChatConfirmationKind` has one member; `MystiaKey` is one mod's hotkey list | ✗ needs a systematic restatement |

Naming: the package id, `README.md` and all three samples already say `Mystia.Extension.Sdk` (2.0.1). Only the
assembly, the project folders and the generator/analyzer/test project names still say `Mystia.Net.Sdk`. The
rename is the last step of a rename that is already half finished (`artifacts/nuget/Mystia.NET.Sdk.1.0.0.nupkg`
sits next to `Mystia.Extension.Sdk.1.0.0.nupkg`).

---

## 1. Rename surface

### 1.1 Runtime literals — breaking

| file:line | text | why it matters |
| --- | --- | --- |
| `src/Mystia.Modding.Host/ModLoader.cs:149` | `Path.GetFileName(path) != "Mystia.Net.Sdk.dll"` | the host excludes the SDK dll when picking a mod's entry assembly (`:138-155`). After a rename a folder with no `"assembly"` field matches two dlls → `InvalidOperationException` (`:151-153`); if the renamed SDK dll were the only dll it becomes the entry assembly → no `ModEntranceAttribute` → warn + skip (`:62-66`) |
| `sdk/Mystia.Extension.Sdk/Sdk/Sdk.targets:3-4` | `<Reference Include="Mystia.Net.Sdk">` + `HintPath ...\lib\net10.0\Mystia.Net.Sdk.dll` | every mod build binds this name |

### 1.2 Already-built mods

A mod compiled today references assembly identity `Mystia.Net.Sdk`. Its folder ships that dll
(`Sdk.targets:5` `Private=true`, `Sdk.props:9` `CopyLocalLockFileAssemblies=true`). `ModAssemblyResolver` binds
by **file name only, no version** (`ModAssemblyResolver.cs:66-69`), so after the rename the host loads
`Mystia.Extension.Sdk.dll` while the mod's IL asks for `Mystia.Net.Sdk` → the marker binding a mod folder finds
the mod's own copy → `IMod`/`ISceneListener` are **different types than the host's**, and registration silently
no-ops. Options are in §8.1.

### 1.3 Build / packaging / test references

`src/Mystia.Net.Sdk/Mystia.Net.Sdk.csproj:4-5` (`AssemblyName`), the same pair in
`Mystia.Net.Sdk.SourceGenerators.csproj:4-5` and `Mystia.Net.Sdk.Analyzers.csproj:4-5`;
`sdk/.../Sdk/Sdk.targets:3-4,7-8`; `sdk/.../Mystia.Extension.Sdk.Pack.csproj:9-11,15-17`;
`MystiaExtensionFramework.slnx:10-12`;
consumer `ProjectReference`s in `Mystia.Modding.Bridge.csproj:18`, `Mystia.Modding.Host.csproj:14`,
`Mystia.Net.Sdk.Tests.csproj:13-15`; `src/Mystia.CatalogGen/Program.cs:16` (writes into
`src/Mystia.Net.Sdk/Catalog/GameIds.g.cs`); `README.md:56`; `docs/extension-api-plan.md:192`;
tests `CatalogTests.cs:74` (folder), and the assembly-name assertions `AssetTests.cs:70`,
`BuilderTests.cs:86`, `CharacterAssetTests.cs:71`, `DayMapTests.cs:86`, `ImguiTests.cs:64`,
`AnalyzerTests.cs:376`. `InternalsVisibleTo` entries name the *consumer* assemblies
(`Mystia.Net.Sdk.csproj:11,14,17`), so they move only if the test/host projects move.

### 1.4 Must NOT be renamed

Namespaces (`Mystia`, `Mystia.Scenes`, `Mystia.Listeners`, `Mystia.Data`, `Mystia.Assets`, `Mystia.Imgui`,
`Mystia.Numerics`, `Mystia.Spells`) — a mod's `using` lines must keep compiling. The source generator hard-codes
the metadata names `Mystia.AutoWireAttribute`, `Mystia.ModEntranceAttribute`, `Mystia.Generated`,
`Mystia.IModRegistrar` (`ModEntranceGenerator.cs:37,83,84,86,91`).

### 1.5 Packaging gaps found on the way

- The version is hardcoded in the nuspec (`Mystia.Extension.Sdk.nuspec:4-5` = `2.0.1`) and `Pack.csproj` packs
  through `NuspecFile` (`:6`), so `dotnet pack -p:Version=…` has **no effect**; re-packing silently overwrites
  the same nupkg in `artifacts/nuget`.
- No XML documentation file is generated (`Mystia.Net.Sdk.csproj` sets no `GenerateDocumentationFile`), so the
  extensive `///` docs never reach mod authors.
- The package carries no dependency group (`NU5128` suppressed at `Pack.csproj:8`) although the SDK itself needs
  `Il2CppInterop.Runtime` (`Mystia.Net.Sdk.csproj:26`) — a consumer gets no transitive reference and must add
  the package plus `MystiaInteropDir` by hand.
- Nothing pins a minimum host version, so the package cannot express API compatibility at all.

---

## 2. Goal conformance

### 2.1 G1 — interop types on the public surface

**(a) The mod must read or write them** — these are the real violations.

| public member | exposed type(s) | file:line | wrap cost |
| --- | --- | --- | --- |
| `IGuestDirector.Claim` (and it has **no default**) | `NightScene.GuestManagementUtility.GuestGroupController` | `GameApi/Listeners.cs:284` | hard: the mod's own implementation signature names a game MonoBehaviour |
| `IGuestControls.OrderFoodAndBeverage` / `.OrderByTag` | `GuestGroupController.EvaluationResult` | `GameApi/Listeners.cs:296,298` | trivial — `GuestEvaluation` already mirrors it |
| `ICookListener.OnPreCookStarted` / `.OnCookStarted` / `.OnCookExtracted` / `.OnCookStored` / `.OnCookCountdownStarted` | `CookController`, `Sellable`, `Recipe` | `GameApi/Listeners.cs:68-78` | medium |
| `IWorkListener.OnPreDishServed` / `.OnPreDishCancelled` / `.OnPreStorageExtracted` / `.OnDishSent` / `.OnStorageExtracted` | `Sellable` | `GameApi/Listeners.cs:122-151` | medium — `DishProxy` exists |
| `IPrepListener.OnConfigureUpdated` / `.OnFoodStored` | `IzakayaConfigure`, `Sellable` | `GameApi/Listeners.cs:98,112` | medium |
| `IWorkSceneCook.*`, `IWorkSceneStorage.Store(Sellable)`, `IWorkSceneTray.Receive`, `IWorkSceneDishes.DishOf` | `Sellable`, `Recipe` | `GameApi/SceneLoops.Work.cs:290,297,301,308,323` | medium — handle-based overloads already exist next to them |
| `IWorkSceneTime.SetMode` | `Common.TimelineExtestion.GameTimeManager.TimeMode` | `GameApi/SceneLoops.Work.cs:328` | easy mirror |
| `IWorkSceneEconomyServices.Edit*`, `IWorkMetricsListener.OnPre*Edit` | `EventManager.MathOperation`, `EventManager.ServeType` | `GameApi/SceneEconomy.cs:11,13`; `GameApi/Listeners.Metrics.cs:14-28` | easy mirror |
| `IPrepNightMapServices.Confirm`, `GuideMapView.SelectedSpot` | `Common.UI.GlobalMap.IGuideMapSpot`, `IzakayaLevel` | `GameApi/SceneLoops.PrepNight.cs:36`; `GameApi/Views.cs:85` | easy handle |
| `IScheduleListener.OnPreRewardProcessed`, `IDaySceneScheduleServices.ReplayReward` | `GameData.Profile.SchedulerNode.Reward` | `GameApi/Listeners.Schedule.cs:23`; `GameApi/SceneLoops.Day.cs:49` | medium |
| `IMissionListener.OnMissionFinishStatesUpdated` | `RunTimeScheduler.TrackedMissionData` | `GameApi/Listeners.Mission.cs:11` | medium |
| `PrepConfigView.CloseWithFadeToken` | `Il2CppSystem.Threading.CancellationToken` | `GameApi/Views.cs:148` | trivial opaque token |
| `ICommonServices.LoadScene`, `IDayListener.OnSceneChanging` | `Common.UI.Scene` (game enum, *not* UnityEngine) | `GameApi/SceneLoops.cs:85`; `GameApi/Listeners.cs:36` | trivial → `SceneId` |
| `ICommonServices.OpenDialog` (3-arg), `IDialogCatalog.TryResolve`, `IDayListener.OnDialogOpened` | `GameData.Profile.DialogPackage` | `GameApi/SceneLoops.cs:87,89`; `DialogCatalog.cs:9` | pass-through asset handle; the only genuine `Il2CppSystem.*` leak is the `Action<Il2CppSystem...Dictionary<int,string>>` the mod must **construct** |
| `PresentationServices.cs:61,69,105`, `IAssetFactory.cs:126,203,216`, `CharacterServices.cs:237` | `object` / `object?` | see files | unpoliceable: the signature does not say what is legal, and no analyzer can check a call site |

**(b) Already acceptable** (inert handles the mod only passes back): `SpriteHandle`, `AudioClipHandle`,
`TextureHandle`, `AssetBundleHandle`, `DayMapHandle`, `CharacterHandle`, `TransformHandle`, and the
`Mystia.Numerics.*` mirrors, which are engine-free by construction (`NumericsUnity.cs` is `internal`).

**Internal contradiction to resolve.** The analyzer bans *any* `UnityEngine.*` use in a mod
(`BanList.cs:79-80` → MYSTIA1004), and the SDK is in fact free of `UnityEngine` in its public signatures
(only `GameApi/NumericsUnity.cs`, `internal`, names it). But `docs/extension-api-plan.md:9` states the opposite
design rule — "Private game members are reachable through the generated interop shell, so the framework passes
game objects instead of inventing per-mod views". G1 and that rule cannot both stand.

### 2.2 G2 — pipeline wiring

The mechanism is sound and needs no redesign:

- `ModEntranceGenerator.cs:53-110` collects `[AutoWire]` interfaces, requires a public parameterless ctor
  (MYSTIA001), and emits `registrar.Add<IFace>(new Impl())` plus `[assembly: ModEntranceAttribute(...)]`.
- `ModLoader.cs:69-91` reads the attribute, invokes the static `Register(IModRegistrar)`, then calls
  `IInitialization.Initialize(IMod)` — the only interface reached outside `Dispatch`.
- `BridgeInstaller.cs:37-51` `Dispatch.Run<T>` walks `ModRegistry.GetInstances<T>()`; ~40 hand-written
  `foreach` loops invoke the members directly. No reflection on the invocation path.
- All 30 `OnPre*` are Harmony prefixes returning `!cancel`, and the loop never short-circuits, so every listener
  sees the previous one's verdict (`ListenerSeams.Work.cs:151-153`; `ScheduleSeams.cs:18-19`).
- All 36 `[AutoWire]` interfaces have a dispatcher; no dispatcher names a member that no longer exists.

Defects:

1. **Mandatory members.** 30 members have no default body, forcing empty overrides: all 7 scene game loops × 3
   (`SceneLoops.{Splash,Main,Day,PrepNight,Work,Staff,Result}.cs`), `IInitialization.Initialize`,
   `IIMGUIProvider.OnGui`, `IPortraitProvider.TryResolvePortrait`, `IGuestDirector.Claim`, `ISpell.SpellId`,
   `IModSaveHandler.OnModLoad` (`ModStorage.cs:53`), plus 6 of `IWorkListener` and 3 of `IPrepListener`.
2. **Event/callback style, not pipeline.** `IGuestDirector` → `IGuestDriver.Start(IGuestControls)` →
   `Seat(int,int,Action)` / `OrderFoodAndBeverage(int,int,Action<EvaluationResult>)` /
   `OrderByTag(…, Action<EvaluationResult>)` (`GameApi/Listeners.cs:282-303`). Also `ChatConfirmationView.Confirm`
   hands the mod a raw `Action` (`Listeners.Chat.cs:153`) and `IScheduleListener.OnPreDayEnd`/`OnPreAfterDayEnd`
   pass the game's `Action` through `ref` (`Listeners.Schedule.cs:25,27`).
3. **`ModRegistry.GetInstances<T>` matches by exact type** (`ModRegistry.cs:34`), while the public
   `IModRegistrar.Add<TContract>` lets a mod register under its own concrete type — such a registration is
   invisible to dispatch. `Add` also does not deduplicate (`:11-17`), so a double registration delivers every
   event twice.
4. **`Dispatch.Run` has no `try/catch`** (`BridgeInstaller.cs:37-41`) and neither does any dispatch loop, so one
   throwing listener aborts the remaining listeners and propagates into the game call. Containment exists only in
   `GlobalHost` (per-loop), `PortraitProviders`, `SaveSeams`, `ChatMenuPipeline`, `SpellPipeline` and
   `SceneLoopHost.Run` is **not** among them.

### 2.3 G3 — MetaMystia-shaped API

The pattern is consistent enough to name: a capability is specified not by what it does to *this* machine's game,
but by what a *peer* needs in order to re-enact another machine's roll.

| member | quoted evidence | file:line |
| --- | --- | --- |
| `IWorkSceneGuests.SpawnNormal(guests, request)`, `.SpawnSpecial(id, request)`, `.CreateOrder`, `.TryQueue`, `.ShowMood`, `.SetRepellable` | "the overload a machine replaying a spawn uses: the request is the one the rolling machine reported"; "the order object itself belongs to the machine that places it"; "A caller replaying a verdict another machine already gave needs exactly that"; "a machine that replays the first order calls it next to `BeginOrderSession`" | `GameApi/SceneLoops.Work.cs:96-98,137-140,199-200,213,220-221,229` |
| `IWorkSceneGuests.BeginOrderSession(…, string message)` | a `message` string rides along with group+order+outcome purely for a mirrored status line | `GameApi/SceneLoops.Work.cs:129,140` |
| `IGuestGroupListener.OnGroupQueuePatienceDepleted` | "a mod mirrors it to the other machines here, and replays the verdict it is told about with `StopPatientCountdown` and `GuestProxy.MoveToSpawn`" | `GameApi/Listeners.cs:232-233` |
| `IWorkSceneChallengeServices.ReplayFailure`, `.StopRun`, `.SwallowCooker`, `.TimedNegativeSpellEnabled`, `.BossOrderEnabled`, `.EarnedFund`, `.PositiveSpellCount` | "This is what a machine that was told the run failed does before it carries the failure out"; "the replay a peer that was told which cooker the boss ate runs"; "A machine that takes the phase's effects from another machine needs the spell not to land twice" | `GameApi/Challenge.cs:390,425-439,449-460` |
| `IChallengeListener.OnPreChallengeClockElapsed`, `.OnPreChallengeGuestSpawn`, `.OnPreBossEvaluated` | "so a mod that waits for another peer's decision releases it later"; "which is what a peer that spawns its own guests does"; "a peer that replays the ruling it was told" | `GameApi/Challenge.cs:526,534,568-569,577` |
| `IChallengeListener` + `IWorkSceneChallengeServices` as a whole | the contract is defined against one boss's compiled state machine: `YuyukoBossData.<MainChallengeLoop>d__16.MoveNext`, with `ChallengeStep.Phase1Settled = 4` … `Phase3EndingPreparingRetake = 15` as hard-coded resume offsets | `GameApi/Challenge.cs:152,160-178` |
| `ChatConfirmationKind` | a single member, `YuyukoChallenge` | `Listeners.Chat.cs:116-123` |
| `MystiaKey` | "Only the keys a mod actually polls are listed; a member is added as a need for it appears" — 11 of ~320 `KeyCode`s | `GameApi/InputServices.cs:4-8` |
| `IWorkSceneEconomyServices.EditTip(…, comboBuff, moodBuff, extraBuff)` | four float factors mirroring one game edit call | `GameApi/SceneEconomy.cs:12` |
| `IScheduleListener.OnReimuProtectionEntered` / `.Exited` | one character's scripted money-box window promoted to a first-class pair | `Listeners.Schedule.cs:28-38` |
| `ICommonServices.Characters` doc | justified by "where a console command lives, and where a mod's network state arrives" | `GameApi/CharacterServices.cs:19` |

Note that two only-one-consumer arguments exist and are *not* the same thing: (i) members that exist **to
synchronise machines** (the rows above), and (ii) members that are merely **undiscovered-complete**, i.e. the
surface is the union of what one mod has needed so far (`GuestHandle.cs:14-18` "the fifteen callbacks … left for
the slice that needs them"; `Assets/**`, `ImguiKey`, `ImguiEventKind`, `ImguiScaleMode`, `TextStyleHandle` are
all explicitly on-demand). (i) is what G3 forbids; (ii) is what §3 answers.

---

## 3. Incomplete API

### 3.1 Listeners and notifications

- **`ISceneListener` is asymmetric.** `OnSceneAwake` is dispatched only for Splash/Main/Day
  (`HarmonySeams.cs:32,50,60`) and `OnSceneStart` only for Splash/PrepNight/Night/Staff/Result
  (`:40,78,88,98,108`). **Main and Day never receive `OnSceneStart`; PrepNight/Night/Staff/Result never receive
  `OnSceneAwake`** — although `SceneLoopHost.Enter` runs for all seven in the same callbacks. There is no
  scene-exit notification at all, so a global loop cannot learn that a scene unloaded.
- **Splash's `OnSceneAwake` can be dispatched twice per run** (`RuntimeInstall.cs:129` and
  `HarmonySeams.cs:32`); the `_singletonLogged` guard at `RuntimeInstall.cs:124-128` protects only its own path.
- **`ChatOptionKind` has 9 values, 2 are reachable.** Only `FreeChat` and `Shop` are hooked
  (`ChatSeams.cs:22-25,40-61`); Mission / DynamicMission / Invite / RequestIngredient / RequestBeverage /
  Commission / Exit are never reported, and the code comment says so.
- **Notifications with no interception where the game call site already has a patch**: `OnGroupSpawned`
  (`HarmonySeams.cs:437-447` — the bridge already returns `false` there), `OnGroupMovingToDesk`
  (called from a *prefix*, `ListenerSeams.Day.cs:65-66`), `OnGroupSeated`, `OnGroupArrived`, `OnGroupQueued`,
  `OnIzakayaClosing` (`HarmonySeams.cs:660-663`), `OnServePanelOpened`, `OnOrdersRefreshed`,
  `OnTimelineDirectorPlayed`, `OnCookExtracted`, `OnCookStored`, `OnFoodStored`, `OnDialogOpened`,
  `OnSceneChanging`, `OnInteracted`.
- **Start without end**: `IWorkSceneTime.BeginTiming` / `.SetTimingEnabled` (no stop/end),
  `IWorkSceneIzakaya.Close()` (no open), `IWorkSceneCook.Start` / `.StartCountdown` (no cancel/stop),
  `IWorkSceneGuests.ShowMood` / `.SetRepellable` (setters with no clear), `IDayListener.OnDayMapEntered`
  (no map-exit), `IWorkListener.OnTimelineDirectorPlayed` (no "started").
- **Post without pre**: `OnRecipeRemoved` / `OnBeverageRemoved` / `OnCookerRemoved`, `OnFoodStored`,
  `OnShopPannelOpened`, `OnHudOpened`, `OnTimedNegativeSpellSuppressed`, `OnChallengeFailureStarted`,
  `OnIzakayaClosing`.
- **`OnReimuProtectionEntered`/`Exited` can be unbalanced**: prefix/postfix on a compiler-generated local
  function with no finalizer (`ScheduleSeams.cs:192-198`), so a throw inside the body skips `Exited` — unlike the
  leave seams, which pair through `__state` + `Finalizer` (`HarmonySeams.cs:529,547`).
- **Duplicate reporting**: order generation is reported by both `OnGroupOrdered` (`HarmonySeams.cs:478,679`) and
  `OnGroupOrderGenerated` (`ListenerSeams.Day.cs:78`); `OnConfigureUpdated` fires from two patches
  (`ListenerSeams.Work.cs:325,332`); `OnGroupSpawned` carries `default(GuestSpawnRequest)` on every path where
  `SpawnRequestHold` was not armed (`SpawnSeams.cs:22-26`, `GuestPipeline.cs:16`).

### 3.2 Services that expose half of the game operation

| what is missing | what the game offers | what the SDK exposes | evidence |
| --- | --- | --- | --- |
| every gate is write-only | 14 independent `StockGate` flags + `EventManager.ShouldGuestSpawn` | 14 `Set*Enabled(bool)`, **zero getters** | `StockGate.cs:6-27` vs `SceneLoops.Work.cs:82-90,295,321,348`; `SceneLoops.Day.cs:38,48,50`; `PrepNight.cs:33,55` |
| economy is edit-only | `EventManager` fund / tip / experience / passion values | 4 relative edits, no read | `SceneEconomy.cs:9-20`; `EconomyServices.cs:14-45` |
| night clock | current `TimeMode`, elapsed and remaining night time | `SetMode` write-only; only the *total* length and a spawn/timing gate; remaining time exists only inside the challenge service | `SceneLoops.Work.cs:326-343`; `TimeSeams.cs:52-80`; `Challenge.cs:314` |
| storage | store **and** extract dishes | `Store` only; extraction is observe-only | `SceneLoops.Work.cs:308,316`; `ListenerSeams.Work.cs:118-125` |
| tray | receive and read/serve off the tray | `Receive` + a close gate | `SceneLoops.Work.cs:321-323` |
| cook | cooker state, countdown, extract-vs-store | 4 commands, unknown index throws, no query | `SceneLoop.Work.cs:295-303` |
| QTE | full QTE lifecycle + score | apply-reward / trigger-buff only | `SceneQte.cs:12,15` |
| buffs | register / extend / end | register / extend / limit, nothing ends a buff | `SceneBuffs.cs:15-25` |
| chat selection panel | `OpenAfterChatMenu(callbacks, key, endButton, …, HideVisual)` | `Open` / `Close` with the other args pinned; no enumerate, no refresh | `ChatSelectionMenus.cs:153-161` |
| dish construction | the game builds `Sellable`/`Recipe` | **no factory at all** — no `CreateDish`, no `Sellable` builder anywhere in `Assets/**` | grep: absent |
| `ICharacterServices.WalkCharacter` | `SceneDirector.MoveCharacter(label, waypoints[], speed, onArrive)` | one position, **arrival callback dropped** (`new Action(static () => {})`) | `CharacterServices.cs:37-56` |
| `TryGetCharacterPosition` | position incl. depth; `SetCharacterZ` writes z | read returns `Vector2` only → a mod cannot read back the z it wrote | `CharacterServices.cs:120` vs `:209` |
| free-seat query | `GuestGroupController.CanQueue(int count)` is static, needs no group | only `CanQueue(GuestHandle)` | `SceneLoops.Work.cs:191` |
| `SpawnSpecial` | `GuestSpawnType` enum + post-process callback | pins `GuestSpawnType.Normal` and the game's own post-process | `GuestServingServices.cs:80-90,114-126` |
| `SpawnNormal(guests, desk)` | 5-arg spawn takes position, leave type, desk, fade | the 2-arg overload hard-pins `LeaveType.Move`, `fade:true` | `GuestServingServices.cs:65-75` |
| Result / Staff / Splash scenes | result panel, staff flow, splash flow | `Common` + `Presentation` only | `SceneLoops.Result.cs:19-24`; `SceneLoops.Staff.cs:19-24` |
| day scene | map/NPC/marker state, time of day | `Swap` / `RefreshSpawnMarkers` / `End` / `Chat` / `ReplayReward` + 3 input gates, no reads | `SceneLoops.Day.cs:38-65` |
| `IDatabaseExtension` | game data tables | injection only (19 `OnInject*`), no read-back of injected or game data | `GameApi/Database.cs:902-938` |
| `IGameDataBuilder` | dialog / mission / event objects | build + `IsDialogBuilt`/`IsNodeBuilt`; no edit, unregister or enumerate; timeline events refused outright | `IGameDataBuilder.cs:48-98`; `SchedulerNodeSpec.cs:33-38` |
| `IAssetFactory` | textures, sprites, audio | no prefab / material / mesh / animation / 9-slice constructor | `IAssetFactory.cs:63-200` |
| `IGuestRecords.IsIgnored` | `StatusTracker.IgnoredGuests` is a mutable collection the bridge already reads | read-only — no member adds or removes an ignored guest | `GuestRecords.cs:5-13`; `ChatSeams.cs:82` |
| `IModStorage` | a mod's own directory | config + cache files, no directory listing, no config delete | `ModStorage.cs` |
| `IPresentationServices.TryBindCharacter(GuestHandle,int,out CharacterHandle?)` | **dead member**: returns `false`/null by default and has **no bridge implementation** while the docs advertise it as the typed way to a guest's character | `PresentationServices.cs:99,120-124` |

### 3.3 Enums

- **Collapse distinct game states** — the bridge's conversion switches all end in a `_ =>` default arm that
  silently folds anything unknown, which contradicts the "value for value" remark on the mirror:
  `DishKind` ← `Sellable.SellableType` → `Food` (`EntitySeams.cs:134-138` vs `DishHandle.cs:13`),
  `GuestKind` → `Normal` (`:52-56`), `GuestLeaveType` → `Move` (`:58-72`), `OrderKind` → `Normal` (`:94-98`),
  `PartnerOrderContext` → `Null` (`:118-132`), `GuestEvaluation` → `None` (`:74-82`),
  `OrderGenerationOutcome` → **`Succeed`** (`:100-107`) — a failure mode is reported to listeners as a success.
- **No pinned numeric values although the value is written into a game enum**: `SpeakerKind`, `DialogSide`,
  `DialogActionKind`, `CharacterRotationKind`, `CookerKind`, `GoodsKind` (`GameApi/Database.cs:24,32,39,52,5,70`).
  `CookerKind` and `GoodsKind` have **no bridge conversion or startup check at all**; the
  counter-example is `Challenge*` (`Challenge.cs:14-99`) and `NodeEventTypes` (`SchedulerNodeSpec.cs:28,34,41`),
  which carry explicit values.
- **Truncated by design**: `MystiaKey` (11 of ~320 `KeyCode`s, `InputServices.cs:4-8`),
  `ImguiKey` (5 keys, `ImguiEvent.cs:35`), `ImguiEventKind` (6 + `Other`; no mouse-button id, so right-click is
  indistinguishable from left-click, `ImguiEvent.cs:59-83`), `ImguiScaleMode` (**1** value; any other throws
  `ArgumentOutOfRangeException`, `TextureHandle.cs:4`), `ChatOptionKind` (9 declared / 2 reachable),
  `ChatConfirmationKind` (1), `PortraitTarget` (2 collapsed panels, `Listeners.cs:247-256`).
- **Two enums for one game concept**: `Mystia.Listeners.GuestLeaveKind` (`Listeners.cs:158`) and
  `Mystia.Scenes.GuestLeaveType` (`GuestHandle.cs:28`) are used side by side
  (`OnGroupLeft(…, GuestLeaveKind)` at `Listeners.cs:239` vs `GuestProxy.FinalLeaveType` at `GuestHandle.cs:238`).
- **Typed-as-`int` pseudo-enums** in the data face (`RewardType`, `ObjectType`, `ConditionType`, `MissionType`,
  `MissionFailedAction`, `EventLockMode`, `DayType`, `CalcType`, `TriggerType`) — the game enum lives only in a
  doc comment (`GameApi/Database.cs:686,714-720,767,826`; `SchedulerNodeSpec.cs:83,105,191`).
- **`SceneId` is complete for the loop dispatcher** but `StockGate.Reset` only handles Day/PrepNight/Night
  (`StockGate.cs:50-77`), so gate resets silently skip the other four scenes.

### 3.4 Scoping and lifetime

- `ICommonServices` is documented "valid from a global loop, from a scene loop, and from a background thread"
  (`SceneLoops.cs:7-10`) yet six members default to `throw new NotSupportedException()`
  (`:56,75,101,134,150,153,156`) and several more default to `false`/`null`. Any third-party or test host
  therefore breaks a promise the contract makes.
- **Scene services are process-wide singletons, not per-session objects** (`WorkSceneServices.Shared`
  `SceneServices.cs:445`; `WorkSceneGuestServing.Shared` `GuestServingServices.cs:27`; `DaySceneServices.Shared`
  `:237`; `CommonServices.Shared` `:47`). A `services` reference kept from night #1 silently acts on night #2;
  only entity handles are session-checked (`EntitySession.cs:28-36`).
- `ServiceScope.Require()` throws outside `Setup`/`Update`/`Shutdown` (`SceneLoopHost.cs:14-20`) and is called
  first in ~60 members, including `MainSceneSessionServices.GotoDay` (`SceneServices.cs:226-231`) and the
  prep-night members, whose wrapped actions are not inherently scene-bound.
- `SceneLoopHost` pairing hazards: a loop registered after `Enter` gets no `Setup` yet still receives `Shutdown`
  (`:66-111`); a listener throwing during `Setup` aborts the rest of the phase after `_active` was set; `Shutdown`
  clears `_active` **before** dispatching (`:52-53`) so a throw there skips `CoroutinePump.LeaveScene()` (`:57`)
  and `EntitySession.Rotate()` (`:62`) — there is no `finally`; `SceneLoopHost.Reset()` has **no production
  caller**, so the active scene's `Shutdown` never runs at process exit.
- Silent no-ops instead of refusals: `IWorkSceneStorage.Store(DishProxy)` no-ops for a non-`Sellable`
  (`SceneServices.cs:527-532`), `ICharacterServices.SetCharacterColliderEnabled` reports `false` for a
  collider-less character (`CharacterServices.cs:265-280`), `UiNavigationEnabled` reads/writes nothing without an
  `EventSystem` (`SceneServices.cs:133-141`).
- `ICommonServices.Clock` publishes unscaled time only (`ClockServices.cs:17-20`).

---

## 4. Correctness defects (independent of the three goals)

1. **A dropped write-back.** `OnPreCookingSubmit` builds a local `CookingRequest` and hands it by `ref`, but the
   value is never read again: `return !cancelInvocation;` (`CookSelectionSeams.cs:32-35`). Every
   `Recipe` / `IngredientIds` rewrite from a listener is silently discarded; only `cancelInvocation` works.
2. **A cancelled action still reports its completion.** HarmonyX runs sibling postfixes after a prefix returns
   `false`, and the repo relies on that in both directions (`LeaveDispatch.cs:24-26`; `SaveSeams.cs:121-124`;
   `ChallengeSeams.cs:104,108` use `__runOriginal` guards or a `ran` flag). **No listener pipeline postfix uses
   such a guard**, so `OnServeFinished` (`HarmonySeams.cs:351`), `OnDishSent` (`:372`), `OnStorageExtracted`
   (`:380`), `OnTimeModeChanged` (`:387`), `OnCookStarted`/`OnCookCountdownStarted` (`:193,:214`),
   `OnRecipeAdded`/`OnBeverageAdded`/`OnCookerAssigned` (`:260,:266,:273`), the four `On*Edited`
   (`MetricsSeams.cs:26,51,66,81`), `OnQteSucceeded` (`QteSeams.cs:33`) and `OnGroupLeft(…, PlayerRepelled)`
   (`HarmonySeams.cs:604`) all fire for actions the game never performed. `OnServeFinished` additionally drops
   the cached panel view on that path (`HarmonySeams.cs:352`).
3. **Dead interception payloads**: `OnPreQteSucceeded` computes and drops `reward.Buff` (`QteSeams.cs:23-28`);
   `OnPreBossEvaluated` writes `Result`/`ComboProtect` back but only applies `Message`/`DamageMultiplier` on the
   cancel path (`ChallengeTimeline.cs:667-679`); `OnPreSpawnNormalGuests` cannot change *which* guests spawn
   because the 5-arg overload's guest enumerable is not a patch parameter (`SpawnSeams.cs:42-53`);
   `OnPreRecipeAdded`/`OnPreBeverageAdded`/`OnPreCookerAssigned` take ids **by value**, so only cancellation
   reaches the game (`ListenerSeams.Work.cs:278,290,302`).
4. **Cancellation with a replacement is half-applied**: `OnPreDayEnd`/`OnPreAfterDayEnd` write the replacement
   into the argument and then never use it when cancelled (`ScheduleSeams.cs:139-141`).
5. **Dual prefixes with no `[HarmonyPriority]`** → undefined relative order between a notification and a gate on
   the same member: `ListenerSeams.Work.cs:71` vs `StockHolds.cs:241`; `StockHolds.cs:76` vs
   `HarmonySeams.cs:586`; `SpawnSeams.cs:63` vs `GuestSpawnPipeline.cs:142`.
6. **Leftover hot-path logging**: a host.log line on every `set_Sprint` call (`HarmonySeams.cs:420`).
7. **Name pinning is inconsistent.** Some seams are compile-pinned (`typeof`/`nameof`:
   `ChatSeams.cs:41-46,60-67,130-134`; `HarmonySeams.cs:220,246`) so a game update breaks the build; others are
   string-pinned with **no runtime verification** (`ListenerSeams.Day.cs:51-52`, duplicated verbatim in
   `StockHolds.cs:44-48`) so a stale name installs nothing and only reaches host.log
   (`GameBridgeHook.Install` `:67-71`). Only `ReimuProtectionWindow` verifies its interop name
   (`ScheduleSeams.cs:170-184`).
8. **`IPortraitProvider` targets, `PartnerOrderContext`'s game member list and the `LogLevel`↔game-log binding
   are UNVERIFIED** from this repository; each may hide further collapsed values.

---

## 5. Host, loader and layout

- **`loadAfter` is parsed and never read** (`ModManifest.cs:12`; `ModOrder.cs:5-30` is not a topological sort).
  `README.md:58` documents this as intentional.
- **No API-version pinning between mod and host**: the host version is only reported (`HostServices.cs:73`), a
  mod's `version` is only logged (`ModLoader.cs:79`).
- **Analyzer enforcement is compile-time only.** MYSTIA1001-1006 are all errors in `Sdk.targets:8` packaging,
  but the host's `FindBannedReferences`/`WarnOnBannedReferences` (`ModLoader.cs:112-136`) only warns, and its
  list (`ModLoader.cs:15-22`, which contains a stray `"MonoPlus"`) diverges from `BanList.cs:33-38`.
- **Malformed manifest or dll errors are host-fatal**: blank/duplicate ids throw outside the per-mod try
  (`ModOrder.cs:9,12`); `Register`/`Initialize` failures are caught per mod and the mod is skipped
  (`ModLoader.cs:84-86`).
- **`assembly` in `mod.json` has no containment check** (`..\` or an absolute path escapes the mod folder,
  `ModLoader.cs:138-155`); dependency resolution is sibling-directory probing by file name only, no version
  (`ModAssemblyResolver.cs:66-69`); no sandbox, no unload (`ModRegistry` has no `Remove`).
- **Mod ordering is `modOrder` → mod id ordinal → class name → interface name** (`ModOrder.cs:19-27`;
  `ModEntranceGenerator.cs:53-79`), so `BridgeInstaller`'s "in player order" is accurate only for mods listed in
  `modOrder`.
- **The bridge ships no seams without interop**: all of `Bridge/Game/*.cs` is `Compile`-conditioned on
  `MystiaInteropDir/Assembly-CSharp.dll` (`Mystia.Modding.Bridge.csproj:31-32`); without it the bridge is
  `GamePatches.Stub.cs:11-25` and dispatch delivers to nothing.
- **Two build prerequisites make the repo un-buildable from a clean clone**: `artifacts/interop/Assembly-CSharp.dll`
  and `artifacts/` is gitignored (`.gitignore:3`).

---

## 6. Test + sample status, and what an in-game test mod needs

### 6.1 Today

- `src/Mystia.Net.Sdk.Tests` (24 xunit files) runs entirely **in process**: Roslyn in-memory compiles for the
  analyzer/generator, temp directories for the host/storage, fake proxies for the entity layer, managed-only
  Harmony. It never touches the il2cpp runtime or the game. It also cannot even build without the interop
  directory, and `HostAndGuardTests.cs:112,135` loads the **prebuilt** sample dlls from
  `samples/*/bin/Debug/net10.0`, so `dotnet build samples/...` must run first. **There is no in-game harness and
  no self-test mod anywhere in the repo.**
- `samples/SampleMod.A` **compiles but its only handler is dead code**: `SceneLog.cs:17-19` declares
  `OnGroupSpawned(NightScene...GuestGroupController, GuestSpawnRequest)` while the current interface member is
  `OnGroupSpawned(GuestHandle, GuestSpawnRequest) { }` (`Listeners.cs:209`). Because the interface member has a
  default body there is **no CS0535**, so the sample silently implements nothing and is never invoked.
  `SampleMod.B` and `SampleMod.Skip` are current.

### 6.2 Scenario → required steering → what exists

| scenario | required steering | SDK today |
| --- | --- | --- |
| skip splash / logo | force or skip the splash stage | **MISSING** (`SceneLoops.Splash.cs:15-19`) |
| new game / choose a save slot | drive the menu | **MISSING** (`SaveManagement.LoadPlayerData` is observe-only, `SyncSeams.cs:20-24`) |
| main → day | `GotoDay()` | ✓ (`SceneLoops.Main.cs:26`) |
| load an arbitrary scene | `LoadScene` | ◐ exists but takes the game's `Common.UI.Scene`, bootstraps no stage state (`SceneLoops.cs:85`) |
| day → prep/night | `Schedule.End()` + gate | ✓ (`SceneLoops.Day.cs:40-44`) |
| suppress the day→night dialog | `SetNightTransitionEnabled` | ✓ (`SceneLoops.cs:120`) |
| advance the day clock | day time-of-day | **MISSING** |
| prep: map / level / confirm | `PrepNightMapServices.Confirm` | ✓ (`SceneLoops.PrepNight.cs:31-37`) |
| prep: recipes / beverages / cookers | `IPrepNightMenuServices` | ✓ (`:40-50`) |
| prep → night | `Session.ToWork()` + gate | ✓ (`:53-59`) |
| night: length / start / mode | `WholeNightSeconds`, `BeginTiming`, `SetMode` | ✓ write side (`SceneLoops.Work.cs:326-343`) |
| night: **read** time left | countdown | **MISSING** (challenge only, `Challenge.cs:314`) |
| night: deterministic spawns | `SpawnNormal` / `SpawnSpecial` | ✓ (`:93-112`) |
| night: natural spawn cadence | interval / flow rate | **MISSING** (only on/off) |
| night: spawn rewrite | `IGuestSpawnModifier.OnPre*` | ◐ (params yes, guest list no — §4.3) |
| night: seat / queue / order / evaluate / leave | guests service + interceptions | ✓ (`:84-131`) |
| night: close the izakaya → result | `Izakaya.Close` | ✓ (`:346-360`) |
| reach Staff / Result | the game's own managers only | **MISSING** — observe only (`HarmonySeams.cs:93,103`) |
| read money / popularity / day number | economy totals | **MISSING** (edit-only) |
| read the loaded stage | a `SceneId` getter | **MISSING** — `SceneLoopHost.Active` is `internal` (`SceneLoopHost.cs:37`) |
| report results | a writable sink | ✓ `IModStorage`, `ILog`, `IIMGUIDrawer`, floating labels |

### 6.3 Hard blockers for an automated in-game run

1. No current-stage read (`SceneLoopHost.Active` is `internal`).
2. No new-game / save-slot / splash-skip: an automated run cannot bootstrap a session by itself.
3. No day clock and no readable night countdown: "advance time in a stage" is not a capability.
4. Staff and Result are unreachable by any mod-driven transition.
5. No economy or inventory read-back, so economy assertions are limited to per-edit deltas.
6. No guest-flow-rate control, so the natural roll cannot be slowed or driven deterministically.
7. Prep/map "presses" ride on compiler-generated method names
   (`_OnGuideMapInitialize_b__21_0`, `_SolveDailyCompletion_b__64_7`; `SceneServices.cs:239-433`,
   `SessionHolds.cs:13`), guarded by `NamedSeams` — a mismatched build silently disables that stage's steering.
8. No driving of the game's own UI or input: `IInputServices` is read-only hotkey polling
   (`InputServices.cs`), and the analyzer forbids `UnityEngine`, so a test mod must use its own IMGUI.

---

## 7. Proposed plan (open for discussion)

Phase 0 — settle §8. No code.

Phase 1 — rename. `src/Mystia.Net.Sdk` → `src/Mystia.Extension.Sdk`, `AssemblyName` likewise; generator and
analyzer assemblies to `Mystia.Extension.Sdk.SourceGenerators` / `.Analyzers`; test project likewise. Namespaces
unchanged. Fix `ModLoader.cs:149`, `Sdk.targets:3-4,7-8`, the pack project, the `.slnx`, `CatalogGen`, the six
tests that hardcode the name. Decide §8.1 (compat facade vs. forced rebuild) and §8.2 (version + single version
source). Ship the XML doc file and a dependency group while the packaging is open.

Phase 2 — conformance.
 2a. Give every listener member a default body except where the member *is* the interface's purpose
 (`IInitialization.Initialize`, `IIMGUIProvider.OnGui`, `IPortraitProvider.TryResolvePortrait`, `ISpell.SpellId`,
 `IGuestDirector.Claim`). Decide the fate of the scene game loops (all-default vs. mandatory).
 2b. Finish the mirror/handle migration for §2.1(a) rows — one row per commit, each with the bridge seam and the
 affected listener updated together. Decide the escape hatch for `DialogPackage` and the `object`-typed members.
 2c. De-MetaMystia the replay cluster (§2.3): restate each member as a general capability
 ("place this order", "drive this group into the queue", "replay a decided verdict") with neutral names and docs
 that do not mention peers or machines; split anything that exists *only* to reconcile two machines into an
 explicitly opt-in surface. Decide whether the challenge contract (`ChallengeStep` resume offsets) stays public.
 2d. Complete or replace the truncated enums; add value-parity startup checks for `CookerKind` / `GoodsKind` /
 `CharacterKind`; remove the `_ =>` fold on read by refusing an unknown value loudly.

Phase 3 — completeness, batched by surface, each batch with reads *and* writes: gate getters; economy reads;
night/day clock reads; storage extract + enumerate; tray read; cook query; QTE start/read; buff end; chat option
coverage; `ISceneListener` symmetry + a scene-exit notification; `TryBindCharacter` (wire or delete); the
`CreatedDish` factory. Then the correctness defects of §4 (cancel-then-post guard in every postfix; the dropped
`OnPreCookingSubmit` write-back; `HarmonyPriority` on dual prefixes; the hot-path log).

Phase 4 — the two mods.
 - **Example mod**: a small, documented reference covering one of each surface — scene loop, listener,
 interception, service, coroutine, storage, asset, IMGUI, data injection, one injected catalog entry. Replaces
 the stale `SampleMod.A` (or fixes it) so `samples/*` all compile against the current surface.
 - **Test mod**: an in-game conformance harness inside the repo, built against the **public SDK only** (that
 constraint is what makes it a real conformance test). Shape: one `[AutoWire]` implementation per interface
 forwarding every member into a recorder; a scenario engine that runs one script per stage and asserts on the
 recorder plus the SDK's own reads; a stage driver that advances the game through its own transitions; a result
 sink writing JSON via `IModStorage` plus an IMGUI pass/fail overlay and a hotkey to start/stop. Blocks 1-7 of
 §6.3 then become work items in Phase 3 rather than test-mod hacks — which is the point.

Phase 5 — docs. Rewrite `docs/extension-api-plan.md` around the settled rules (drop the "passes game objects"
rule unless §8.3 keeps it), update `README.md`, add a CHANGELOG, and state the compatibility policy.

---

## 8. Open decisions

**8.1 Prebuilt-mod compatibility after the rename** — (a) hard break: bump to 3.0.0, have the host refuse a mod
whose assembly references `Mystia.Net.Sdk` (a loud skip instead of today's silent no-op), and keep both names in
the loader's exclusion list; (b) ship a `Mystia.Net.Sdk.dll` facade containing `[assembly: TypeForwardedTo]` for
every public type and put it in the host directory *and* the package. (a) is simple and honest; (b) is friendlier
but fragile, because `ModAssemblyResolver` binds by file name with no version and a mod's own shipped copy may win.
**Recommendation: (a)**, with the reference scan as a general API-version gate (§5).

**8.2 Version and version source** — 2.0.1 → 3.0.0, and move the version out of the nuspec into one MSBuild
property so `dotnet pack -p:Version=` works?

**8.3 How strict is G1?** — (a) *strict*: no game type in any public signature; the design rule at
`docs/extension-api-plan.md:9` ("the framework passes game objects") is dropped, `DialogPackage` becomes a handle,
the `object`-typed members get typed wrappers, and `LoadScene` takes `SceneId`. (b) *targeted*: wrap everything a
mod must **manipulate**, keep inert asset handles (`DialogPackage`, `SpriteHandle`) and document the rule as
"a value in, a handle out". (c) *documented exception*: keep game types but add an analyzer rule that a mod may
only name them in listener signatures. **This choice drives the size of Phase 2b more than anything else.**

**8.4 Where does the "replay/peer" surface go?** — delete it (the mod composes from general primitives), keep it
but restate it in general terms, or move it into an explicitly opt-in namespace/package. It is genuinely useful
to any mod that wants deterministic multi-instance behaviour, so deletion is a real cost.

**8.5 Does the challenge contract stay public?** — `ChallengeStep`'s resume offsets are one boss's compiled state
machine. Keep public (generalised), move behind the opt-in surface, or reduce to phase/clock and drop the steps?

**8.6 Scene game loops** — all three members default, or stay mandatory (they are the loop contract, and an empty
loop is meaningless)?

**8.7 `IGuestDirector` / `IGuestDriver` / `IGuestControls`** — convert to the listener pipeline, keep as the one
imperative hand-off, or remove?

**8.8 Test mod access** — public SDK only (so every gap becomes a real Phase 3 item), or may it also use a clearly
separated in-repo test harness that touches interop for boot-stage things (splash skip, new game, save slot) that
will never be general API? **Recommendation: public SDK only, and add the boot-stage capabilities to the SDK as
general members** — a "steer the game to a stage" capability is legitimate for any mod.

**8.9 Who runs the test mod, and when?** It cannot run in CI (needs Steam + the pinned game build). Is it a
human-triggered in-game run with a pass/fail overlay, and should the repo carry a recorded "expected report" to
diff against?

**8.10 Names and placement of the two mods** — `samples/ExampleMod` + `tests/TestMod`? Fixed mod ids
(`mefx.example`, `mefx.selftest`)? Should the test mod be built by the solution so it always compiles against the
current surface?
