# Third-party software notices

This document records the third-party package metadata resolved for the
BeatInsight V1 Windows build. It is an audit snapshot, not legal advice and
not a substitute for reviewing the applicable licence texts before release.

## Audit scope

- Snapshot date: 2026-09-23.
- Project: `BeatInsight/BeatInsight.csproj`.
- Target: `net8.0-windows` with the restored `win-x64` runtime graph.
- Direct package references: `Microsoft.Data.Sqlite 10.0.9` and
  `ppy.osu.Game.Rulesets.Osu 2026.702.1`.
- Evidence: `BeatInsight/obj/project.assets.json`, matching local `.nupkg` and
  `.nuspec` files, package-local licence files, a clean win-x64 Release publish,
  binary version/configuration strings, and the exact upstream repository
  revisions identified below.
- The framework-neutral target resolves 231 package nodes. The `win-x64`
  target resolves 257 package nodes; 166 of those expose runtime, native or
  resource assets. The inventory below covers those 166 runtime-bearing
  package nodes.

The exact package files copied by a future publishing profile can differ from
the build graph. Re-run this audit after changing package versions, target
framework, runtime identifier, trimming, single-file settings, or publishing
profile.

## Dependency provenance follow-up

The exact `net8.0-windows/win-x64` graph was restored and a Release publish was
generated on 2026-09-23. The following conclusions are based on package-local
files or on the upstream repository and revision recorded by the package. A
repository licence is not described as package-local when the `.nupkg` omits it.

| Package | Upstream and copyright evidence | Verified licence and evidence |
| --- | --- | --- |
| `HidSharpCore 1.3.0` | `OpenTabletDriver/HIDSharpCore`, commit `bca4aee1c349985b0119eed24aba83e2dd63492d`; package copyright and assembly: `Copyright 2010-2019 James F. Bellinger` | Apache License 2.0. The `.nupkg` contains no licence or notice file; the exact upstream commit contains `LICENSE` and `NOTICE.txt`, and source headers also identify Apache-2.0. |
| `ppy.osuTK.NS20 1.0.211` | `ppy/osuTK` `netstandard` project URL; package authors `ppy Pty Ltd & The Open Toolkit Team`. The package does not pin a repository commit. Its archive timestamp is 2022-12-12 and the repository state immediately preceding publication is commit `af742f1afd01828efc7bc9fe77536b54aab8b419`. | The upstream `License.txt` is composite: MIT terms for the Open Toolkit (`Copyright (c) 2006-2014 Stefanos Apostolopoulos`), MIT terms for included Mono and Tao portions (`Copyright (c) 2004 Novell, Inc.` and `Copyright 2003-2007 Tao Framework Team`), and BSD 3-clause terms for OpenEXR-derived half conversion code (`Copyright (c) 2002 Industrial Light & Magic`). The `.nupkg` contains no licence file, so the complete upstream `License.txt`, rather than a one-word MIT label, is the evidence to retain. |
| `ppy.SDL2-CS 1.0.741-alpha` | `ppy/SDL2-CS`, commit `64b296a17ec93e60831745fd27a56eb3e0e40d79`; wrapper `Copyright (c) 2013-2024 Ethan Lee`; the bundled win-x64 native file identifies `Copyright (C) 2024 Sam Lantinga`. | zlib licence. The `.nupkg` contains no licence file. The pinned repository `LICENSE` supplies the zlib terms for SDL2#, and its `README` states that SDL2 and SDL2# are released under that licence. The package also contains SDL2 `2.31.0`; its published `SDL2.dll` is byte-identical to the package asset. |
| `ppy.Vk 1.0.26` | `mellinoe/vk`, commit `c3a41a7e14f3a33099c1fbae0bc6c433f37c100d`; package and assembly: `Copyright 2017 (c) Eric Mellino` | MIT for the code in the shipped library, from `LICENSE.md` at the exact repository commit. The `.nupkg` contains only the managed library and no licence or sample assets. The same upstream file separately attributes sample code to Sascha Willems; those samples are not in this package. |
| `SixLabors.ImageSharp 3.1.11` | `SixLabors/ImageSharp`, commit `f4a2684737732059876c13c481c856ed6b28e2c6`; package copyright `Copyright (c) Six Labors` | Apache License 2.0 applies under the package-local Six Labors Split License 1.0 criteria because BeatInsight is distributed under a Source Available licence. ImageSharp is also resolved as a transitive package dependency, which the same file lists as an independent Apache-2.0 qualification. This records only the qualification stated by that licence. |
| `FFmpeg.AutoGen 4.3.0.1` managed bindings | `Ruslan-B/FFmpeg.AutoGen`; package copyright `Copyright (c) Ruslan Balanukhin 2020` | GNU LGPL version 3. The historical `.nupkg` declares its own `LICENSE.txt` as the licence and includes the full LGPL-3.0 text. No conclusion is taken from the current upstream licence. |
| `OpenTabletDriver`, `OpenTabletDriver.Configurations`, `OpenTabletDriver.Native`, `OpenTabletDriver.Plugin` `0.6.7` | `OpenTabletDriver/OpenTabletDriver`, commit `736003ed72c8bbb28033b039d5a0bb76c344145c`; package authors `InfinityGhost,X9VoiD`; no separate copyright line is present in the package metadata | GNU LGPL `3.0-or-later`, stated by all four nuspecs and matched by `LICENSE` at the exact upstream commit. None of the four `.nupkg` files contains the licence text. |
| `TagLibSharp 2.3.0` | `mono/taglib-sharp`; package copyright `Copyright (c) 2006-2007 Brian Nickel; Copyright (c) 2009-2020 other contributors` | GNU LGPL `2.1-only`, stated by the nuspec and matched by `COPYING` at release tag `TaglibSharp-2.3.0.0` (`b5ae84f2e84087bf160bb0471420200dd2b5d809`). The `.nupkg` contains no licence file. |

