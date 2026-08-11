# BeatInsight

BeatInsight is a gameplay analysis tool for osu! beatmaps.

The goal of BeatInsight is to describe the gameplay characteristics of a beatmap rather than simply giving it a difficulty rating.

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
