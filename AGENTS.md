# BeatInsight — Agent Guidelines

## Project

BeatInsight is a WPF .NET 8 application for osu! beatmap analysis.

Current major goal:
- finish the ML data pipeline
- build the first structural-classification baseline
- avoid adding new features before the first baseline model

Primary ML structural classes:
- Stream
- Jump
- Tech
- Classic/Mixed

Human labels and Community labels are distinct data sources.

---

## Critical semantic rules

### Gameplay Identity

Primary / Secondary structural identities are limited to:
- Stream
- Jump
- Tech
- Classic/Mixed

Never use these as Primary:
- Aim
- Speed
- Reading

Reason:
- Speed is transversal
- Aim and Reading are skill-pressure signals, not primary structural gameplay types

Do not change this unless explicitly requested.

### Taxonomy

Structure:
- Stream
- Jump
- Tech presence

Transversal:
- Burst

Skill Pressure:
- Aim
- Speed
- Reading
- Technical Pressure

Keep these concepts separate.

Do not conflate:
- Tech Presence
- Technical Pressure
- Tech Identity

---

## Analyzer safety

Do NOT modify GameplayAnalyzer formulas unless the user explicitly asks.

Do NOT silently recalibrate:
- Stream
- Jump
- Burst
- Tech
- Aim
- Speed
- Reading
- BeatInsight Rating
- Gameplay Identity
- confidence formulas

Current V1 formulas are considered frozen.

---

## ML philosophy

Human labels are ground truth annotations.

Community labels are a separate supervised source.

Target training composition:
- 50% Human
- 50% Community

Once a Community sample passes quality filters, it receives the same training weight as a Human sample.

Community confidence is used for:
- filtering
- auditing
- source comparison

It is NOT a lower training weight by default.

Never write Community labels into:
- PrimaryHumanLabel
- SecondaryHumanLabel
- HumanValidated

Never overwrite Human labels from Community data.

---

## Community Dataset

Community structural mapping:

Stream:
- stream-related
- deathstream-related
- stamina-related evidence

Jump:
- jump-related
- aim-related
- wide-aim-related evidence

Tech:
- tech-related
- aim-control
- technical-control evidence

Classic/Mixed:
- Clean or Simple community signal
- AND no strongly dominant Stream evidence
- AND no strongly dominant Jump evidence
- AND no strongly dominant Tech evidence

Do not force ambiguous maps into a class.

Prefer rejecting weak/ambiguous samples over lowering quality thresholds just to fill quotas.

---

## Community Seed architecture

Community seed selection is separate from local ML feature availability.

A remote Community seed may exist even when:
- the beatmap is not installed locally
- there is no MlDatasetSample yet

Conceptually:

CommunitySeedSample
!=
MlDatasetSample

Community seed metadata may include:
- BeatmapId
- BeatmapSetId
- Artist
- Title
- Difficulty
- Mapper
- StarRating
- CommunityLabel
- CommunityLabelConfidence
- CommunityLabelSource
- CommunityLabelEvidence
- CommunityTags
- IsOwned
- IsInMlDataset
- AcquisitionRequired

Never create fake MlDatasetSample rows without extracted features.

Reconciliation later is by BeatmapId.

---

## Community Discovery

Community / osu! user tags are sampling evidence only.

They must never automatically become Human labels.

Reuse existing:
- Community Discovery source
- CommunitySamplingTagCatalog
- OsuCommunityRequestPolicy
- OAuth token cache
- user-tag cache
- deterministic ranking

Do not create duplicate remote-request systems.

Avoid excessive request volume.

Prefer:
1. cheap candidate discovery
2. bounded enrichment of promising candidates
3. stop once quota is reached

Respect:
- 429
- Retry-After
- cancellation
- bounded retries

Never log:
- OAuth tokens
- Authorization headers
- cookies
- client secrets

---

## Download / Import semantics

Beatmap installation and BeatInsight indexing are separate concepts.

Installed locally:
- beatmap exists in osu! Songs

Owned / indexed:
- BeatInsight analysis repository contains the analyzed beatmap

Do not conflate:
IsInstalledLocally
with
IsOwned

Import confirmation must check the Songs folder, not BeatmapAnalysisRepository.

Do not manually modify osu!'s internal database.

Do not hardcode or request osu! session cookies.

---

## ML dataset safety

Do not:
- train models unless explicitly requested
- silently alter feature semantics
- bump FeatureSchemaVersion for annotation-only changes
- mix Human and Community labels into one persisted field
- introduce train/validation leakage

Deduplicate at least by:
- BeatmapId
- BeatmapSetId where appropriate

Different difficulties from the same beatmapset should not casually leak across train/validation splits.

---

## Current ML roadmap

Until first baseline:

1. Finish targeted indexing after import
2. Finish Community Dataset Generator
3. Generate Community dataset
4. Complete Human labels
5. Freeze dataset
6. Train first baseline
7. Evaluate confusion matrix / Macro F1 / per-class F1
8. Integrate model into BeatInsight

Do NOT add new gameplay features before the first baseline unless explicitly requested.

---

## UI / workspace rules

BeatInsight has:
- Analyzer workspace
- ML Lab workspace

ML-specific tools belong in ML Lab.

BeatInsight Gameplay Identity should not bias Human labeling workflows.

Fast Labeling / Calibration / Community Review must keep their queue semantics isolated.

Do not make Enter/Space in Community Review accidentally advance Fast Labeling or Calibration queues.

---

## Persistence

Primary persistent DB:
%LOCALAPPDATA%\BeatInsight\beatinsight.db

Annotation schema changes must not automatically imply ML feature schema changes.

Human labels:
- PrimaryHumanLabel
- SecondaryHumanLabel
- HumanValidated

Community labels:
must stay logically and persistently separate.

---

## Build / tests

Use:

dotnet build BeatInsight.slnx --configuration Debug --no-restore -m:1
dotnet test BeatInsight.slnx --configuration Debug --no-restore -m:1
git diff --check
git status --short

If BeatInsight.exe locks build output:
- identify the PID
- terminate it if authorized
- rerun build/tests

CRLF warnings from Git are acceptable if git diff --check reports no actual whitespace errors.

---

## Git rules

Do not commit unless explicitly asked.

Before commit:
- runtime behavior should be validated when relevant
- build must pass
- tests must pass
- git diff --check must pass

Never include local junk such as:
- release/
- scan-build/
- nul

Do not use git force.

Do not perform broad Replace All operations across the repository.

---

## Agent workflow

For non-trivial tickets:

1. inspect current working tree
2. read existing implementation
3. audit before changing architecture
4. make the smallest coherent change
5. preserve existing semantics
6. build
7. test
8. diff-check
9. report exactly what changed
10. no commit unless requested

Do not restart an existing partially completed implementation from scratch.

When resuming work:
- inspect current diff first
- identify what is already complete
- finish only what remains

---

## Current development principle

The project is now in baseline-delivery mode.

Priority:
correctness > feature count

Do not add speculative features before the first working ML baseline.