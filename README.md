# BeatInsight

BeatInsight is a gameplay analysis tool for osu! beatmaps.

The goal of BeatInsight is to describe the gameplay characteristics of a beatmap rather than simply giving it a difficulty rating.
je construis ça et j'ai besoin de joueurs pour me dire si mon algo raconte n'importe quoi Mdr

Lien de présentation de l'appli => https://youtu.be/6ewRDpj2UPs

Lien du Discord => https://discord.gg/DC3xZevRds

> ⚠️ BeatInsight is currently in an early public testing phase.
> The analysis and classification systems are still being calibrated.

---

## 🚀 Current Version

**v0.1.0 — Public Testing**

This release is intended for testing and feedback.

---

## 📦 Installation

1. Download the latest `.zip` from the Releases page.
2. Extract the archive somewhere on your computer.
3. Launch:

`BeatInsight.exe`

No installation is required.

### Requirements

- Windows x64
- Tosu is required for beatInsight
Here the link to how to download tosu => https://www.youtube.com/watch?v=KxqJkqlyym4

### Optional osu! community features

BeatInsight's local beatmap analysis does not require osu! API credentials.
Without credentials, community tags and Community Discovery are shown as
unavailable and no OAuth request is sent.

Each user can configure a personal osu! OAuth application from BeatInsight's
Settings panel. The Client ID is stored in the user settings file and the
Client Secret is protected with Windows DPAPI for the current Windows user at:

```text
%LOCALAPPDATA%\BeatInsight\osu-api-credentials.json
```

Create the application from the osu! account settings page, leave the callback
URL blank for the Client Credentials flow, then enter the Client ID and Client
Secret in BeatInsight. Save the credentials and use **Test connection**.

For developer workflows, environment variables remain a fallback when no
credentials have been saved in Settings:

```text
BEATINSIGHT_OSU_CLIENT_ID=<your client id>
BEATINSIGHT_OSU_CLIENT_SECRET=<your client secret>
```

Never commit, package, or log these values. DPAPI prevents plaintext storage;
it is local protection tied to the signed-in Windows account, not a guarantee
against compromise of that account.

---

# 🎮 Gameplay Analysis

BeatInsight currently analyses several gameplay characteristics.

### Pattern Detection

- Stream
- Jump
- Burst

### Gameplay Difficulty Signals

- Tech
- Read
- Speed
- Aim

The application combines these signals to determine the general gameplay profile of a beatmap.

---

⚠️ Windows SmartScreen

Lors du premier lancement, Windows peut afficher
"Windows a protégé votre ordinateur".

Cliquez sur "Informations complémentaires",
puis sur "Exécuter quand même".

Cette alerte est liée à la réputation de l'application,
pas à une détection de malware.

# 🧠 Gameplay Identity

BeatInsight also attempts to describe the overall identity of a map.

For example:

```text
IDENTITY = Jump / Stream Speed Aim
CONFIDENCE = 87%

TRAITS =
- High Speed Pressure
- High Aim Pressure
- Jump Heavy
- Stream Heavy
- Reading Influence
