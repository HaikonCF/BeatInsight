# BeatInsight

BeatInsight is a gameplay analysis tool for osu! beatmaps.

The goal is to analyze a beatmap and produce a readable gameplay profile describing
the main characteristics of the map.

## Features

### Gameplay detection

BeatInsight currently analyzes:

- Streams
- Jumps
- Bursts
- Tech
- Reading
- Speed
- Aim

Each category produces independent signals and scores.

### Gameplay Identity

BeatInsight attempts to classify the overall gameplay identity of a map.

Examples:

- Stream Speed
- Jump Aim
- Jump Reading
- Stream Speed Aim
- Jump / Stream Speed Aim
- Classic / Mixed

The classification is not intended to be perfect.

It is currently being calibrated using real beatmaps and player feedback.

### Confidence

Each classification includes a confidence value.

Example:

    IDENTITY = Jump / Stream Speed Aim
    CONFIDENCE = 87%

The confidence represents how strongly the current analysis supports the
classification.

### Traits

BeatInsight also exposes the characteristics detected in the map.

Example:

    TRAITS =
    - High Speed Pressure
    - High Aim Pressure
    - Jump Heavy
    - Stream Heavy
    - Reading Influence

These traits are intentionally visible to testers so that incorrect
classifications can be identified and reported.

## Current analysis

Example gameplay profile:

    PRIMARY TYPE = Stream

    SPEED = 75/100
    AIM = 53/100
    TECH = 16/100
    READ = 63/100

    IDENTITY = Stream Speed Aim
    CONFIDENCE = 62%

    TRAITS =
    - High Speed Pressure
    - High Reading Demand
    - Stream Heavy

## Why this project?

The goal of BeatInsight is not simply to give a single difficulty number.

It aims to describe **how a map plays**.

For example, two maps with similar difficulty can have very different
gameplay characteristics:

- one may focus on streams and speed,
- another on jumps and aim,
- another on reading and tech patterns.

BeatInsight attempts to make these differences explicit.

## Feedback

This project is currently in development.

If the classification of a map seems incorrect, please open an Issue and
include:

- Beatmap name
- Difficulty
- BeatInsight classification
- Confidence
- Traits
- What you believe the classification should be
- Optional explanation

Example:

    Beatmap: Ange du Blanc Pur
    BeatInsight: Stream Speed
    Confidence: 73%

    Expected:
    Stream Speed

    Notes:
    The map contains some bursts, but the majority of the gameplay is stream.

Feedback is especially useful for improving the classification system.

## Status

🚧 Early development / testing

The gameplay analysis system is actively being calibrated.