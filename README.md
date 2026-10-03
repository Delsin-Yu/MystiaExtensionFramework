# Mystia Extension Framework

Unofficial mod middleware for Touhou Mystia's Izakaya. Launching the game directly still runs the Steam copy unchanged. Mods start only when the game is launched through Mystia.Syringe.

This project does not ship the game, its assemblies, or generated interop. Those stay on your machine.

Supported build: Unity 2021.3.28f1. `GameAssembly.dll` SHA256 `91CE5AE3DAD5DA07DFED63BAB4C9E454F67B6E50F9A6E8EC498EF9B0B806A789`. A different game build will not start.

## Mod authors

Install the .NET 10 SDK. Reference the package `Mystia.Extension.Sdk`.

```xml
<Project Sdk="Mystia.Extension.Sdk/2.0.0">
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

A mod is a class library. Declare one type per behavior, mark the interfaces with `[AutoWire]`, and add `mod.json` next to the assembly:

```json
{
  "id": "sample.a",
  "version": "1.0.0",
  "loadAfter": []
}
```

`loadAfter` is not an order. Players set the order in `launcher.json` with `modOrder`. Listed ids run in that sequence. A loaded mod missing from the array runs afterwards, sorted by id.

Namespaces:

| Namespace | What you implement or call |
| --- | --- |
| `Mystia` | `[AutoWire]`, `IModContext`, `IPostInitialize`, `ILog` |
| `Mystia.Scenes` | `SceneId`, `ISceneListener`, scene loops and their services |
| `Mystia.Listeners` | Day, prep, cook, work, and guest listeners, including `IGuestDirector` |
| `Mystia.Data` | `IDatabaseExtension` and `Catalogs` |

`IModContext.LoadSprite` loads a PNG from a path relative to the mod directory. `Catalogs.Core.FoodTags.Meat` and the other `Catalogs` members are the stock ids, with Chinese summaries on each constant.

Scene loops implement `Setup`, `Update`, and `Shutdown`. The services argument is valid only inside those calls. `SceneId.Night` is the work scene (`IWorkSceneGameLoop`).

See `samples/SampleMod.A`, `samples/SampleMod.B`, and `samples/SampleMod.Skip`.

## Players

Install the .NET 10 runtime. The launcher directory contains:

- `Mystia.Syringe.exe`
- `Mystia.Bootstrap.dll`
- `launcher.json`
- `mods/`, one folder per mod with that mod's dll and `mod.json`
- `host/Mystia.Modding.Host.dll`, `host/Mystia.Modding.Host.runtimeconfig.json`, and the host's dependencies, including the bridge, Il2CppInterop, and HarmonyX

`launcher.json`:

```json
{
  "gameExe": "C:\\Path\\To\\Touhou Mystia Izakaya.exe",
  "modsDirectory": "mods",
  "expectedGameAssemblySha256": "91CE5AE3DAD5DA07DFED63BAB4C9E454F67B6E50F9A6E8EC498EF9B0B806A789",
  "modOrder": ["sample.b", "sample.a"]
}
```

`gameExe` is required. If `GameAssembly.dll` beside that executable does not match `expectedGameAssemblySha256`, the game is not started. The injector does not write into the Steam directory.

Windows security software often blocks a program that starts another process suspended and loads a DLL into it. That is how the injector mounts mods.

Build the managed launcher from this repository with `dotnet build src/Mystia.Syringe`. Build `Mystia.Bootstrap.dll` from `native/Mystia.Bootstrap` with CMake and MSVC. Publish the host with `dotnet publish src/Mystia.Modding.Host -c Release -r win-x64` and copy the output into `host/`.

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
