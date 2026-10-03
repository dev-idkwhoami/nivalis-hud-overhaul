.DEFAULT_GOAL := help

GAME_PATH ?= $(NIVALIS_GAME_PATH)
CONFIGURATION ?= Release
DOTNET ?= $(if $(wildcard .tools/dotnet/dotnet),./.tools/dotnet/dotnet,dotnet)
PACKAGE_DIR ?= bin

PROJECT := src/HudOverhaul.csproj

export RELEASE_TAG GAME_PATH CONFIGURATION DOTNET PROJECT PACKAGE_DIR
export DOTNET_CLI_TELEMETRY_OPTOUT := 1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE := 1
export DOTNET_CLI_HOME := $(CURDIR)/.tools/cli-home
export NUGET_PACKAGES := $(CURDIR)/.tools/nuget

.PHONY: help check-sdk check-game build test test-quest-backoff test-quest-runtime test-vendor-search install package deploy clean check-release
help:
	@printf '%s\n' 'make deploy GAME_PATH="..."               Build, verify, and publish a tagged release'
	@printf '%s\n' 'make build GAME_PATH="../Nivalis Nights"  Build the DLL' 'make test                                Run HUD Overhaul checks' 'make install GAME_PATH="..."              Build and copy the DLL' 'make package GAME_PATH="..."              Build a release ZIP' 'make clean                               Remove generated build output' 'Optional: DOTNET=dotnet CONFIGURATION=Release'

check-game:
	@test -n "$$GAME_PATH" || { printf '%s\n' 'Set GAME_PATH to the game root (or export NIVALIS_GAME_PATH).'; exit 1; }
	@test -f "$$GAME_PATH/BepInEx/interop/Assembly-CSharp.dll" || { printf '%s\n' 'Game references missing. Launch the game with BepInEx IL2CPP once first.'; exit 1; }

build: check-sdk check-game
	@"$$DOTNET" build "$$PROJECT" --configuration "$$CONFIGURATION" "-p:GamePath=$$(cd "$$GAME_PATH" && pwd)" --nologo

# These checks use pure managed sources and do not need game assemblies.
test: check-sdk test-quest-runtime test-vendor-search
	@"$$DOTNET" run --project tests/SortingChecks.csproj --configuration "$$CONFIGURATION"

test-quest-backoff: check-sdk check-game
	@"$$DOTNET" run --project tools/QuestBackoffChecks/QuestBackoffChecks.csproj --configuration "$$CONFIGURATION" "-p:GamePath=$$(cd "$$GAME_PATH" && pwd)"

test-vendor-search: check-sdk
	@"$$DOTNET" run --project tools/VendorSearchChecks/VendorSearchChecks.csproj --configuration "$$CONFIGURATION"

test-quest-runtime: check-sdk
	@"$$DOTNET" run --project tools/QuestRuntimeChecks/QuestRuntimeChecks.csproj --configuration "$$CONFIGURATION"
	@"$$DOTNET" run --project tools/QuestRuntimeChecks/QuestRuntimeChecks.csproj --configuration "$$CONFIGURATION" --no-build -- --blocked-startup
	@"$$DOTNET" run --project tools/QuestRuntimeChecks/QuestRuntimeChecks.csproj --configuration "$$CONFIGURATION" --no-build -- --foreign-startup
	@"$$DOTNET" run --project tools/QuestRuntimeChecks/QuestRuntimeChecks.csproj --configuration "$$CONFIGURATION" --no-build -- --allowed-startup

install: build
	@bash tools/mod_artifacts.sh install

package: build
	@bash tools/mod_artifacts.sh package

deploy: check-sdk check-game
	@bash tools/deploy.sh

clean:
	@bash tools/mod_artifacts.sh clean

check-release: check-sdk
	@bash tools/check_release.sh

check-sdk:
	@command -v "$$DOTNET" >/dev/null 2>&1 || { printf '%s\n' '.NET SDK not found. Install SDK 8 on PATH, place it in .tools/dotnet/, or pass DOTNET="/path/to/dotnet".'; exit 1; }
