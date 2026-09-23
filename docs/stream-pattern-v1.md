# Stream Pattern Analysis V1

## Statut

Cette page fige la première baseline de `Stream Pattern Analysis`. Elle décrit des motifs observés dans les runs rapides d'une beatmap ; elle ne décide ni de l'Identity, ni de Primary/Secondary, ni de la présence Tech, ni du Rating, et elle ne dépend pas de Jump Pattern Analysis ou de l'UI.

Les neuf patterns V1 sont : Burst, Short Stream, Long Stream, Deathstream, Stamina Stream, Irregular Stream, Speed Stream, Flow Stream et Alternating Stream.

## Segmentation commune

Tous les patterns consomment les mêmes runs produits par `SegmentRapidCircleRuns` :

- cercles uniquement ;
- liens temporels de 30 à 130 ms, bornes incluses ;
- runs rapides maximaux ;
- aucun découpage artificiel en sous-runs.

Un slider ou un lien hors de l'intervalle ferme le run courant. La segmentation n'utilise aucune condition spatiale.

## Baseline de production

| Pattern | Qualification V1 | Métriques principales |
|---|---|---|
| Burst | 3–8 objets | longueur, intervalles, consistance temporelle, couverture |
| Short Stream | 9–16 objets | longueur, intervalles, consistance temporelle, couverture |
| Long Stream | 17–32 objets | longueur, intervalles, consistance temporelle, couverture |
| Deathstream | au moins 33 objets | longueur, intervalles, consistance temporelle, couverture |
| Stamina Stream | run d'au moins 9 objets ; au moins 2 runs ; au moins 24 objets rapides ; récupération au plus 1500 ms, borne incluse | nombre de runs, objets rapides, récupération, ratio actif, durée, consistance |
| Irregular Stream | au moins 6 objets ; CV au moins 0.10 ; au moins 2 régions d'intervalles atypiques distinctes ; déviation à la médiane d'au moins 12 ms | CV, deltas consécutifs, ratio de deltas significatifs, intervalles atypiques, événements distincts, score |
| Speed Stream | au moins 6 objets ; intervalle moyen au plus 90 ms | intervalle moyen, BPM Stream, intensité, consistance temporelle |
| Flow Stream | au moins 6 objets ; angles Flow 15°–110° inclus ; chaque lien au moins 18 px ; ratio de pivots Flow au moins 0.70 ; direction dominante au moins 0.75 | pivots, direction, angles, distances, `CurvatureConsistency`, score |
| Alternating Stream | au moins 6 objets ; angles valides 20°–160° inclus ; chaque lien au moins 20 px ; ratio de pivots valides au moins 0.70 ; ratio d'alternance au moins 0.75 | pivots valides, changements de signe, angles, distances, score |

`IrregularStreamPatternOptions.MinimumSignificantDeltaRatio` reste fixé à **0.30** pour la compatibilité et les diagnostics, mais ce ratio n'est pas un prédicat qualificatif V1. La qualification structurelle repose sur le CV et au moins deux régions atypiques distinctes. `MinimumDistinctIrregularEvents` vaut **2**.

`FlowStreamPatternSequence.CurvatureConsistency` reste une métrique descriptive. Elle ne participe pas à la qualification V1.

## Coexistence

Burst, Short Stream, Long Stream et Deathstream forment une partition exclusive par longueur d'un même run maximal. Les cinq autres patterns décrivent des dimensions orthogonales et peuvent coexister sur ce run :

- Stamina décrit la pression temporelle entre plusieurs runs ;
- Irregular décrit l'irrégularité temporelle interne ;
- Speed décrit la vitesse absolue soutenue ;
- Flow décrit une courbure spatiale cohérente ;
- Alternating décrit une alternance spatiale régulière des directions de virage.

Cette coexistence ne constitue pas une double classification de la dimension longueur.

## Compromis connus

### Irregular

- Aucun positif réel fort n'est considéré comme confirmé sur le corpus V1 actuel.
- Le spike isolé `80/80/80/125/80/80/80` peut satisfaire les seuils.
- Le ratio de deltas significatifs ne doit pas être abaissé à 0.20 en V1.
- La collecte de contrôles réels annotés reste nécessaire.

### Stamina

- La récupération maximale de 1500 ms est conservée provisoirement.
- La plage 1000–1250 ms n'est pas retenue faute de preuve suffisante.
- Road of Resistance reste un contrôle Stamina important.

### Flow

- `MinimumFlowPivotRatio = 0.70` est conservé.
- La variante `PivotRatio >= 0.65` avec un seuil qualificatif de `CurvatureConsistency` a été rejetée comme trop agressive : selon le seuil testé, elle supprimait 13 à 28 des 50 positifs de production du corpus.
- `CurvatureConsistency` reste descriptive uniquement.
- `MinimumFlowLinkDistance = 18 px` est confirmé en production. La baseline forensic antérieure à 20 px est historique et ne doit pas être réactivée.

### Alternating

- Le corpus ne contient qu'un contrôle positif réel fort.
- Les seuils sont conservés sans recalibration.
- Le rappel devra être réévalué lorsque davantage de contrôles zigzag annotés seront disponibles.

## Forensic reproductible et isolation

Les runners forensic sont exclusivement sous `BeatInsight.Tests/Analysis` et ne sont référencés par aucun projet de production :

- `StreamPatternGlobalForensicTests.cs` : photographie globale des neuf patterns sur le corpus réel de 18 cartes ;
- `StreamPatternTargetedSecondForensicTests.cs` : simulations ciblées Irregular, Stamina, Flow et Alternating, avec deltas exacts ; sa baseline Flow à 20 px est volontairement historique ;
- revue des candidats Flow : revue manuelle des neuf runs ajoutés par `PivotRatio = 0.65`, alimentée par la géométrie détaillée du runner ciblé ;
- mini-forensic Flow : comparaison en lecture seule de `0.65 + CurvatureConsistency` avec la production sur les 50 positifs Flow ; sa conclusion est l'abandon de cette combinaison ;
- `IrregularStreamPatternStructuralForensicTests.cs` : vérification structurelle des événements irréguliers et du spike isolé ;
- `StreamPatternV1ConsolidationForensicTests.cs` : consolidation finale du corpus et invariants transversaux V1.

Ces runners peuvent écrire des rapports dans leur répertoire de sortie de test. Ils n'installent aucune option simulée dans le runtime et ne sont appelés par aucun analyseur de production.

## Verrouillage et travaux reportés

`StreamPatternV1BaselineTests` verrouille les options et les bornes temporelles officielles sans dupliquer les tests comportementaux propres à chaque analyseur.

Sont reportés après V1 :

- toute recalibration des seuils ;
- une nouvelle métrique robuste de courbure Flow ;
- l'élargissement du corpus positif Alternating ;
- la confirmation de contrôles réels Irregular supplémentaires ;
- une éventuelle révision de la récupération Stamina.

Toute évolution future doit être forensic-first, testée sur des contrôles positifs et négatifs, et versionnée comme une évolution explicite de cette baseline.
