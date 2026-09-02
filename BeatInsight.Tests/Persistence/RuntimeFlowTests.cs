using BeatInsight;
using BeatInsight.Models;
using BeatInsight.Services;
using BeatInsight.Services.Persistence;
using Microsoft.Data.Sqlite;
using System.IO;

namespace BeatInsight.Tests.Persistence;

/// <summary>
/// Reproduit la séquence exacte du flux runtime de MainWindow après
/// branchement du cache :
///
///   visite map 1  -> analyse fraîche + persistance
///   visite map 2  -> analyse fraîche + persistance
///   retour map 1  -> hit, snapshot restauré
///
/// Ces tests n'instancient pas la fenêtre WPF et ne dépendent pas de
/// tosu : ils exercent le même service, avec les mêmes appels, dans le
/// même ordre. La vérification de l'interface réelle reste une étape
/// manuelle distincte.
///
/// Aucun appel réseau : les tags communautaires sont fabriqués
/// localement pour vérifier que Community Evidence se recalcule bien
/// à partir d'un snapshot restauré.
/// </summary>
public sealed class RuntimeFlowTests : IDisposable
{
    private const string MapOne = "Tower Of Heaven [Extra].osu";
    private const string MapTwo = "Ashioto Tarte Tatin [Koori's Insane].osu";

    private readonly string directory;
    private readonly string mapOnePath;
    private readonly string mapTwoPath;
    private readonly BeatmapAnalysisCacheService service;

