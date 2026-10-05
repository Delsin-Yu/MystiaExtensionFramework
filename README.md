# Mystia Extension Framework

Unofficial mod middleware for Touhou Mystia's Izakaya. Mods ride inside a game that Steam started: `Mystia.Syringe.exe` installs a proxy DLL next to the game executable and then hands the launch to Steam. A game started any other way carries no mods.

This project does not ship the game, its assemblies, or generated interop. Those stay on your machine.

Supported build: Unity 2021.3.28f1. `GameAssembly.dll` SHA256 `91CE5AE3DAD5DA07DFED63BAB4C9E454F67B6E50F9A6E8EC498EF9B0B806A789`. A different game build is refused.

## Mod authors

Install the .NET 10 SDK. Reference the package `Mystia.Extension.Sdk`.

```xml
<Project Sdk="Mystia.Extension.Sdk/2.0.1">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <MystiaInteropDir>C:\path\to\artifacts\interop\</MystiaInteropDir>
  </PropertyGroup>
</Project>
```

`MystiaInteropDir` must point at interop assemblies generated from your own game install. The public scene, listener, and service APIs use game types, so the compiler needs those assemblies. This repository's `nuget.config` also looks in `artifacts/nuget` after you pack the SDK locally.

Generate interop and the id catalog by passing the game project directory. Interop also needs the Steam install directory. There is no default path.

```text
dotnet run --project src/Mystia.CatalogGen -- <game-project-dir>
dotnet run --project src/Mystia.InteropGen -- <game-project-dir> <game-install-dir>
```

The project directory is the game repository root. Catalog generation reads `Assets/_SortedAssets/DataBase`. Interop generation reads a `Managed` folder under `Build` (for example `Build/<game>_BackUpThisFolder_ButDontShipItWithYourGame/Managed`), and `GameAssembly.dll` plus `global-metadata.dat` from the install directory.

### A mod is a folder

A mod is a class library. Declare one type per behavior, mark the interfaces with `[AutoWire]`, and add `mod.json` next to the assembly (the host ignores a folder without one):

```json
{
  "id": "sample.a",
  "version": "1.0.0",
  "loadAfter": []
}
```

The folder has to hold the entry assembly and every DLL the mod needs at run time. When it holds more than one assembly, `mod.json` has to name the entry assembly:

```json
{
  "id": "sample.b",
  "version": "1.0.0",
  "loadAfter": [ "sample.a" ],
  "assembly": "SampleMod.B.dll"
}
```

Without `"assembly"` the host takes the only `*.dll` in the folder (`Mystia.Net.Sdk.dll` does not count) and refuses the folder when there is more than one. A dependency has to be copied into the mod directory: a class library does not copy the assemblies of its NuGet packages next to itself unless it is built with `-p:CopyLocalLockFileAssemblies=true`, which the SDK sets for the projects that use it. The host resolves a mod's dependencies from the mod's own directory (`ModAssemblyResolver`), so a dependency shipped beside the mod is found by the time the mod's own code binds it. A mod that fails to load is skipped and written to `host.log`; the mods beside it keep loading.

The build output directory (for example `bin/Release/net10.0/`) is the folder a player copies into `mods/`, so `mod.json` belongs there too - the samples copy it with `<None Include="mod.json" CopyToOutputDirectory="PreserveNewest" />`.

`loadAfter` is not an order. Players set the order in `launcher.json` with `modOrder`. Listed ids run in that sequence. A loaded mod missing from the array runs afterwards, sorted by id.

### Interop freshness

Game member names - including the compiler generated ones (`_b__N_M`, `<>c__DisplayClassN_M`, `_d__N`) - have to come from interop generated from the same `GameAssembly.dll` the player runs. The build numbers those names, so interop from another build still compiles and then hooks the wrong member. Regenerate after every game update:

```text
dotnet run --project src/Mystia.InteropGen -- <game-project-dir> <game-install-dir>
```

The generator writes `interop-manifest.json` into the output directory, recording the `GameAssembly.dll` and `global-metadata.dat` hashes it read. `BepInEx/interop` inside the install directory is usually older than the installed game and is not a source of names. A mod may not bind these names at all (analyzer MYSTIA1005); the bridge does, which is why its interop has to match the installed build.

### Value type layouts