### Native FFmpeg files in the win-x64 publish

`FFmpeg.AutoGen.dll` is managed binding code and does not supply the native
FFmpeg libraries. The four native files below come from
`ppy.osu.Framework.NativeLibs 2025.806.0-nativelibs`; SHA-256 comparison
confirms that the published files are byte-identical to its win-x64 assets.
No other FFmpeg native DLL is present in the publish output.

| Published file | FFmpeg product version | File version | Embedded licence report |
| --- | --- | --- | --- |
| `avcodec-58.dll` | `4.3.3` | `58.91.100` | `libavcodec license: LGPL version 2.1 or later` |
| `avformat-58.dll` | `4.3.3` | `58.45.100` | `libavformat license: LGPL version 2.1 or later` |
| `avutil-56.dll` | `4.3.3` | `56.51.100` | `libavutil license: LGPL version 2.1 or later` |
| `swscale-5.dll` | `4.3.3` | `5.7.100` | `libswscale license: LGPL version 2.1 or later` |

All four embed the same configure string. It includes `--enable-shared`,
`--disable-all`, the listed demuxers/parsers/decoders and Windows hardware
acceleration, and does not include `--enable-gpl`, `--enable-nonfree`, or any
external GPL library option. The native binary licence status can therefore be
established from the binaries as LGPL-2.1-or-later; it is not left ambiguous
between LGPL and GPL. The supplying `.nupkg` has MIT package metadata but no
FFmpeg licence, notice, source offer, or standalone build-configuration file,
so that MIT expression must not be applied to the contained FFmpeg binaries.

### Release materials

Verbatim third-party licence and notice files are shipped under
`THIRD-PARTY-LICENSES/`. Corresponding-source archives, exact revisions,
archive hashes, FFmpeg build configuration, and replacement/relinking
information are recorded in `THIRD-PARTY-SOURCE.md` and
`THIRD-PARTY-SOURCES/`. This notice does not duplicate the complete licence
texts supplied there.

## osu! ecosystem status

The primary osu! packages declare MIT in their local NuGet metadata:

- `ppy.osu.Game.Rulesets.Osu 2026.702.1`
- `ppy.osu.Game 2026.702.1`
- `ppy.osu.Framework 2026.629.0`
- `ppy.osu.Framework.NativeLibs 2025.806.0-nativelibs`