    public RuntimeFlowTests()
    {
        directory = Path.Combine(
            Path.GetTempPath(),
            "beatinsight-flow-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        mapOnePath = CopyFixture(MapOne);
        mapTwoPath = CopyFixture(MapTwo);

        service = new BeatmapAnalysisCacheService(
            new BeatmapAnalysisRepository(
                Path.Combine(directory, "flow.db")));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    private string CopyFixture(string fixtureName)
    {
        string source = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Maps",
            fixtureName);

        Assert.True(
            File.Exists(source),
            $"Fixture introuvable : {source}");

        string destination = Path.Combine(directory, fixtureName);
        File.Copy(source, destination);

        return destination;
    }


    // ============================================================
    // SÉQUENCE COMPLÈTE
    // ============================================================

    [Fact]
    public void VisitOne_VisitTwo_ReturnToOne_ProducesHitOnFirstMap()
    {
        // Visite 1 : analyse fraîche
        Beatmap firstVisit =
            service.GetOrAnalyze(mapOnePath, beatmapId: 111);

        Assert.NotEmpty(firstVisit.HitObjects);

        // Autre map : analyse fraîche également
        Beatmap otherMap =
            service.GetOrAnalyze(mapTwoPath, beatmapId: 222);

        Assert.NotEmpty(otherMap.HitObjects);

        // Retour sur la map 1 : hit, snapshot restauré
        Beatmap returnVisit =
            service.GetOrAnalyze(mapOnePath, beatmapId: 111);

        Assert.Empty(returnVisit.HitObjects);

        // Les deux maps restent distinctes en cache.
        Assert.NotEqual(firstVisit.Title + firstVisit.Version,
                        otherMap.Title + otherMap.Version);

        AssertPresentationIdentical(firstVisit, returnVisit);
    }

    [Fact]
    public void ReturnVisit_OnSecondMap_AlsoHits()
    {
        Beatmap firstVisit = service.GetOrAnalyze(mapTwoPath, 222);
        service.GetOrAnalyze(mapOnePath, 111);
        Beatmap returnVisit = service.GetOrAnalyze(mapTwoPath, 222);

        Assert.NotEmpty(firstVisit.HitObjects);
        Assert.Empty(returnVisit.HitObjects);

        AssertPresentationIdentical(firstVisit, returnVisit);
    }


    // ============================================================
    // CHAMPS AFFICHÉS
    // ============================================================

    /// <summary>
    /// Vérifie tout ce que l'interface et les rapports consomment.
    /// </summary>
    private static void AssertPresentationIdentical(
        Beatmap fresh,
        Beatmap restored)
    {
        // Metadata
        Assert.Equal(fresh.Title, restored.Title);
        Assert.Equal(fresh.Artist, restored.Artist);
        Assert.Equal(fresh.Creator, restored.Creator);
        Assert.Equal(fresh.Version, restored.Version);
        Assert.Equal(fresh.LengthDisplay, restored.LengthDisplay);
        Assert.Equal(fresh.BPM, restored.BPM);
        Assert.Equal(fresh.MaxCombo, restored.MaxCombo);
        Assert.Equal(fresh.AR, restored.AR);
        Assert.Equal(fresh.OD, restored.OD);
        Assert.Equal(fresh.CS, restored.CS);
        Assert.Equal(fresh.HP, restored.HP);
        Assert.Equal(fresh.CircleCount, restored.CircleCount);
        Assert.Equal(fresh.SliderCount, restored.SliderCount);
        Assert.Equal(fresh.SpinnerCount, restored.SpinnerCount);

        // osu! SR et BeatInsight Rating
        Assert.Equal(fresh.OsuStarRating, restored.OsuStarRating);
        Assert.True(restored.OsuStarRating > 0.0);
        Assert.Equal(
            fresh.BeatInsightRating,
            restored.BeatInsightRating);

        GameplayProfile before = fresh.GameplayProfile;
        GameplayProfile after = restored.GameplayProfile;

        // Structure ratios
        Assert.Equal(before.StreamRatio, after.StreamRatio);
        Assert.Equal(before.JumpRatio, after.JumpRatio);
        Assert.Equal(before.BurstRatio, after.BurstRatio);

        // Aim / Speed / Reading
        Assert.Equal(before.AimScore, after.AimScore);
        Assert.Equal(before.SpeedScore, after.SpeedScore);
        Assert.Equal(before.ReadScore, after.ReadScore);

        // Tech : couverture structurelle et pression finale, distinctes
        Assert.Equal(before.TechPresence, after.TechPresence);
        Assert.Equal(before.TechScore, after.TechScore);

        // Identity
        Assert.Equal(
            before.Identity.Primary,
            after.Identity.Primary);
        Assert.Equal(
            before.Identity.Secondary,
            after.Identity.Secondary);
        Assert.Equal(
            before.Identity.Pattern,
            after.Identity.Pattern);
        Assert.Equal(
            before.Identity.FullName,
            after.Identity.FullName);

        // Confidence
        Assert.Equal(
            before.Identity.Confidence,
            after.Identity.Confidence);

        // Traits
        Assert.Equal(before.Identity.Traits, after.Identity.Traits);
        Assert.Equal(
            before.Identity.TraitsDisplay,
            after.Identity.TraitsDisplay);

        // ClassificationReasons (recalculé, jamais persisté)
        Assert.Equal(
            before.ClassificationReasons,
            after.ClassificationReasons);

        // ReadSectionCount
        Assert.Equal(
            before.ReadSectionCount,
            after.ReadSectionCount);
    }


    // ============================================================
    // COMMUNITY EVIDENCE APRÈS LE SNAPSHOT
    // ============================================================

    /// <summary>
    /// MainWindow calcule Community Evidence après le chargement
    /// local. Ce test vérifie que cette étape fonctionne à
    /// l'identique sur un snapshot restauré : les champs dont les
    /// comparateurs dépendent (Primary, Secondary, FullName, Traits)
    /// survivent au cache.
    /// </summary>
    [Fact]
    public void CommunityEvidence_StillComputesOnRestoredSnapshot()
    {
        Beatmap fresh = service.GetOrAnalyze(mapOnePath, 111);
        Beatmap restored = service.GetOrAnalyze(mapOnePath, 111);

        Assert.Empty(restored.HitObjects);

        List<CommunityTag> tags =
        [
            new CommunityTag { Name = "jump", Votes = 12 },
            new CommunityTag { Name = "stream", Votes = 7 },
            new CommunityTag { Name = "tech", Votes = 3 },
        ];

        // Exactement la séquence de MainWindow.
        GameplayTagComparisonResult freshTagComparison =
            GameplayTagComparer.Compare(
                tags,
                fresh.GameplayProfile.Identity.FullName,
                fresh.GameplayProfile.Identity.Traits);

        GameplayTagComparisonResult restoredTagComparison =
            GameplayTagComparer.Compare(
                tags,
                restored.GameplayProfile.Identity.FullName,
                restored.GameplayProfile.Identity.Traits);

        Assert.Equal(
            freshTagComparison.Score,
            restoredTagComparison.Score);
        Assert.Equal(
            freshTagComparison.Status,
            restoredTagComparison.Status);
        Assert.Equal(
            freshTagComparison.TotalVotes,
            restoredTagComparison.TotalVotes);

        CommunityIdentityAgreement freshAgreement =
            CommunityIdentityAgreementComparer.Compare(
                tags,
                fresh.GameplayProfile.Identity);

        CommunityIdentityAgreement restoredAgreement =
            CommunityIdentityAgreementComparer.Compare(
                tags,
                restored.GameplayProfile.Identity);

        Assert.Equal(
            freshAgreement.Agreement,
            restoredAgreement.Agreement);
        Assert.Equal(
            freshAgreement.Reliability,
            restoredAgreement.Reliability);
        Assert.Equal(
            freshAgreement.RelevantVotes,
            restoredAgreement.RelevantVotes);
        Assert.Equal(
            freshAgreement.MatchedFamilies,
            restoredAgreement.MatchedFamilies);
        Assert.Equal(
            freshAgreement.ConflictingFamilies,
            restoredAgreement.ConflictingFamilies);
    }

    /// <summary>
    /// Community Evidence n'est jamais mis en cache : un snapshot
    /// arrive vierge, et c'est bien l'appelant qui le renseigne.
    /// </summary>
    [Fact]
    public void RestoredSnapshot_CarriesNoCommunityEvidence()
    {
        service.GetOrAnalyze(mapOnePath, 111);
        Beatmap restored = service.GetOrAnalyze(mapOnePath, 111);

        Assert.Empty(restored.CommunityTags);
        Assert.Null(restored.TagComparison);
        Assert.Null(restored.CommunityIdentityAgreement);
    }
}
