# Asset notices

This document records the image assets currently present in the BeatInsight
repository and their known release status as of 2026-09-23. Repository history
shows when files were added, but does not by itself establish authorship,
ownership, permission, or a licence grant.

## Distributed application resources

The following files are compiled as WPF resources by
`BeatInsight/BeatInsight.csproj`.

| Asset | Dimensions | SHA-256 | Provenance / licence |
| --- | ---: | --- | --- |
| `Assets/BiaChan/BiaChanIdle.png` | 1122 x 1402 | `E2A6A0D1B2D449D10FF13070A7DA41D69E921BD71100B8EC62FEC9DCF25E65A1` | Provenance/licence à confirmer. |
| `Assets/BiaChan/BiaChanGoodrun.png` | 1122 x 1402 | `CEF70B2244FAFBA40991BE6DC863AAB2B24DCCF1EB33A93F5A60A1CBB696C2A6` | Provenance/licence à confirmer. |
| `Assets/BiaChan/BiaChanFC.png` | 1122 x 1402 | `70783BC006D9A840A7B9C453A0B307162A52D581B6E3983CDBF11ED322B6253D` | Provenance/licence à confirmer. |
| `Assets/BiaChan/BiaChanFailed.png` | 1122 x 1402 | `973ED0FA77B7EA5CD0110B228B3674131C1254CE39AAE31F6F48043AE4395A58` | Provenance/licence à confirmer. |
| `Assets/BiaChan/BiaChanError.png` | 1122 x 1402 | `4628F6CEE9837EBD62D805899D953CAD45A71C46A646286338E2EEB5320DCFBE` | Provenance/licence à confirmer. |
| `BeatInsight/Assets/Brand/BeatInsightLogo.png` | 1254 x 1254 | `06AFDC809AFC9A9AE1C35013D1C95222C23E54ACA6D45FAF107B41A57132DC66` | Provenance/licence à confirmer. |

The repository contains no accompanying author credit, source URL, licence,
commission agreement, assignment, or permission record for these images.
Their addition in a repository commit must not be presented as proof that the
committer created them or owns the necessary rights.

Before release, the owner must record for each asset: creator, source, date,
applicable licence or written permission, required attribution, permission for
modification, and permission for redistribution in application binaries.

## Duplicate brand source

`Assets/Brand/BeatInsightLogo.png` is byte-for-byte identical to the compiled
logo above (same dimensions, size and SHA-256). It is not referenced by the
current project file and appears to be a repository/source duplicate rather
than a second distributed resource. It was not deleted during this hardening
pass because duplication cleanup is separate from establishing rights.

## Other asset categories

No additional font, icon, background, SVG, GIF, JPEG, OTF, TTF, WOFF, or WOFF2
files were found under the repository's `Assets` directories in this audit.
This statement does not cover visual resources that may be embedded inside
third-party assemblies; those belong to the package audit in
`THIRD-PARTY-NOTICES.md`.