Other resolved osu!/ppy packages do not all use that same declaration:

- `ppy.osu.Game.Resources 2026.523.0` points to the
  [osu-resources licence](https://github.com/ppy/osu-resources/blob/master/LICENCE.md)
  and records copyright `(c) 2019 ppy Pty Ltd`.
- `ppy.managed-midi 1.10.2` points to its
  [upstream licence](https://github.com/atsushieno/managed-midi/LICENSE) and
  records `(C) 2010-2019, Atsushi Eno and contributors`.
- `ppy.ManagedBass`, `ppy.ManagedBass.Fx`, `ppy.ManagedBass.Mix`, and
  `ppy.ManagedBass.Wasapi` `2022.1216.0` contain the MIT licence and the notice
  `Copyright (c) 2016 Mathew Sachin`.
- The ppy Veldrid packages point to upstream licence files, listed in the
  inventory below.
- `HidSharpCore`, `ppy.osuTK.NS20`, `ppy.SDL2-CS`, and `ppy.Vk` omit licence
  files from their `.nupkg` archives. Their exact upstream evidence is recorded
  in the follow-up table above and must be retained with the release.

The osu! package family is therefore **not** covered by one blanket licence
statement. Each package's own metadata and upstream licence must be retained
and reviewed.

## Runtime-bearing package inventory

Package names and versions below come from the local `win-x64` NuGet graph.
Licence labels are copied from package metadata unless marked as a local file.

### MIT expression (58)

`AutoMapper 13.0.1`; `HtmlAgilityPack 1.12.4`; `Humanizer.Core 2.14.1`;
`JetBrains.Annotations 2025.2.4`; `MessagePack 3.1.7`;
`MessagePack.Annotations 3.1.7`; `Microsoft.AspNetCore.Connections.Abstractions 10.0.9`;
`Microsoft.AspNetCore.Http.Connections.Client 10.0.9`;
`Microsoft.AspNetCore.Http.Connections.Common 10.0.9`;
`Microsoft.AspNetCore.SignalR.Client 10.0.9`;
`Microsoft.AspNetCore.SignalR.Client.Core 10.0.9`;
`Microsoft.AspNetCore.SignalR.Common 10.0.9`;
`Microsoft.AspNetCore.SignalR.Protocols.Json 10.0.9`;
`Microsoft.AspNetCore.SignalR.Protocols.MessagePack 10.0.9`;
`Microsoft.AspNetCore.SignalR.Protocols.NewtonsoftJson 10.0.9`;
`Microsoft.Bcl.AsyncInterfaces 10.0.9`; `Microsoft.Bcl.TimeProvider 10.0.9`;
`Microsoft.Data.Sqlite 10.0.9`; `Microsoft.Data.Sqlite.Core 10.0.9`;
`Microsoft.Diagnostics.NETCore.Client 0.2.61701`;
`Microsoft.Diagnostics.Runtime 2.0.161401`;
`Microsoft.Extensions.Configuration.Abstractions 10.0.9`;
`Microsoft.Extensions.DependencyInjection 10.0.9`;
`Microsoft.Extensions.DependencyInjection.Abstractions 10.0.9`;
`Microsoft.Extensions.Features 10.0.9`; `Microsoft.Extensions.Logging 10.0.9`;
`Microsoft.Extensions.Logging.Abstractions 10.0.9`;
`Microsoft.Extensions.Options 10.0.9`; `Microsoft.Extensions.Primitives 10.0.9`;
`Microsoft.NET.StringTools 17.11.4`; `Microsoft.Toolkit.HighPerformance 7.1.2`;
`Newtonsoft.Json 13.0.4`; `NUnit 4.5.1`; `ppy.osu.Framework 2026.629.0`;
`ppy.osu.Framework.NativeLibs 2025.806.0-nativelibs`;
`ppy.osu.Game 2026.702.1`; `ppy.osu.Game.Rulesets.Osu 2026.702.1`;
`ppy.SDL3-CS 2026.629.0`; `Sentry 6.6.0`; `SharpCompress 0.49.1`;
`SharpGen.Runtime 2.0.0-beta.13`; `SharpGen.Runtime.COM 2.0.0-beta.13`;
`System.Collections.Immutable 1.7.1`; `System.ComponentModel.Annotations 5.0.0`;
`System.Diagnostics.DiagnosticSource 10.0.9`; `System.IO.Pipelines 10.0.9`;
`System.Net.ServerSentEvents 10.0.9`; `System.Numerics.Tensors 8.0.0`;
`System.Reflection.Metadata 1.8.1`; `System.Runtime.CompilerServices.Unsafe 6.0.0`;
`System.Text.Encodings.Web 10.0.9`; `System.Text.Json 10.0.9`;
`System.Threading.Channels 10.0.9`; `Vortice.D3DCompiler 2.4.2`;
`Vortice.Direct3D11 2.4.2`; `Vortice.DirectX 2.4.2`; `Vortice.DXGI 2.4.2`;
`Vortice.Mathematics 1.4.25`.

### MIT in a package-local licence file (5)

`ppy.ManagedBass 2022.1216.0`; `ppy.ManagedBass.Fx 2022.1216.0`;
`ppy.ManagedBass.Mix 2022.1216.0`; `ppy.ManagedBass.Wasapi 2022.1216.0`;
`SharpFNT 2.0.0`.

The ManagedBass packages retain `Copyright (c) 2016 Mathew Sachin`.
SharpFNT retains `Copyright 2018-2020 Aurora Berta-Oldham`.

### Apache-2.0 expression or URL (7)

`DiffPlex 1.9.0`; `Microsoft.Extensions.ObjectPool 5.0.11`; `Realm 20.1.0`;
`SQLitePCLRaw.config.e_sqlite3 3.0.3`; `SQLitePCLRaw.core 3.0.3`;
`SQLitePCLRaw.provider.e_sqlite3 3.0.3`; `Remotion.Linq 2.2.0`.

### Apache-2.0 in a package-local licence file (1)

`MongoDB.Bson 2.21.0`.

### LGPL (6 packages)

- `OpenTabletDriver 0.6.7`, `OpenTabletDriver.Configurations 0.6.7`,
  `OpenTabletDriver.Native 0.6.7`, `OpenTabletDriver.Plugin 0.6.7`:
  `LGPL-3.0-or-later`.
- `TagLibSharp 2.3.0`: `LGPL-2.1-only`.
- `FFmpeg.AutoGen 4.3.0.1`: LGPL-3.0 text in the package-local
  `LICENSE.txt`; package metadata records `Copyright © Ruslan Balanukhin 2020
  All rights reserved.`

The native FFmpeg libraries are not supplied by `FFmpeg.AutoGen`; see the
separate native-binary provenance section above.

### BSD (2 packages)

- `Markdig 0.23.0`: `BSD-2-Clause`.
- `StbiSharp 1.1.0`: BSD 3-Clause licence in package-local `LICENSE.txt`,
  retaining `Copyright (c) 2019, Thomas Müller`.

### Other package-local terms (2 packages)

- `SourceGear.sqlite3 3.50.4.5`: the package-local notice states
  `SQLite is Public Domain` and points to `https://sqlite.org/copyright.html`.
- `SixLabors.ImageSharp 3.1.11`: the package-local Six Labors Split License
  1.0 grants the Apache License 2.0 branch when the work is used in software
  under an Open Source or Source Available licence, and independently when it
  is a transitive package dependency. Both recorded facts apply here.
  Copyright `(c) Six Labors`.

### Licence supplied as an upstream URL (9 packages)

- `Microsoft.CSharp 4.5.0`, `System.Buffers 4.5.1`, `System.Memory 4.5.5`:
  [dotnet/corefx LICENSE.TXT](https://github.com/dotnet/corefx/blob/master/LICENSE.TXT).
- `ppy.Veldrid 4.9.69-ga405fe8484`,
  `ppy.Veldrid.MetalBindings 4.9.69-ga405fe8484`,
  `ppy.Veldrid.OpenGLBindings 4.9.69-ga405fe8484`:
  [ppy/Veldrid licence](https://raw.githubusercontent.com/ppy/veldrid/master/LICENSE).
- `ppy.Veldrid.SPIRV 1.0.15-g5911265ec3`:
  [veldrid-spirv licence](https://raw.githubusercontent.com/mellinoe/veldrid-spirv/master/LICENSE).
- `ppy.managed-midi 1.10.2`: upstream URL and notice described above.
- `ppy.osu.Game.Resources 2026.523.0`: upstream URL and notice described above.

### Legacy Microsoft licence URL (72 packages)

The following local package metadata points to
`http://go.microsoft.com/fwlink/?LinkId=329770`. The link is recorded exactly
as supplied by NuGet; this audit does not replace it with a guessed SPDX ID.

`Microsoft.NETCore.Platforms 1.1.0`; `Microsoft.NETCore.Targets 1.1.0`;
`runtime.any.System.Collections 4.3.0`; `runtime.any.System.Diagnostics.Tools 4.3.0`;
`runtime.any.System.Diagnostics.Tracing 4.3.0`; `runtime.any.System.Globalization 4.3.0`;
`runtime.any.System.Globalization.Calendars 4.3.0`; `runtime.any.System.IO 4.3.0`;
`runtime.any.System.Reflection 4.3.0`; `runtime.any.System.Reflection.Extensions 4.3.0`;
`runtime.any.System.Reflection.Primitives 4.3.0`;
`runtime.any.System.Resources.ResourceManager 4.3.0`; `runtime.any.System.Runtime 4.3.0`;
`runtime.any.System.Runtime.Handles 4.3.0`;
`runtime.any.System.Runtime.InteropServices 4.3.0`;
`runtime.any.System.Text.Encoding 4.3.0`; `runtime.any.System.Text.Encoding.Extensions 4.3.0`;
`runtime.any.System.Threading.Tasks 4.3.0`; `runtime.any.System.Threading.Timer 4.3.0`;
`runtime.native.System 4.3.0`; `runtime.native.System.IO.Compression 4.3.0`;
`runtime.native.System.Net.Http 4.3.0`; `runtime.native.System.Net.Security 4.3.0`;
`runtime.native.System.Security.Cryptography.Apple 4.3.0`;
`runtime.native.System.Security.Cryptography.OpenSsl 4.3.2`;
`runtime.win.Microsoft.Win32.Primitives 4.3.0`; `runtime.win.System.Console 4.3.0`;
`runtime.win.System.Diagnostics.Debug 4.3.0`; `runtime.win.System.IO.FileSystem 4.3.0`;
`runtime.win.System.Net.Primitives 4.3.0`; `runtime.win.System.Net.Sockets 4.3.0`;
`runtime.win.System.Runtime.Extensions 4.3.0`; `System.AppContext 4.3.0`;
`System.Collections.Concurrent 4.3.0`; `System.Dynamic.Runtime 4.3.0`;
`System.Globalization.Extensions 4.3.0`; `System.IO.Compression 4.3.0`;
`System.IO.Compression.ZipFile 4.3.0`; `System.IO.FileSystem.Primitives 4.3.0`;
`System.Linq 4.3.0`; `System.Linq.Expressions 4.3.0`; `System.Linq.Queryable 4.0.1`;
`System.Net.Http 4.3.0`; `System.Net.NameResolution 4.3.0`; `System.Net.Security 4.3.2`;
`System.Net.WebHeaderCollection 4.3.0`; `System.Net.WebSockets 4.3.0`;
`System.Net.WebSockets.Client 4.3.2`; `System.ObjectModel 4.3.0`;
`System.Reflection.Emit 4.3.0`; `System.Reflection.Emit.ILGeneration 4.3.0`;
`System.Reflection.Emit.Lightweight 4.3.0`; `System.Reflection.TypeExtensions 4.3.0`;
`System.Runtime.InteropServices.RuntimeInformation 4.3.0`; `System.Runtime.Numerics 4.3.0`;
`System.Security.Claims 4.3.0`; `System.Security.Cryptography.Algorithms 4.3.0`;
`System.Security.Cryptography.Cng 4.3.0`; `System.Security.Cryptography.Csp 4.3.0`;
`System.Security.Cryptography.Encoding 4.3.0`;
`System.Security.Cryptography.OpenSsl 4.3.0`;
`System.Security.Cryptography.Primitives 4.3.0`;
`System.Security.Cryptography.X509Certificates 4.3.0`; `System.Security.Principal 4.3.0`;
`System.Security.Principal.Windows 4.3.0`; `System.Text.RegularExpressions 4.3.0`;
`System.Threading 4.3.0`; `System.Threading.Overlapped 4.3.0`;
`System.Threading.Tasks.Extensions 4.3.0`; `System.Threading.ThreadPool 4.3.0`;
`System.Xml.ReaderWriter 4.3.0`; `System.Xml.XDocument 4.3.0`.

### Licence absent from package; verified from upstream (4 packages)

`HidSharpCore 1.3.0`; `ppy.osuTK.NS20 1.0.211`;
`ppy.SDL2-CS 1.0.741-alpha`; `ppy.Vk 1.0.26`.

The `.nupkg` archives have no licence field/file, but authoritative upstream
licence files have now been matched as described in the dependency-provenance
table. Because those texts are not embedded in the packages, copies still need
to be added to the release materials.

## Licence and notice files shipped with the release

- `HidSharpCore 1.3.0`: `THIRD-PARTY-LICENSES/HidSharpCore-1.3.0/LICENSE`
  and `NOTICE.txt` from commit
  `bca4aee1c349985b0119eed24aba83e2dd63492d`.
- `ppy.osuTK.NS20 1.0.211`:
  `THIRD-PARTY-LICENSES/ppy.osuTK.NS20-1.0.211/License.txt`, including its MIT
  and BSD third-party portions.
- `ppy.SDL2-CS 1.0.741-alpha`:
  `THIRD-PARTY-LICENSES/ppy.SDL2-CS-1.0.741-alpha/LICENSE` and
  `SDL2-LICENSE.txt` for the bundled native SDL2 library.
- `ppy.Vk 1.0.26`: `THIRD-PARTY-LICENSES/ppy.Vk-1.0.26/LICENSE.md` from commit
  `c3a41a7e14f3a33099c1fbae0bc6c433f37c100d`.
- `SixLabors.ImageSharp 3.1.11`:
  `THIRD-PARTY-LICENSES/SixLabors.ImageSharp-3.1.11/`, containing the
  package-local Split License, Apache-2.0 text, and copyright notice.
- `FFmpeg.AutoGen 4.3.0.1`:
  `THIRD-PARTY-LICENSES/FFmpeg.AutoGen-4.3.0.1/LICENSE.txt` and the GNU v3
  texts under `THIRD-PARTY-LICENSES/GNU/`.
- Native FFmpeg 4.3.3 DLLs: `THIRD-PARTY-LICENSES/FFmpeg-4.3.3/`, containing
  `COPYING.LGPLv2.1`, `CREDITS`, exact ppy build scripts, and the embedded
  configuration/provenance record.
- `OpenTabletDriver* 0.6.7`:
  `THIRD-PARTY-LICENSES/OpenTabletDriver-0.6.7/LICENSE` from commit
  `736003ed72c8bbb28033b039d5a0bb76c344145c` (GNU LGPL v3) and the GNU GPL v3
  text under `THIRD-PARTY-LICENSES/GNU/`.
- `TagLibSharp 2.3.0`: `THIRD-PARTY-LICENSES/TagLibSharp-2.3.0/COPYING` from tag
  `TaglibSharp-2.3.0.0` (GNU LGPL v2.1).

## Retention note

Where a package requires preservation of a licence, copyright notice, NOTICE
file, corresponding-source offer, relinking mechanism, or other material, the
release archive must contain that material in the form required by the
applicable licence. This inventory intentionally does not invent missing text
or declare legal compliance on the basis of NuGet metadata alone.
