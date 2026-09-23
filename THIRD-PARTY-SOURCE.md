# Third-party source and relinking information

This document records source provenance and practical replacement information
for LGPL components distributed with the BeatInsight win-x64 binary package.
It is a technical provenance record, not legal advice or a declaration of legal
compliance.

## Distribution mechanism used by this package

The release carries compressed source snapshots in `THIRD-PARTY-SOURCES/`,
verbatim licence texts in `THIRD-PARTY-LICENSES/`, and the dynamically linked
libraries as separate files. The source archives are identified below by exact
revision and SHA-256. They are included with the binary package rather than
relying only on mutable web links.

BeatInsight does not modify the listed third-party components. The assemblies
and native libraries are copied from their restored NuGet package assets.

## FFmpeg.AutoGen managed bindings

- Component/version: `FFmpeg.AutoGen 4.3.0.1`.
- Licence: GNU Lesser General Public License version 3, from the historical
  package-local `LICENSE.txt`.
- Upstream: `https://github.com/Ruslan-B/FFmpeg.AutoGen`.
- Source snapshot shipped:
  `THIRD-PARTY-SOURCES/FFmpeg.AutoGen-4.3.0.1-source-02622b8c.zip`.
- Snapshot revision: `02622b8c80e4c3beeae4cad38cae13c8aafe1c1c`.
- Snapshot SHA-256:
  `F488E1606F39022414ACFD5DF2603126666C04D3E039F642A46839781DB9DBB2`.
- Revision caveat: the nuspec does not contain a repository commit. This is the
  publication-time commit whose timestamp and 4.3 release update immediately
  precede the 4.3.0.1 package build; the package does not cryptographically
  assert the mapping.
- Modification status: not modified by BeatInsight.
- Replacement/relinking: `FFmpeg.AutoGen.dll` is distributed as a separate
  managed assembly, not merged into `BeatInsight.dll`. An interface-compatible
  rebuilt assembly can be placed under the same filename alongside BeatInsight.
- Licence material: `THIRD-PARTY-LICENSES/FFmpeg.AutoGen-4.3.0.1/LICENSE.txt`,
  `THIRD-PARTY-LICENSES/GNU/LGPL-3.0.txt`, and
  `THIRD-PARTY-LICENSES/GNU/GPL-3.0.txt`.

## FFmpeg native libraries

- Component/version: FFmpeg `4.3.3`.
- Licence classification: LGPL-2.1-or-later. Each distributed library reports
  that classification at runtime, and the embedded configure string contains
  neither `--enable-gpl` nor `--enable-nonfree`.
- Supplying package: `ppy.osu.Framework.NativeLibs
  2025.806.0-nativelibs`.
- Upstream source release: `https://ffmpeg.org/releases/ffmpeg-4.3.3.tar.gz`.
- Upstream tag/revision: `n4.3.3`, commit
  `a77521cd5d27e955b16e8097eecefc779ffdcb6d`.
- Source archive shipped: `THIRD-PARTY-SOURCES/ffmpeg-4.3.3.tar.gz`.
- Source archive SHA-256:
  `D721DB7E00955DF0DDE811B82BEE93F14568F1F8A22FFC41DAF4406CD7BB267B`.
- Historical ppy build revision:
  `ppy/osu-framework@02d0a060afa5fbae5cca987fde5cee2ad9f8771b`.
  That is the commit that introduced the exact currently distributed win-x64
  DLL blob and its scripts explicitly select FFmpeg 4.3.3.
- Build scripts shipped:
  `THIRD-PARTY-LICENSES/FFmpeg-4.3.3/ppy-build-common.sh` and
  `THIRD-PARTY-LICENSES/FFmpeg-4.3.3/ppy-build-win.sh`.
- Full embedded configuration and binary hashes:
  `THIRD-PARTY-LICENSES/FFmpeg-4.3.3/CONFIGURATION.txt`.
- Distributed shared libraries: `avcodec-58.dll`, `avformat-58.dll`,
  `avutil-56.dll`, and `swscale-5.dll`. No other FFmpeg native DLL is present.
- Modification status: not modified by BeatInsight.
- Replacement/relinking: the four DLLs are separate shared libraries. They are
  not statically embedded into BeatInsight and may be replaced under the same
  filenames by ABI-compatible builds.
- Licence material: `THIRD-PARTY-LICENSES/FFmpeg-4.3.3/COPYING.LGPLv2.1`,
  `THIRD-PARTY-LICENSES/FFmpeg-4.3.3/CREDITS`, and
  `THIRD-PARTY-LICENSES/GNU/LGPL-2.1.txt`.

## OpenTabletDriver managed components

- Components/version: `OpenTabletDriver`,
  `OpenTabletDriver.Configurations`, `OpenTabletDriver.Native`, and
  `OpenTabletDriver.Plugin`, all version `0.6.7`.
- Licence: LGPL-3.0-or-later.
- Upstream: `https://github.com/OpenTabletDriver/OpenTabletDriver`.
- Exact revision recorded by every package:
  `736003ed72c8bbb28033b039d5a0bb76c344145c`.
- Source archive shipped:
  `THIRD-PARTY-SOURCES/OpenTabletDriver-0.6.7-source-736003ed.zip`.
- Source archive SHA-256:
  `A02D289717453537E27713D080D9F0A8E9800148B64DD42A72D42D55C2918F99`.
- Modification status: not modified by BeatInsight.
- Replacement/relinking: each OpenTabletDriver component is distributed as a
  separate managed assembly and is not merged into `BeatInsight.dll`.
  Interface-compatible rebuilt assemblies can be placed under the same
  filenames alongside BeatInsight.
- Licence material: `THIRD-PARTY-LICENSES/OpenTabletDriver-0.6.7/LICENSE`,
  `THIRD-PARTY-LICENSES/GNU/LGPL-3.0.txt`, and
  `THIRD-PARTY-LICENSES/GNU/GPL-3.0.txt`.

## TagLibSharp

- Component/version: `TagLibSharp 2.3.0`.
- Licence: LGPL-2.1-only.
- Upstream: `https://github.com/mono/taglib-sharp`.
- Release tag/revision: `TaglibSharp-2.3.0.0`, commit
  `b5ae84f2e84087bf160bb0471420200dd2b5d809`.
- Source archive shipped:
  `THIRD-PARTY-SOURCES/TagLibSharp-2.3.0-corresponding-source-b5ae84f2.zip`.
- Source archive SHA-256:
  `84A8E985C6CB09D9E98795D07FE4FB4966B32999910652D52B1903F7D159BC6D`.
- Archive scope: 466 upstream entries are retained, including production and
  test source, project/build files, documentation, and `COPYING`. Exactly 207
  large binary test-media entries under `src/TaglibSharp.Tests/samples/` and
  `src/TaglibSharp.Tests/raw-samples/` are excluded; no production or build
  source file is excluded.
- Modification status: not modified by BeatInsight.
- Replacement/relinking: `TagLibSharp.dll` is distributed as a separate
  managed assembly and is not merged into `BeatInsight.dll`. An
  interface-compatible rebuilt assembly can be placed under the same filename.
- Licence material: `THIRD-PARTY-LICENSES/TagLibSharp-2.3.0/COPYING` and
  `THIRD-PARTY-LICENSES/GNU/LGPL-2.1.txt`.

## Scope and remaining review

The archives, exact revisions, build scripts, licence texts, and replaceable
library layout provide an auditable technical source/relinking package. A
release owner should still confirm that this chosen delivery mechanism and the
final distribution channel satisfy the applicable licence conditions. This
document does not make that legal determination.
