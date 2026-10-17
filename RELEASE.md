# BeatInsight V3 release and packaging guide

BeatInsight V3 targets Windows x64. The public package is produced by Velopack
as a per-user installer and contains these product components:

- `BeatInsight.exe` (`net8.0-windows`, self-contained);
- `NativeLazer/NativeLazerFastRefreshRunner.exe` (`net10.0-windows`, self-contained);
- `LazerKeys/LazerKeyOverlayRunner.exe` (`net10.0-windows`, self-contained);
- `CommunityOverlays/BeatInsight.CommunityOverlays.Poc.exe`
  (`net8.0-windows10.0.17763.0`, self-contained).

Despite its historical `Poc` name, the CommunityOverlays executable is a V3
product companion. A missing helper or companion is a packaging failure, not an
optional degraded release.

## Reproducible build inputs

The following source projects must exist in a clean clone before building:

```text
BeatInsight/BeatInsight.csproj
BeatInsight/Diagnostics/NativeLazer/NativeLazerFastRefreshRunner.csproj
tools/LazerKeyOverlayRunner/LazerKeyOverlayRunner.csproj
tools/LazerKeyOverlayReader/LazerKeyOverlayReader.csproj
BeatInsight.CommunityOverlays.Poc/BeatInsight.CommunityOverlays.Poc.csproj
```

The release script restores every product project for `win-x64`; it must not
reuse untracked binaries from a developer machine. Build the installer from the
repository root with:

```powershell
.\build-release.ps1
```

`-SkipRestore` is allowed only after the same projects and runtime identifiers
have already been restored. Release tooling is pinned by
`.config/dotnet-tools.json`.

## Package boundaries

The public package includes the desktop application, NativeLazer helper,
LazerKeys reader/runner, CommunityOverlays companion, required runtime
dependencies, legal notices, and source-compliance material.

The public package excludes PDB/source files, CommunityOverlays `SelfTest` and
`TestAssets`, NativeLazer research runners, test projects, logs, reports, corpus
data, and all developer-only build artifacts.

The CommunityOverlays companion uses the Microsoft Edge WebView2 Runtime. The
SDK assemblies and x64 `WebView2Loader.dll` are packaged, but the Evergreen
WebView2 Runtime is a Windows prerequisite and must be verified on the target
machine. The application must fail clearly if that runtime is unavailable.

## Automated release gates

Before publishing:

1. Build Debug and Release without restoring implicitly.
2. Run NativeLazer, LazerKeys, CommunityOverlays, overlay, PP, lifecycle, theme,
   packaging, `BeatInsight.Tests`, and `BeatInsight.ML.Tests` suites.
3. Report corpus tests requiring `D:\osu!\Songs` separately when unavailable.
4. Run the self-contained `win-x64` publish and `build-release.ps1` gates.
5. Run `git diff --check` and inspect the final publish manifest.

The release script rejects missing product executables, missing WebView2 x64
loader/assemblies, PDB/source/log files, CommunityOverlays test content, and
known diagnostic NativeLazer runners.

## Runtime validation gates

Test the installed package—not a developer `bin` directory—on Windows x64:

- startup, tray, shutdown, startup-with-Windows and update;
- osu!stable and osu!lazer SongSelect, Gameplay, Pause, Results, Retry and map change;
- live stats and PP, including Results latching and stale prevention;
- CommunityOverlays lifecycle and precise v1/v2 arbitration;
- native K1/K2 behavior and expected unavailable values;
- themes, Edit Mode, layout persistence and restart;
- WebView2 installed behavior and its clear unavailable-runtime failure path.

## Symbols, signing and user data

Developer builds retain symbols. Public publishing sets `DebugSymbols=false`
and `DebugType=None`; PDBs remain private. Current release artifacts are not
code-signed, so Windows SmartScreen may warn users.

Install, update and uninstall validation must preserve
`%LOCALAPPDATA%\BeatInsight`. Packaging must never remove osu! folders or user
beatmaps.

## Asset provenance gate

Bia-chan and the BeatInsight logo were generated with ChatGPT / OpenAI image
generation at the project owner's request. Neither must be described as
entirely hand-drawn or as carrying guaranteed exclusive rights.

The [OpenAI Terms of Use for Europe](https://openai.com/fr-FR/policies/eu-terms-of-use/)
reviewed for this release state that, as between the user and OpenAI and to the
extent permitted by applicable law, the user owns the output and OpenAI assigns
any rights it has in that output. They also state that outputs may not be unique
and make the user responsible for applicable law and third-party rights.
Accordingly, the previously unknown logo-generator provenance is resolved, but
the terms do not guarantee copyrightability, exclusivity, uniqueness, or
third-party clearance. Retain available generation records and complete a
reasonable visual and trademark-similarity review before publication. See
`ASSET-NOTICES.md`.

## Current release status

The repository is not release-ready until every required V3 source exists in a
clean clone, the full package passes its gates, the remaining asset review is
accepted, and the final installed live scenarios are demonstrated.
