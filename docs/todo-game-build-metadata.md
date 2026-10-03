# TODO: emit a version binding from the game build

Status: not started. Nothing in this repository consumes this yet.

## Why

`Mystia.InteropGen` takes the compile time surface - which types and members exist, and under which names -
from a managed backup directory produced by a local Unity player build (`<project>/Build/**/Managed`).
That directory is a build product of one machine and is not versioned, so the generated interop is not
reproducible from the versioned inputs.

Observed on two machines with the same game commit, the same `ProjectSettings` (`stripEngineCode: 1`,
`managedStrippingLevel: 1` everywhere, same `Assets/Plugins/link.xml`):

- One backup kept `ResourceProviderBase.Release`; the other lost it to UnityLinker. The generated interop
  followed the backup, and the machine whose backup lost it could not compile the bridge at all
  (`CS0115 ... Release ... found no suitable method to override`).
- The backup folder is byte identical to the linker's own output
  (`Library/Bee/artifacts/**/ManagedStripped`), i.e. it is the post link build product, not a source artifact.

`artifacts/` is ignored in this repository, so the interop cannot travel through git either. A machine whose
backup lacks a member has to be handed the whole `artifacts/interop` directory from another machine by hand.
That stopgap is what this note is meant to remove.

Two further problems have the same root:

- **The compile time surface is not the runtime surface.** The runtime truth is the shipped, pinned
  `GameAssembly.dll` and `global-metadata.dat`. The compile surface comes from a local rebuild that is *not*
  the shipped build (its `GameAssembly.dll` hash differs). The framework can therefore compile against
  members the shipped player does not have, and only discover it when the member is called in game.
- **Compiler generated identities carry the build.** `<>c__DisplayClass16_6`, `<MainChallengeLoop>g__Timing|2>d`,
  `_isRetake_5__2` are numbered by the compiler of one build. `ChallengeTargets`, `NamedSeams` and
  `AssetBuilderTargets` verify them at startup, but such a check can only say "this is not the build I know".
  It cannot name the seam in a build independent way, so every binding has to be re-derived by hand after a
  game side refactor.

## Proposal

The game build emits a payload beside the build output whenever a player is built:

```text
<project>/Build/<game>_BackUpThisFolder_ButDontShipItWithYourGame/MEFXMetadata_DontShip/
```

The `_DontShip` suffix follows the existing backup folder's convention: the directory must never reach the
shipped build, the Steam depot or an installer. MEFX is the only consumer.

### Contents

**1. `build.json` - identity and build options.** Whatever decides the linker's result, recorded so the
payload can be pinned by hash:

```json
{
  "schemaVersion": 1,
  "gameAssemblyVersion": "...",
  "unityVersion": "2021.3.28f1",
  "platform": "StandaloneWindows64",
  "developmentBuild": false,
  "copySymbols": false,
  "managedStrippingLevel": 1,
  "stripEngineCode": true,
  "linkXmlSha256": "...",
  "scriptingDefineSymbols": ["..."],
  "gameAssemblySha256": "...",
  "globalMetadataSha256": "..."
}
```

**2. `seams.json` - the version binding. This is the part that cannot be derived anywhere else.**

A map from stable, semantic names to the build specific identity that currently implements them. The names
are the contract; the identities are what changes per build.

```json
{
  "schemaVersion": 1,
  "seams": {
    "challenge.yuyuko.storyClosure":     { "type": "GameData.Profile.YuyukoBossData+<>c__DisplayClass16_0", "role": "closure" },
    "challenge.yuyuko.retakeClosure":    { "type": "GameData.Profile.YuyukoBossData+<>c__DisplayClass16_6", "role": "closure" },
    "challenge.yuyuko.standClosure":     { "type": "GameData.Profile.YuyukoBossData+<>c__DisplayClass16_9", "role": "closure" },
    "challenge.yuyuko.mainLoop":         { "type": "GameData.Profile.YuyukoBossData+<MainChallengeLoop>d__16", "role": "stateMachine" },
    "challenge.yuyuko.phaseClock":       { "type": "...+<>c__DisplayClass16_0+<<MainChallengeLoop>g__Timing|2>d", "role": "stateMachine" },
    "challenge.yuyuko.phase1SpawnLoop":  { "type": "...+<>c__DisplayClass16_0+<<MainChallengeLoop>g__Phase1GuestSpawnLoop|7>d", "role": "stateMachine" },
    "challenge.yuyuko.evaluationCallback": { "type": "GameData.Profile.YuyukoBossData+<>c__DisplayClass16_0", "member": "<the evaluation local function>", "signature": "Void(EvaluationResult, GuestGroupController, Boolean, ref String, ref Boolean)" },
    "challenge.yuyuko.retakeFlag":       { "type": "...+<MainChallengeLoop>d__16", "member": "_isRetake_5__2", "role": "field" },
    "challenge.yuyuko.bossLife":         { "type": "...+<>c__DisplayClass16_0", "member": "yuyukoTotalLife", "role": "field" }
  }
}
```

