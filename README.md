# HUD Overhaul

A BepInEx IL2CPP mod for Nivalis Nights. This README covers building, testing,
installing development builds, and packaging releases.

## Requirements

- .NET SDK 8 (the mod targets .NET 6; the checks target .NET 8).
- GNU Make, Bash, and standard Unix utilities (including sed).
- The game with BepInEx 6 IL2CPP and generated interop assemblies.
- Mod Companion 1.0.2, included in the archive.

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

Mod Companion owns settings in `BepInEx/config/HUDOverhaul/HUDOverhaul.cfg`.
New keys use their defaults when absent. There is no migration of legacy setting
names. Labels, history and other gameplay data stay in
`BepInEx/config/HUDOverhaul/`; their existing data migration remains intact.
Do not ship your personal config folder.

Enable `[Developer] Verbose = true` in the companion-managed HUD config while the
game is closed, or use HUD Overhaul's Developer category in Mod Companion, for detailed diagnostics. Warnings and
errors remain enabled otherwise. Diagnostics use BepInEx's shared `LogOutput.log`;
`HUDOverhaul.history.log` is saved gameplay history, independent of verbosity.

## Farming screen placement appearance

Attached farming screens participate in the module's native material override
when it is moved, and return to their normal appearance when the override ends.
The screen's latest produce image is kept in the native restoration cache, so
automatic label updates cannot replace the placement appearance mid-move.
This uses the existing Farming Screens feature and adds no polling or colliders.

## Known Vendors search

With expanded search enabled, the Known Vendors search matches vendor names or
the names of items they sell, including temporarily sold-out items. Matching is
case-insensitive and accepts part of a name. Location and vendor-type filters
still apply; undiscovered vendors are not added. Stock is read only when filtering,
with no background scan or persistent inventory cache.

`make test-vendor-search` runs the search callbacks against managed fixtures,
including combined filters, disabled search, sold-out items and failure cleanup.
The game UI and IL2CPP hooks still need an in-game check.

## Quest mod compatibility

The **Show quest HUD** toggle in Mod Companion's Features category and the
native hold-J action control the same setting: `[Features] QuestHudVisible` in
`BepInEx/config/HUDOverhaul/HUDOverhaul.cfg`. Changes apply immediately and persist
across restarts and saves. Temporary hiding by other UI does not change it.
The earlier JSON preference is no longer used. This setting is independent of pinned-quest
filtering, but is disabled when the quest module yields to a conflicting mod.

Combined Ingredients closes with Escape/controller Back or its close button.
The native Pause action (including O) no longer closes this window.

Quest hooks are deferred until BepInEx reports that all plugins have loaded,
and registered on the next Unity update, after startup event handlers return.
Compatibility is checked once at that point. Tracked Quests HUD's optional
load-order dependency is retained, and its presence disables the entire quest
module. Another Harmony owner on `NavigationUI.LateUpdate` or
`ActiveJournalEntriesUi.Refresh` also prevents registration. No quest lifecycle,
HUD, compass, or marker-registry hooks are installed when blocked, and the module
never subscribes to quest events. Other HUD features install normally during Load.

Your saved quest-filter setting is preserved, but cannot enable a skipped module
during that session. The menu shows the option disabled with an explanation.
Restart after changing installed quest mods. There is no runtime compatibility
polling or support for changing patch ownership after this startup decision.
Harmony identifies hooks, not what those hooks do; direct native modifications
outside Harmony are not detected. A failed partial installation rolls back only
our dedicated quest-module patches.

Run `make test-quest-backoff GAME_PATH="../Nivalis Nights"` for ownership and
provider-metadata checks against the installed Harmony types. Run
`make test-quest-runtime` (also included in `make test`) for the production quest
selection/cache callbacks against managed stand-ins for native game objects,
and the startup installer against a recording patcher.
These cover startup gating, notifications, unchanged frames, marker replacement,
registrations during drawing, exception cleanup, and save/manager replacement.
They do not replace an in-game IL2CPP detour or coexistence test.

