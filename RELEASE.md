# BeatInsight public beta release notes

The public beta publish target is Windows x64, self-contained,
`net8.0-windows`, untrimmed, and not bundled as a single file. Use the checked-in
profile:

```powershell
dotnet publish BeatInsight/BeatInsight.csproj -c Release --no-restore -m:1 -p:PublishProfile=PublicBetaWinX64
```

## Symbol policy

Normal developer Debug and Release builds retain their standard symbols. The
public-beta publish profile sets `DebugSymbols=false` and `DebugType=None`, so
`BeatInsight.pdb` is excluded from the public distribution. Keep internal
symbols from a matching private build when diagnostics require them; do not add
the PDB to the public archive by default.

## Packaging gate

Before creating the public ZIP:

1. Confirm the publish folder contains `LICENSE.txt`, the notice files, and the
   `THIRD-PARTY-LICENSES` and `THIRD-PARTY-SOURCES` directories.
2. Confirm `BeatInsight.pdb` and source/configuration files containing local
   credentials are absent.
3. Run `BeatInsight.exe` on a clean Windows x64 machine without a separately
   installed .NET runtime, then exercise startup, Tosu connection, one local
   analysis, settings, and a normal shutdown.

The repository publish audit is not a substitute for that clean-machine smoke
test.
