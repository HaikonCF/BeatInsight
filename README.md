# BeatInsight

BeatInsight is a Windows gameplay-analysis tool for osu! beatmaps. It describes
patterns, skill pressure, sections, difficulty signals, and overall gameplay
characteristics rather than reducing a map to one number. Analysis runs locally
on the user's computer; BeatInsight does not upload beatmap files or diagnostic
logs.

> **Current version:** the current source version is **3.0.1**. Analysis and
> classification are still being calibrated, so results may change and user
> feedback is welcome.

## Requirements

- Windows x64.
- The self-contained public build; no separate .NET installation is required.
- Microsoft Edge WebView2 Runtime for Community Overlays.
- osu!stable or osu!lazer for supported live game-state features.

osu! should be running before BeatInsight when current-map tracking is needed.
If osu! is unavailable, BeatInsight remains open and reports that it is waiting,
without retaining stale map state. Native helpers are packaged with BeatInsight;
they are not separate downloads.
Local library data, cached results, settings, and explicit library actions remain
local; osu! API credentials are not required for local analysis.

Discord: https://discord.gg/DC3xZevRds

## Install and use

1. Download and run the Windows x64 BeatInsight installer.
2. Keep the installed `NativeLazer`, `LazerKeys`, and `CommunityOverlays`
   directories beside the main application files.
3. Start osu!stable or osu!lazer, then run BeatInsight.
4. Select a beatmap in osu!. BeatInsight reads the local map through the native
   game-state integration and displays the completed analysis.

The per-user installer, automatic updater, and application data use separate
locations. Updating or uninstalling the application must preserve the user data
under `%LOCALAPPDATA%\BeatInsight`.

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

## 🤖 AI in BeatInsight — Development & Transparency

BeatInsight is an independent project developed with the assistance of AI tools.

AI plays a role in both the project's development process and its long-term technical direction. I believe it's important to be transparent about how these tools are used and what remains under human responsibility.

### 💜 How BeatInsight is developed

BeatInsight is not a project where AI is simply asked to generate an entire application without supervision.

Development follows a collaborative, human-directed workflow:

1. **Vision & Design** — I define the project's goals, features, requirements, priorities and expected behavior.
2. **Technical Research** — AI assists with code analysis, architecture reviews, debugging and exploring possible implementations.
3. **Implementation** — AI coding assistants help write, modify and refactor code within defined constraints.
4. **Testing & Diagnostics** — Changes are checked through automated tests, builds, logs and targeted diagnostics.
5. **Real-world Validation** — I personally test features in osu!stable and osu!lazer, verify results and report unexpected behavior.
6. **Iteration** — When something doesn't work as expected, we investigate the root cause, correct the implementation and validate it again.

AI-generated code is not automatically considered correct. Successful compilation is not enough: important features must also work in real gameplay conditions.

### 🛠️ Tools & Responsibilities

AI assistants, including **ChatGPT, OpenAI Codex and Claude**, have been used throughout development for technical discussions, implementation assistance, debugging and code reviews.

Their contributions vary depending on the task. Some features require only small changes, while others involve multiple iterations of investigation, implementation and validation.

**My responsibilities include:**
- Defining the project's direction and requirements.
- Making architectural and product decisions.
- Evaluating suggested solutions.
- Testing BeatInsight during actual osu! gameplay.
- Reporting bugs and validating corrections.
- Deciding when a feature is ready for release.

AI assists with development, but **the project's direction, acceptance criteria and release decisions remain human-controlled**.

### 🧠 AI Features & Future Direction

BeatInsight currently relies heavily on deterministic analysis of osu! beatmaps and gameplay-related data.

The long-term direction includes exploring machine learning for more advanced pattern recognition and difficulty analysis.

Any AI-powered functionality should be distinguished from deterministic analysis and documented according to what it actually does.

**Using AI to develop BeatInsight does not mean that every analysis result is generated by AI.**

### 🔍 Transparency & Quality

The development process prioritizes:

- Reproducible bugs and evidence-based diagnostics.
- Minimal, targeted fixes instead of speculative changes.
- Automated regression testing.
- Validation against real osu!stable and osu!lazer behavior.
- Preserving data integrity and avoiding fabricated gameplay statistics.
- Clear acknowledgment of known limitations.

AI is a development tool, not a substitute for testing, engineering judgment or accountability.

---

*BeatInsight is a human-directed project built with AI assistance, extensive iteration and real-world testing.*

*Made with curiosity, determination, a lot of debugging, and a little help from AI. 💜*

## Known beta limitations

- Only Windows x64 is supported by this public build.
- Live support differs between osu!stable and osu!lazer when the game does not
  expose an equivalent trustworthy native value; unavailable values remain
  fail-closed rather than being invented.
- Community Overlays require the Microsoft Edge WebView2 Runtime.
- Community Tags and Discovery require each user to provide their own osu! API
  application credentials.
- Analysis labels and calibration may evolve during the beta.
- The application is not currently code-signed.
- Bia-chan and the BeatInsight logo were generated with ChatGPT / OpenAI image
  generation at the project owner's request. Their provenance and the remaining
  non-exclusivity and third-party-rights caveats are documented in
  `ASSET-NOTICES.md`.

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