This is the list `ChallengeTargets` holds today, moved to the side that can actually keep it up to date.
The names come from game side knowledge (which closure holds `yuyukoTotalLife`, which state machine is the
phase clock); the identities are read out of the build. A refactor that moves one of them then changes this
file instead of silently renumbering what the bridge hooks.

**3. Stripped member report.** Which members the linker removed from the assemblies it fed to IL2CPP.
This turns today's hand written list of "capabilities this build does not have" into data: a member that is
absent here must never be called, and MEFX can reject a bridge that references it.

**4. Shipped member surface (decision, see below).** Either

- the post link managed assemblies themselves (exact, but tens of MB and not diffable), or
- a `surface.json` listing every type (with its original mangled name), its members, signatures and tokens, or
- nothing, if MEFX is moved to derive its input from the pinned install instead (see Alternatives).

## How to emit

An editor only build callback in the game project (`IPostprocessBuildWithReport`), which runs when both the
linker output and the final `GameAssembly.dll` exist. It must:

- write into the backup folder's `MEFXMetadata_DontShip/`, never into the shipped data folder;
- be editor tooling only - no runtime change, nothing added to the player;
- fail the build (or at least report loudly) when a named seam in `seams.json` cannot be resolved in this
  build. A broken seam contract should break the build that broke it, not every mod at runtime;
- be cheap: it runs once per player build.

Whether the seam resolution is maintained by hand in the game repository, or derived structurally at build
time (find the closure under `YuyukoBossData` that holds a field named `yuyukoTotalLife`), is an open
decision. Hand maintained is unambiguous but rots silently; derived fails loudly but is more code. A first
cut can derive and write out, and let the file be reviewed like any other generated artifact.

## Consumer work in this repository

- `Mystia.InteropGen` gains `--payload <dir>`: reads `build.json`, checks `gameAssemblySha256` and
  `globalMetadataSha256` against the install directory it was pointed at, and refuses to generate on a
  mismatch. `seams.json` is copied into the manifest as the binding of record.
- The generated manifest records the payload hash, so a user can tell which game build the interop belongs
  to without a rebuild.
- A small table of known payload hashes can be committed here (the payload is small once it is a descriptor),
  which is what makes "the SDK knows game version X" a versioned statement instead of a per machine accident.
- The startup validators (`ChallengeTargets`, `NamedSeams`, `AssetBuilderTargets`) read the seam names from
  the binding rather than carrying a second, hand written copy of the mangled names.

## Acceptance

- Two machines, same game source and same build options, produce the same payload content (the identity
  fields may differ in nothing but ordering).
- MEFX generates interop from `(payload, install directory)` alone, with no reference to `Library/`,
  `Build/Symbols` or any other developer local state.
- MEFX refuses to generate on a `GameAssembly.dll` or `global-metadata.dat` that does not match the payload.
- Deleting `MEFXMetadata_DontShip/` from a build output has no effect on the game.
- Moving one of the seams on the game side changes `seams.json`, and MEFX reports a named failure for it -
  never a silently mis-applied hook.

## Decisions

1. Descriptor (`surface.json`) or the post link assemblies? A descriptor keeps the payload small enough to
   version here; the assemblies keep the current generator input unchanged.
2. Is the seam map hand maintained in the game repository, derived at build time, or both?
3. Does an unresolvable seam fail the game build, or only write a report?
4. Is the payload always emitted, or behind a build option / a script define?

## Alternatives considered

**Derive everything from the pinned install (no game side change).** `GameAssembly.dll` plus
`global-metadata.dat` already contain the shipped type and member surface; a dumper's dummy assemblies are
the standard input for this kind of generator. This removes the machine dependent input *and* the
compile/runtime split in one move, and needs no cooperation from the game repository. It does not provide
the seam map, which is the one thing only game side knowledge can supply - so the two approaches are
complementary rather than exclusive, and the seam map (2) is the reason to still want this payload.

**Commit a canonical post link backup here and pin its hash.** Keeps the current pipeline and makes it
reproducible, but it is a snapshot of one build: it rots as the game moves and it does not fix the
compile/runtime split.

**Keep copying `artifacts/interop` between machines.** The current stopgap. Works, but it is a manual,
unverifiable step; the failure mode observed was a stale interop that nobody noticed until a compile failed.

## Risks

- A game side build step that fails builds can be disruptive to the game's own release process; keep the
  failure behind a decision (see Decisions 3).
- A hand maintained seam map rots unless something forces it to be reviewed. Prefer deriving it, or fail
  the build when a name no longer resolves.
- The payload must never reach players: the `_DontShip` name is a convention, not enforcement. The depot and
  installer configuration has to exclude it explicitly, and the acceptance test above exists to keep that
  honest.
