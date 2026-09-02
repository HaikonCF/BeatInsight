using BeatInsight.Models;
using BeatInsight.Models.Persistence;
using BeatInsight.Parser;
using Microsoft.Data.Sqlite;
using System.IO;

namespace BeatInsight.Services.Persistence;

/// <summary>
/// Orchestre le cache d'analyses : lookup, validation de fraîcheur,
/// recalcul et persistance.
///
/// RÔLE
///
/// Ce service est le seul endroit où l'on décide qu'un enregistrement
/// est réutilisable. Le repository, lui, ne fait que lire et écrire
/// des lignes.
///
/// GameplayAnalyzer reste la source de vérité : en cas de miss, le
/// pipeline local V1 est exécuté sans aucune modification.
///
/// LE CACHE NE DOIT JAMAIS FAIRE ÉCHOUER BEATINSIGHT
///
/// Toute défaillance de persistance (base absente, illisible,
/// verrouillée, corrompue) dégrade vers le pipeline V1 et reste
/// invisible pour l'utilisateur.
///
/// En revanche, une erreur réelle d'analyse remonte normalement : une
/// beatmap illisible est un problème d'analyse, pas une panne de
/// cache, et la masquer serait trompeur.
/// </summary>
internal sealed class BeatmapAnalysisCacheService
{
    private readonly BeatmapAnalysisRepository repository;

    /// <summary>
    /// Le schéma n'est créé qu'une fois par instance.
    ///
    /// En cas d'échec, aucun état d'invalidité permanent n'est
    /// mémorisé : la cause peut être transitoire, et une nouvelle
    /// tentative au prochain appel ne coûte qu'une ouverture de
    /// connexion sur un chemin déjà en échec.
    /// </summary>
    private bool schemaReady;

    internal BeatmapAnalysisCacheService(
        BeatmapAnalysisRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);

        this.repository = repository;
    }


    // ============================================================
    // POINT D'ENTRÉE
    // ============================================================

    /// <summary>
    /// Retourne l'analyse d'une beatmap, depuis le cache si elle y
    /// est encore valide, sinon en exécutant le pipeline local puis
    /// en persistant le résultat.
    /// </summary>
    /// <param name="filePath">Chemin du fichier .osu.</param>
    /// <param name="beatmapId">
    /// Identifiant osu! lorsqu'il est connu. Absent de Beatmap, il
    /// provient de tosu et n'est stocké qu'à titre de clé secondaire :
    /// il ne participe pas à la validité en V2.1.
    /// </param>
    /// <returns>
    /// Une beatmap analysée, ou un snapshot de présentation restauré
    /// depuis le cache. Voir BeatmapAnalysisMapper pour la liste des
    /// membres absents d'un snapshot.
    /// </returns>
    internal Beatmap GetOrAnalyze(
        string filePath,
        int? beatmapId = null)
    {
        return GetOrAnalyzeDetailed(filePath, beatmapId).Beatmap;
    }

    /// <summary>
    /// Équivalent de <see cref="GetOrAnalyze"/>, mais indique en plus
    /// explicitement si le résultat provient du cache.
    ///
    /// Destiné aux appelants qui doivent distinguer hit et miss pour
    /// leurs propres besoins (par exemple des compteurs de
    /// progression), sans avoir à inférer cette information de l'état
    /// interne de la beatmap retournée.
    /// </summary>
    internal BeatmapAnalysisOutcome GetOrAnalyzeDetailed(
        string filePath,
        int? beatmapId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        FileInfo fileInfo = new(filePath);

        // Un fichier absent n'est pas un problème de cache : on laisse
        // le pipeline lever son erreur habituelle.
        if (fileInfo.Exists)
        {
            BeatmapAnalysisRecord? record = TryFind(filePath);

            if (record is not null
                && IsValid(record, filePath, fileInfo))
            {
                return new BeatmapAnalysisOutcome
                {
                    Beatmap = BeatmapAnalysisMapper.ToBeatmap(record),
                    WasCacheHit = true,
                };
            }
        }

        // ------------------------------------------------------------
        // Miss, stale, ligne illisible ou cache indisponible.
        //
        // Cet appel n'est volontairement pas protégé : une erreur
        // d'analyse doit remonter telle quelle.
        // ------------------------------------------------------------

        Beatmap beatmap = BeatmapParser.Load(filePath);

        if (fileInfo.Exists)
        {
            TryPersist(beatmap, filePath, fileInfo, beatmapId);
        }

        return new BeatmapAnalysisOutcome
        {
            Beatmap = beatmap,
            WasCacheHit = false,
        };
    }


    // ============================================================
    // VALIDITÉ
    // ============================================================

    /// <summary>
    /// Détermine si un enregistrement peut être réutilisé tel quel.
    ///
    /// Md5 et BeatmapId ne participent pas à la validité en V2.1 :
    /// le premier n'est pas alimenté, le second est absent pour une
    /// map locale et ne dit rien de la fraîcheur du fichier.
    /// </summary>
    private static bool IsValid(
        BeatmapAnalysisRecord record,
        string filePath,
        FileInfo fileInfo)
    {
        return string.Equals(
                   record.FilePath,
                   filePath,
                   StringComparison.OrdinalIgnoreCase)
               && record.FileSize == fileInfo.Length
               && record.FileLastWriteUtc
                      == fileInfo.LastWriteTimeUtc
               && record.AnalyzerVersion
                      == Analysis.AnalyzerVersion.Current
               && record.SchemaVersion
                      == PersistenceSchemaVersion.Current;
    }


    // ============================================================
    // ACCÈS TOLÉRANTS À LA PANNE
    // ============================================================

    /// <summary>
    /// Tente un lookup. Toute défaillance de stockage est traitée
    /// comme un miss.
    /// </summary>
    private BeatmapAnalysisRecord? TryFind(string filePath)
    {
        try
        {
            EnsureSchemaOnce();

            return repository.Find(filePath);
        }
        catch (SqliteException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Tente de persister l'analyse.
    ///
    /// Un échec d'écriture est silencieux par conception : l'analyse
    /// a déjà réussi et doit être rendue à l'utilisateur. La seule
    /// conséquence est que la map sera recalculée au prochain accès.
    /// </summary>
    private void TryPersist(
        Beatmap beatmap,
        string filePath,
        FileInfo fileInfo,
        int? beatmapId)
    {
        try
        {
            EnsureSchemaOnce();

            BeatmapAnalysisRecord record =
                BeatmapAnalysisMapper.ToRecord(
                    beatmap,
                    filePath,
                    fileInfo.Length,
                    fileInfo.LastWriteTimeUtc,
                    DateTime.UtcNow,
                    beatmapId);

            repository.Upsert(record);
        }
        catch (SqliteException)
        {
            // Cache non disponible en écriture : sans effet sur le
            // résultat retourné.
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void EnsureSchemaOnce()
    {
        if (schemaReady)
        {
            return;
        }

        repository.EnsureSchema();
        schemaReady = true;
    }
}