Interop has to be generated with the tool in this repository, because the generator lays the value types out
afterwards. Il2CppInterop writes every generated value type as explicit layout and copies each field's offset
out of the input's `FieldOffsetAttribute` - the named `Offset` property Il2CppDumper's dummy assemblies carry -
and it never computes one from the fields. A managed backup of the game project carries no such attribute, and
a dump whose field offsets were not recovered carries none either, so every field of every generated struct
lands at offset 0 and the managed struct is as small as its largest field: Unity's `Color`, sixteen bytes in
the engine, is four bytes here. Any engine call that writes such a value back through a managed address - the
generated constructors hand `Unsafe.AsPointer(ref this)` to `il2cpp_runtime_invoke` - then writes past the
managed local and over its caller's frame, which the runtime reports as a stack cookie check failure
(`0xC0000409`, subcode 2) somewhere several frames away from the call that caused it.

`Mystia.InteropGen` lays those value types out itself: a type that has no offsets keeps the layout it was
given, a type whose fields all sit at 0 is laid out the way a sequential struct is (each field at the first
offset its own type's alignment allows, capped by the pack size), and the report printed at the end names the
types it could not size instead of guessing them. The pass only ever moves fields up, so a repaired type is
never smaller than the one it replaces. Every further shape the CLR refuses has its own pass: a type name
rendered through an instantiation that cannot exist, a pointer conversion through a constructor .NET Core
dropped, a `params` array default built as an array the parameter is not declared with, a `System.ValueType`
constraint on the generated mirror, and the `ComImport` flag a mirrored COM type cannot keep. Interop generated
before them is repaired in place, by every pass at once:

```text
dotnet run --project src/Mystia.InteropGen -- --repair <interop-dir>
```

Running it twice changes no byte, and a fresh generation from the same input needs no repair step at all.

Explicit layouts that overlap on purpose (a union, a native struct with padding) cannot be recovered from an
input that lost its offsets; they come out sequential instead, which is larger and therefore safe, but their
fields then do not alias the way the engine's do. Generate from a dump that carries `FieldOffsetAttribute` for
those, or lay them out by hand.

`sdk/Mystia.Extension.Sdk/Sdk/Sdk.props` and `Sdk.targets` ship inside the `Mystia.Extension.Sdk` package, so a mod is always built against the version the package was packed at. Keep the version in the nuspec, in this README and in the sample projects the same (currently 2.0.x).

Namespaces:

| Namespace | What you implement or call |
| --- | --- |
| `Mystia` | `[AutoWire]`, `IMod`, `IInitialization`, `ILog` |
| `Mystia.Scenes` | `SceneId`, `ISceneListener`, scene loops and their services |
| `Mystia.Listeners` | Day, prep, cook, work, and guest listeners, including `IGuestDirector` |
| `Mystia.Data` | `IDatabaseExtension` and `Catalogs` |

`IMod.Storage` reads and writes the mod own files: `TryOpenConfigRead`/`TryOpenConfigWrite` for the player editable config, and `TryOpenRead`/`TryOpenWrite`/`Exists`/`TryDelete` for the mod private cache. `IMod.Directory` is the mod folder.

Scene loops implement `Setup`, `Update`, and `Shutdown`. The services argument is valid only inside those calls. `SceneId.Night` is the work scene (`IWorkSceneGameLoop`).

See `samples/SampleMod.A`, `samples/SampleMod.B`, and `samples/SampleMod.Skip`.

## Players

Install the .NET 10 runtime. The launcher directory is a folder of your own; it contains:

- `Mystia.Syringe.exe`, the installer and launcher
- `Mystia.Proxy.dll`, the native proxy payload, installed into the game directory as `version.dll`
- `Mystia.Bootstrap.dll`
- `launcher.json`
- `mods/`, one folder per mod with that mod's dll and `mod.json`
- `host/`, a self-contained host: `Mystia.Modding.Host.dll`, `Mystia.Modding.Host.runtimeconfig.json`, the .NET runtime files (`hostfxr.dll`, `hostpolicy.dll`, `coreclr.dll`) and the host's dependencies, including the bridge, Il2CppInterop, and HarmonyX

`launcher.json`:

```json
{
  "gameExe": "C:\\Path\\To\\Touhou Mystia Izakaya.exe",
  "modsDirectory": "mods",
  "expectedGameAssemblySha256": "91CE5AE3DAD5DA07DFED63BAB4C9E454F67B6E50F9A6E8EC498EF9B0B806A789",
  "modOrder": ["sample.b", "sample.a"],
  "steamAppId": 1584090
}
```

- `gameExe` is required, and `GameAssembly.dll` has to sit beside it.
- `modsDirectory` is the mods folder, relative to the launcher directory (an absolute path is used as given).
- `expectedGameAssemblySha256` is the hash `GameAssembly.dll` must have before the launcher installs anything.
- `modOrder` is read by the host, not by the launcher: listed ids run in that sequence, and a loaded mod missing from the array runs afterwards, sorted by id.
- `steamAppId` is required. It is the app the launcher starts through Steam.

`--config <path>` reads a different `launcher.json`.

### Install and run

`Mystia.Syringe.exe`:

1. refuses to install when this machine pins `VERSION.dll` in `KnownDLLs` - the loader would take the system copy before the game directory and the proxy would never be called;
2. checks that `GameAssembly.dll` beside `gameExe` matches `expectedGameAssemblySha256`;
3. copies `Mystia.Proxy.dll` into the game directory as `version.dll` and writes `Mystia.Proxy.txt` beside it, naming the launcher directory;
4. starts the game through the Steam client with the activation argument: `steam.exe -applaunch <steamAppId> --enable-mystia-extension-framework` (the client command line; the `steam://run/<steamAppId>//<argument>` form works too but makes Steam ask the player to confirm the launch first).

The proxy stays installed for every launch of the game and stays inert unless that argument is there: a launch from the library carries no arguments, so it runs the shipped game with no pointer file read, no log written and no bootstrap loaded - `version.dll` only forwards, silently. Only a launch the launcher asked for (`--enable-mystia-extension-framework`) mounts mods. The same holds for the argument's spelling: `ProxyInstall.ActivationArgument` is the token the proxy matches, case-insensitively, as a whole argument.

Steam has to be the one that starts the game. The game's `SteamPlatform` constructor calls `SteamAPI_RestartAppIfNecessary`, so a process that is not a child of the Steam client is relaunched through `steam://run/<appid>` and exits - the injected copy never reaches the code that would mount mods. That is why the framework is a proxy inside the game process instead of an injector that starts the game itself, and why the game has to be installed through Steam and updated to the build named by `expectedGameAssemblySha256`: the installer refuses any other build, and the interop the host loads belongs to that one build.

`version.dll` is a drop-in replacement for the system `VERSION.dll`, which `UnityPlayer.dll` statically imports and which is not a KnownDLL. It forwards all 17 `VERSION.dll` exports to the real DLL under the system directory, then - only when the launch carries the activation argument - loads `Mystia.Bootstrap.dll` from the launcher directory `Mystia.Proxy.txt` names. The bootstrap hooks `il2cpp_init` and starts the host. The logs `proxy.log`, `bootstrap.log`, `host.log` and `stderr.log` are written into the launcher directory; until the proxy has read `Mystia.Proxy.txt` it logs to `%TEMP%\Mystia.Proxy.log`.

`--uninstall` removes `version.dll` from the game directory and `Mystia.Proxy.txt`, but only what it can identify as ours: a `version.dll` that is not byte identical to the payload, or a `Mystia.Proxy.txt` that names another launcher directory, is left in place and reported. It runs before the game hash check, so it still works after a game update. `--force` overwrites a `version.dll` that is not ours.

### Building the launcher

`dotnet build src/Mystia.Syringe` builds `Mystia.Syringe.exe`. Build the native payload from `native/Mystia.Proxy` and `native/Mystia.Bootstrap` with CMake and MSVC:

```text
cmake -S native/Mystia.Proxy -B build/proxy
cmake --build build/proxy
cmake -S native/Mystia.Bootstrap -B build/bootstrap
cmake --build build/bootstrap
```

The proxy build output is `Mystia.Proxy.dll` and the bootstrap's is `Mystia.Bootstrap.dll`; copy both next to the launcher. Never name the proxy payload `version.dll` inside the launcher directory: a process in that directory which imports `VERSION.dll` would hijack itself through its own payload, the same way the game is hijacked.

Publish the host into `host/` as a self-contained win-x64 application:

```text
dotnet publish src/Mystia.Modding.Host -c Release -r win-x64 --self-contained true -o <launcher>/host
```

The bootstrap loads `host\hostfxr.dll` and points hostfxr's `dotnet_root` at that same directory, so the runtime has to be inside `host/`. A framework dependent publish leaves it out and the host does not start.

## Pack the SDK

From the repository root:

```text
dotnet pack sdk/Mystia.Extension.Sdk/Mystia.Extension.Sdk.Pack.csproj -c Release
```

The package is written to `artifacts/nuget`.

## Third-party notices

Mystia Extension Framework is MIT, copyright 2026 Delsin-Yu. See `LICENSE`.

The bridge uses Il2CppInterop and HarmonyX, both MIT and copyright BepInEx contributors, and Iced, MIT and copyright 0xd4d. Those packages remain under their own licenses.

## Trademark

Touhou Mystia's Izakaya and related names belong to their owners. This repository is not affiliated with them.
