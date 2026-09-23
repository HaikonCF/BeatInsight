# BeatInsight

BeatInsight is a Windows gameplay-analysis tool for osu! beatmaps. It describes
patterns, skill pressure, sections, difficulty signals, and overall gameplay
characteristics rather than reducing a map to one number. Analysis runs locally
on the user's computer; BeatInsight does not upload beatmap files or diagnostic
logs.

> **Beta:** The current release is **2.0.0-beta.1**. Analysis and
> classification are still being calibrated, so results may change and user
> feedback is welcome.

## Requirements

- Windows x64.
- The self-contained public build; no separate .NET installation is required.
- [Tosu](https://github.com/tosuapp/tosu) for automatic detection of the map
  currently selected in osu!.

Tosu and osu! should be running before BeatInsight when current-map tracking is
needed. If Tosu is unavailable, BeatInsight remains open and reports that it is
waiting/disconnected, but it cannot automatically follow the current osu! map.
Local library data, cached results, settings, and explicit library actions remain
local; osu! API credentials are not required for local analysis.

Discord: https://discord.gg/DC3xZevRds

## Install and use

1. Download the Windows x64 release ZIP.
2. Extract the complete archive to a writable folder. Keep its native DLLs and
   legal directories beside `BeatInsight.exe`.
3. Start Tosu and osu!, then run `BeatInsight.exe`.
4. Select a beatmap in osu!. BeatInsight reads the local map through the Tosu
   integration and displays the completed analysis.

The public beta is not code-signed. Windows SmartScreen may show an
"unrecognized app" warning on first launch because the executable has no
reputation yet; that warning is not itself a malware finding.

## Optional osu! community features

Community Tags and Community Discovery use the osu! API. They are unavailable
when personal API credentials are absent, and BeatInsight does not send an OAuth
request in that state. Local beatmap analysis continues to work.

To enable these features:

1. Sign in to the osu! website and create an OAuth application in the account
   settings.
2. Use the Client Credentials flow; a callback URL is not needed by BeatInsight.
3. In BeatInsight settings, enter that application's Client ID and Client
   Secret, save them, and select **Test connection**.

The Client ID and protected secret record are stored at:

```text
%LOCALAPPDATA%\BeatInsight\osu-api-credentials.json
```

The Client Secret is protected with Windows DPAPI for the current Windows user.
DPAPI protects the value at rest; it is not protection against compromise of
that signed-in account. BeatInsight does not write the plaintext secret, OAuth
tokens, or Authorization headers to its logs.

For developer workflows only, environment variables are a fallback when no
credentials have been saved in the app:

```text
BEATINSIGHT_OSU_CLIENT_ID=<your client id>
BEATINSIGHT_OSU_CLIENT_SECRET=<your client secret>
```

Never commit, package, or share those values.

## Local data and diagnostics

BeatInsight stores runtime data per Windows user rather than beside the
executable:

- analysis database: `%LOCALAPPDATA%\BeatInsight\beatinsight.db`
- beatmap analysis cache: `%LOCALAPPDATA%\BeatInsight\Cache\Beatmaps\`
- settings and preferences: `%LOCALAPPDATA%\BeatInsight\`
- session and crash logs: `%LOCALAPPDATA%\BeatInsight\Logs\`

Cached analysis is reused when its content/version keys match; stale or missing
entries are recalculated locally. Clearing the cache causes recalculation and
does not remove the original `.osu` files.

Release logging is deliberately limited to lifecycle information, warnings,
errors, and important diagnostics. BeatInsight retains up to eight session logs
and caps each at 5 MiB. Detailed analyzer traces are disabled by default.

If BeatInsight crashes, include the newest `beatinsight-*.log`, the app version,
what you were doing, and reproducible steps in the issue report. Review logs
before sharing them; crash reports are local and are never uploaded
automatically.

## Known beta limitations

- Only Windows x64 is supported by this public build.
- Automatic current-map tracking depends on Tosu.
- Community Tags and Discovery require each user to provide their own osu! API
  application credentials.
- Analysis labels and calibration may evolve during the beta.
- The application is not currently code-signed.

## Licence and notices

BeatInsight is source-available under the custom BeatInsight Source License.
Non-commercial use and redistribution are permitted only under that licence's
conditions; commercial use requires prior written permission. This is not an
OSI open-source licence.

- [BeatInsight Source License](LICENSE.txt)
- [Third-party software notices](THIRD-PARTY-NOTICES.md)
- [Asset provenance and licence status](ASSET-NOTICES.md)
- [Release and PDB policy](RELEASE.md)

Presentation video: <https://youtu.be/6ewRDpj2UPs>
