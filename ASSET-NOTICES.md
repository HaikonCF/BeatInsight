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
| `Assets/BiaChan/BiaChanIdle.png` | 1360 x 1156 | `C04F80059B14FC4CAF79A8EE3FB9EC7EE25A96A884D45A2A7F21E55F9C56F1CA` | Generated with ChatGPT / OpenAI image generation at the BeatInsight owner's request as part of the Bia-chan mascot set. |
| `Assets/BiaChan/BiaChanGoodrun.png` | 1122 x 1402 | `CEF70B2244FAFBA40991BE6DC863AAB2B24DCCF1EB33A93F5A60A1CBB696C2A6` | Generated with ChatGPT / OpenAI image generation at the BeatInsight owner's request as part of the Bia-chan mascot set. |
| `Assets/BiaChan/BiaChanFC.png` | 1122 x 1402 | `70783BC006D9A840A7B9C453A0B307162A52D581B6E3983CDBF11ED322B6253D` | Generated with ChatGPT / OpenAI image generation at the BeatInsight owner's request as part of the Bia-chan mascot set. |
| `Assets/BiaChan/BiaChanFailed.png` | 1360 x 1156 | `13A0269905C5BA8ED0EDD5322FEE0ABB885A2E71B3E125D6E340A63CFA6AE96D` | Generated with ChatGPT / OpenAI image generation at the BeatInsight owner's request as part of the Bia-chan mascot set. |
| `Assets/BiaChan/BiaChanError.png` | 1122 x 1402 | `4628F6CEE9837EBD62D805899D953CAD45A71C46A646286338E2EEB5320DCFBE` | Generated with ChatGPT / OpenAI image generation at the BeatInsight owner's request as part of the Bia-chan mascot set. |
| `BeatInsight/Assets/Brand/BeatInsightLogo.png` | 1254 x 1254 | `06AFDC809AFC9A9AE1C35013D1C95222C23E54ACA6D45FAF107B41A57132DC66` | Generated with ChatGPT / OpenAI image generation at the BeatInsight owner's request. |

The Bia-chan images were directed/requested by the BeatInsight owner and
generated with ChatGPT / OpenAI image generation. They must not be described as
entirely hand-drawn artwork. This provenance statement does not assert that the
output is copyrightable in every jurisdiction, that rights are exclusive, or
that no third-party rights can apply.

The BeatInsight logo was also generated with ChatGPT / OpenAI image generation
at the owner's request. Neither the logo nor Bia-chan must be described as
entirely hand-drawn. Their presence in the repository must not be presented as
proof that copyright protection exists in every jurisdiction, that the rights
are exclusive, or that all possible third-party rights have been cleared.

The OpenAI Terms of Use for Europe reviewed for this notice (page updated
2026-01-16) state that, as between the user and OpenAI and to the extent
permitted by applicable law, the user owns the output and
OpenAI assigns any rights it has in that output. The same terms also state that
outputs may not be unique and that the user remains responsible for ensuring
that content does not violate applicable law or third-party rights. This notice
therefore records provenance and the OpenAI contractual position; it does not
guarantee copyrightability, exclusivity, uniqueness, or clearance from
third-party copyright, trademark, publicity, or other claims.

Before public release, retain any available generation records and perform a
reasonable visual and trademark-similarity review of the logo and mascot. See
the current OpenAI terms linked from `RELEASE.md`. Any newly identified
attribution or disclosure requirement must be added here before release.

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