### Background work

Quest selection is invalidated by native pin, progress, quest-start, and
quest-completion notifications. HUD refreshes also request current state. Save
clears and manager destruction detach listeners and discard cached state. There
is no quest-state polling timer. Unchanged pin selections keep the same revision,
so ordinary progress notifications do not rebuild the compass view unnecessarily.

While the feature is enabled and an active quest is pinned, the compass uses a
cached secondary marker list. It rebuilds only when the pin selection, manager,
source list, or native list version changes. The game's quest-assignment setter
unregisters/re-registers a marker, which changes that version. Direct third-party
writes to marker fields without those native callbacks are not tracked. Moving
targets remain live because the view contains the original marker objects.

Native `NavigationUI.LateUpdate` reads the manager's list field directly, with no
list parameter or filter callback. We therefore select the cached list only for
that draw and restore the original registry in a finalizer. No list is rebuilt
on unchanged frames. Marker registration/removal during a draw is routed to the
original registry so those changes survive restoration. With the feature off or
no active pins, the compass uses its original list without substitution.

Only staff payments are recorded in the custom history log. The unused sale,
purchase, price/wage-change, venue-baseline and inventory collectors have been
removed, along with the recurring reconciliation scan and frame-spreading code.
Payroll capture, save branches, load restoration and the one-time staff receipt
import remain intact. Buffered payroll writes still run on the existing worker;
the lightweight pump flushes pending records every two seconds or at 256 records,
and on save/quit. It does not scan menus, staff rosters or inventories.
Existing log files remain readable and are not rewritten to remove older events.

The bundled Mod Companion adds its settings entry when the native settings panel
opens, without repeatedly searching loaded objects during gameplay.

To support another known quest mod, edit `src/QuestModCompatibility.cs`: add its
plugin GUID and display name to `Providers`, and add a matching soft-dependency
attribute at the top of that same file. Optional Harmony owner IDs can be listed
on the provider when they differ from its plugin GUID. No changes to patch
registration or menu code are required. The compatibility checks verify every
catalog entry has its optional load-order declaration. Unlisted owners are still
detected through Harmony; matching plugin metadata supplies their display name.

Overlapping Combined Ingredients rows were reproduced by disabling the copied
list layout. The window now restores its copied layout components so it works
even when the native Statistics window's layout was disabled when copied. This
is separate from the compass conflict; a direct connection to Tracked Quests HUD
was not established. Main-menu Controls rows likewise restore their layout and
register with the native canvas manager.

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

## Mod Companion settings

Mod Companion 1.0.2 is required and included. F5 and the native settings entry open
its shared menu; HUD Overhaul no longer provides a standalone settings window.
Choose HUD Overhaul's icon in the top row. Features, Shopping and Farm are custom
tabs; Controls, Developer and Info use Companion's dedicated section APIs. Controls
contains Quick Actions and Farm Editor, Developer supplies the standard verbose
toggle, and Info uses headings and paragraphs for help. Standard/Estimated shopping mode is also exposed and
stays synchronized with the shopping window's tab selection.

Quick Actions and Farm Editor are configured in the companion's Controls tab.
Their action registration and binding storage still use the game's input system;
Farm Editor now has a gamepad binding slot as well. The retired HUD-menu action
identity is inert, retained only to prevent unknown-action errors when the game
loads its own existing input save packet; HUD config values are not imported.

Builds reference the bundled DLL in `dependencies/`. Packaging and deployment include
all files from that folder under `BepInEx/plugins/`; no separate Companion download is needed. BepInEx will refuse to load
HUD Overhaul if the dependency is missing. In-game controller/rebinding checks
remain necessary with both rebuilt DLLs.

The gold HUD logo is embedded in the DLL as `HudOverhaul.Icon.png` and passed to Companion as image bytes. No separate icon file is installed. The source image is in `assets/hud-overhaul-icon.png`.
