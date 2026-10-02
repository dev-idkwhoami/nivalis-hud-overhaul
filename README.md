# HUD Overhaul

A BepInEx IL2CPP mod for Nivalis Nights. This README covers building, testing,
installing development builds, and packaging releases.

## Requirements

- .NET SDK 8 (the mod targets .NET 6; the checks target .NET 8).
- GNU Make, Bash, and standard Unix utilities (including sed).
- The game with BepInEx 6 IL2CPP and generated interop assemblies.

Launch the game with BepInEx once before building. The build reads references
from `BepInEx/core/` and `BepInEx/interop/` in that installation. The first build
may need network access to restore .NET reference packages.

This repository contains only HUD Overhaul. Run all commands below from its
root, alongside the Makefile and `Directory.Build.props`.

## Build and test

```sh
make build GAME_PATH="../Nivalis Nights"
make test
```

Replace `../Nivalis Nights` with your game directory. Paths containing spaces
must be quoted. Relative paths resolve from the repository root. You can instead
export `NIVALIS_GAME_PATH` once and run `make build` without an argument.

The DLL is written to `bin/Nivalis.HudOverhaul.dll`.
Building does not install or package it. Tests cover managed logic and storage;
they do not require the game and do not validate Unity rendering or interactions.

The Makefile uses `dotnet` from PATH, or `.tools/dotnet/dotnet` if present.
Override with `DOTNET=...`. Builds default to `CONFIGURATION=Release`.
Local CLI state and NuGet packages stay under the ignored `.tools/` directory.
Install, package, clean, and release checks use Bash helpers. ZIP creation uses
the .NET SDK itself; Python and a separate ZIP utility are not required.

## Development installation

Close the game, then run:

```sh
make install GAME_PATH="../Nivalis Nights"
```

This builds and copies only the mod DLL into `BepInEx/plugins/`, replacing an
existing copy. It removes the known legacy per-mod DLL location and the old
standalone Condition Order DLL to prevent duplicate patches. It creates no ZIP
or backup and preserves existing mod settings and data. Restart the game to load
the new DLL.

For a manual installation test, use `make build` and copy the DLL yourself.

## Release package

```sh
make package GAME_PATH="../Nivalis Nights"
```

Packaging reads the assembly name and version from `src/HudOverhaul.csproj` and
creates `bin/Nivalis.HudOverhaul-<version>.zip`. Use `PACKAGE_DIR=...`
to choose another output directory. The archive contains exactly:

```text
BepInEx/plugins/Nivalis.HudOverhaul.dll
```

Users extract it into the game root after installing BepInEx. Packages contain
no game libraries, configuration, save data, history, or debugging symbols.
Packaging does not install or publish anything.

## Publish a release

Install GitHub CLI (`gh`) and authenticate with `gh auth login`. Set the same
version in `src/HudOverhaul.csproj` and `Plugin.Version`, commit your changes,
and create a tag on that commit:

```sh
git tag -a 1.0.0 -m "HUD Overhaul 1.0.0"
make deploy GAME_PATH="../Nivalis Nights"
```

Use the intended version instead of `1.0.0`; skip tag creation if it already
exists. Deploy defaults to the project version, or accepts `RELEASE_TAG=...`.
It requires a clean checkout matching the tag, runs tests, builds and packages
locally, and pushes the tag to `origin` if needed. It does not push branches.

The ZIP and a `.zip.sha256` checksum file are uploaded to a draft GitHub release.
Deploy downloads both assets and verifies their SHA-256 checksum before
publishing. Failed verification leaves the release in draft for investigation;
rerunning can replace draft assets. Published releases are never overwritten.
The checksum file is a separate release asset, not part of the ZIP. It verifies
file integrity, not a cryptographic signature or build attestation.

## Configuration and diagnostics

On first launch, the mod creates `BepInEx/config/HUDOverhaul/`, including
`HUDOverhaul.cfg`, editable labels, and persistent mod data. Existing legacy
root-level files migrate automatically. Do not ship your personal config folder.

Enable `[Logging] Verbose = true` in `HUDOverhaul.cfg` while the game is closed,
or use the in-game Features checkbox, for detailed diagnostics. Warnings and
errors remain enabled otherwise. Diagnostics use BepInEx's shared `LogOutput.log`;
`HUDOverhaul.history.log` is saved gameplay history, independent of verbosity.

## Source layout

```text
Makefile                       Build, test, install, package, deploy, clean
Directory.Build.props          C# build settings
LICENSE                        MIT license
tools/deploy.sh                Local release publishing and checksum verification
src/                           Mod source and project
tests/                         Managed regression checks
localization/labels.en.json     Default user-facing text
data/                          Bundled screen placement defaults
tools/                         Build and release helpers
bin/                           Generated DLL and optional ZIP
```

Run `make clean` to remove this mod's build outputs, including test outputs and
release ZIPs. It does not remove installed files. `.tools/`, `work/`, `bin/`, and
`obj/` are excluded from version control; local investigation material and game
binaries do not belong in the published source.

Licensed under the [MIT License](LICENSE).

> [!NOTE]
> This project was developed with AI assistance, including substantial AI-generated code and documentation. Contributions should be reviewed and tested; generated code may contain mistakes.
