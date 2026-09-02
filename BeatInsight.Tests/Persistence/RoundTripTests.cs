using BeatInsight.Models;
using BeatInsight.Models.Persistence;
using BeatInsight.Services.Persistence;

namespace BeatInsight.Tests.Persistence;

/// <summary>
/// Vérifie que le passage
/// Beatmap -> BeatmapAnalysisRecord -> Beatmap
/// préserve exactement toutes les valeurs sources consommées par
/// l'interface et par les deux rapports.
///
/// Ces tests constituent le filet de sécurité de la persistance :
/// un champ oublié dans le mapper vaudrait 0 ou "" silencieusement,
/// et produirait un affichage faux sans lever d'erreur.
///
/// Les six fixtures sont celles utilisées pour le benchmark V1.
/// </summary>
public sealed class RoundTripTests
{
    private const string TowerOfHeaven =
        "Tower Of Heaven [Extra].osu";

    private const string FreedomDiveFrenZ =
        "FREEDOM DiVE [FrenZ's Insane].osu";

    private const string FreedomDiveArles =
        "FREEDOM DiVE [Arles].osu";

    private const string Frozen =
        "Frozen [Collab Insane].osu";

    private const string ExitPrimordial =
        "Exit This Earth's Atmosphere [Primordial Nucleosynthesis].osu";

    private const string AshiotoTarteTatin =
        "Ashioto Tarte Tatin [Koori's Insane].osu";

    // Valeurs de fichier déterministes : le mapper ne fait aucune I/O,
    // ces informations sont normalement fournies par l'appelant.
    private const string StubPath = @"C:\Songs\stub\map.osu";
    private const long StubSize = 123_456L;
    private const int StubBeatmapId = 4_242;

    private static readonly DateTime StubLastWriteUtc =
        new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    private static readonly DateTime StubAnalysedAtUtc =
        new(2026, 6, 7, 8, 9, 10, DateTimeKind.Utc);


    // ============================================================
    // HELPERS
    // ============================================================

    private static (Beatmap Original,
                    BeatmapAnalysisRecord Record,
                    Beatmap Restored) RoundTrip(string fixtureName)
    {
        Beatmap original = FixtureLoader.Load(fixtureName);

        BeatmapAnalysisRecord record =
            BeatmapAnalysisMapper.ToRecord(
                original,
                StubPath,
                StubSize,
                StubLastWriteUtc,
                StubAnalysedAtUtc,
                StubBeatmapId);

        Beatmap restored =
            BeatmapAnalysisMapper.ToBeatmap(record);

        return (original, record, restored);
    }


    // ============================================================
    // MÉTADONNÉES UI
    // ============================================================

    [Theory]
    [InlineData(TowerOfHeaven)]
    [InlineData(FreedomDiveFrenZ)]
    [InlineData(FreedomDiveArles)]
    [InlineData(Frozen)]
    [InlineData(ExitPrimordial)]
    [InlineData(AshiotoTarteTatin)]
    public void RoundTrip_PreservesUiMetadata(string fixtureName)
    {
        var (original, _, restored) = RoundTrip(fixtureName);

        Assert.Equal(original.Title, restored.Title);
        Assert.Equal(original.Artist, restored.Artist);
        Assert.Equal(original.Creator, restored.Creator);
        Assert.Equal(original.Version, restored.Version);

        Assert.Equal(original.Length, restored.Length);
        Assert.Equal(original.BPM, restored.BPM);
        Assert.Equal(original.MaxCombo, restored.MaxCombo);

        Assert.Equal(original.AR, restored.AR);
        Assert.Equal(original.OD, restored.OD);
        Assert.Equal(original.CS, restored.CS);
        Assert.Equal(original.HP, restored.HP);

        Assert.Equal(original.CircleCount, restored.CircleCount);
        Assert.Equal(original.SliderCount, restored.SliderCount);
        Assert.Equal(original.SpinnerCount, restored.SpinnerCount);
    }


    // ============================================================
    // RATINGS
    // ============================================================

    [Theory]
    [InlineData(TowerOfHeaven)]
    [InlineData(FreedomDiveFrenZ)]
    [InlineData(FreedomDiveArles)]
    [InlineData(Frozen)]
    [InlineData(ExitPrimordial)]
    [InlineData(AshiotoTarteTatin)]
    public void RoundTrip_PreservesRatings(string fixtureName)
    {
        var (original, _, restored) = RoundTrip(fixtureName);

        Assert.Equal(original.OsuStarRating, restored.OsuStarRating);
        Assert.Equal(
            original.BeatInsightRating,
            restored.BeatInsightRating);
    }


    // ============================================================
    // FAMILLES STRUCTURELLES ET TECH
    // ============================================================

    [Theory]
    [InlineData(TowerOfHeaven)]
    [InlineData(FreedomDiveFrenZ)]
    [InlineData(FreedomDiveArles)]
    [InlineData(Frozen)]
    [InlineData(ExitPrimordial)]
    [InlineData(AshiotoTarteTatin)]
    public void RoundTrip_PreservesStructuralAndTechValues(
        string fixtureName)
    {
        var (original, _, restored) = RoundTrip(fixtureName);

        GameplayProfile before = original.GameplayProfile;
        GameplayProfile after = restored.GameplayProfile;

        Assert.Equal(before.StreamRatio, after.StreamRatio);
        Assert.Equal(before.JumpRatio, after.JumpRatio);
        Assert.Equal(before.BurstRatio, after.BurstRatio);

        // TechPresence et TechScore sont deux grandeurs distinctes :
        // couverture structurelle et pression finale. Les deux sont
        // vérifiées séparément, sans confusion possible.
        Assert.Equal(before.TechPresence, after.TechPresence);
        Assert.Equal(before.TechScore, after.TechScore);
    }


    // ============================================================
    // PRESSIONS DE SKILL
    // ============================================================

    [Theory]
    [InlineData(TowerOfHeaven)]
    [InlineData(FreedomDiveFrenZ)]
    [InlineData(FreedomDiveArles)]
    [InlineData(Frozen)]
    [InlineData(ExitPrimordial)]
    [InlineData(AshiotoTarteTatin)]
    public void RoundTrip_PreservesSkillPressures(string fixtureName)
    {
        var (original, _, restored) = RoundTrip(fixtureName);

        GameplayProfile before = original.GameplayProfile;
        GameplayProfile after = restored.GameplayProfile;

        Assert.Equal(before.AimScore, after.AimScore);
        Assert.Equal(before.SpeedScore, after.SpeedScore);
        Assert.Equal(before.ReadScore, after.ReadScore);
    }


    // ============================================================
    // IDENTITÉ ET TRAITS
    // ============================================================

    [Theory]
    [InlineData(TowerOfHeaven)]
    [InlineData(FreedomDiveFrenZ)]
    [InlineData(FreedomDiveArles)]
    [InlineData(Frozen)]
    [InlineData(ExitPrimordial)]
    [InlineData(AshiotoTarteTatin)]
    public void RoundTrip_PreservesIdentityAndTraits(string fixtureName)
    {
        var (original, _, restored) = RoundTrip(fixtureName);

        GameplayIdentity before = original.GameplayProfile.Identity;
        GameplayIdentity after = restored.GameplayProfile.Identity;

        Assert.Equal(before.Primary, after.Primary);
        Assert.Equal(before.Secondary, after.Secondary);
        Assert.Equal(before.Pattern, after.Pattern);
        Assert.Equal(before.Confidence, after.Confidence);

        // Ordre inclus : les traits sont affichés dans l'ordre produit
        // par l'analyzer.
        Assert.Equal(before.Traits, after.Traits);
    }


    // ============================================================
    // CHAMPS SOURCES DE CopyAnalysis_Click
    // ============================================================

    [Theory]
    [InlineData(TowerOfHeaven)]
    [InlineData(FreedomDiveFrenZ)]
    [InlineData(FreedomDiveArles)]
    [InlineData(Frozen)]
    [InlineData(ExitPrimordial)]
    [InlineData(AshiotoTarteTatin)]
    public void RoundTrip_PreservesCopyAnalysisFields(string fixtureName)
    {
        var (original, _, restored) = RoundTrip(fixtureName);

        GameplayProfile before = original.GameplayProfile;
        GameplayProfile after = restored.GameplayProfile;

        // Identité et confiance
        Assert.Equal(
            before.Identity.FullName,
            after.Identity.FullName);
        Assert.Equal(
            before.Identity.Confidence,
            after.Identity.Confidence);

        // Patterns
        Assert.Equal(before.StreamRatio, after.StreamRatio);
        Assert.Equal(before.JumpRatio, after.JumpRatio);
        Assert.Equal(before.BurstRatio, after.BurstRatio);

        // Scores
        Assert.Equal(before.TechScore, after.TechScore);
        Assert.Equal(before.ReadScore, after.ReadScore);
        Assert.Equal(before.SpeedScore, after.SpeedScore);
        Assert.Equal(before.AimScore, after.AimScore);

        // Signaux Tech
        Assert.Equal(
            before.TechTransitionSignal,
            after.TechTransitionSignal);
        Assert.Equal(
            before.TechStructureSignal,
            after.TechStructureSignal);
        Assert.Equal(
            before.TechSpatialSignal,
            after.TechSpatialSignal);
        Assert.Equal(
            before.TechTemporalSignal,
            after.TechTemporalSignal);

        // Signaux Reading
        Assert.Equal(
            before.ReadDensitySignal,
            after.ReadDensitySignal);
        Assert.Equal(
            before.ReadClutterSignal,
            after.ReadClutterSignal);
        Assert.Equal(before.ReadCSSignal, after.ReadCSSignal);
        Assert.Equal(before.ReadIntensity, after.ReadIntensity);
        Assert.Equal(before.ReadCoverage, after.ReadCoverage);
        Assert.Equal(
            before.ReadPredictability,
            after.ReadPredictability);
        Assert.Equal(before.ReadNovelty, after.ReadNovelty);
        Assert.Equal(
            before.ReadTemporalRegularity,
            after.ReadTemporalRegularity);
        Assert.Equal(
            before.ReadSpacingRegularity,
            after.ReadSpacingRegularity);
        Assert.Equal(
            before.ReadTrajectoryRepetition,
            after.ReadTrajectoryRepetition);
        Assert.Equal(before.ReadAmbiguity, after.ReadAmbiguity);

        // Signaux Speed
        Assert.Equal(
            before.SpeedFastObjectRatio,
            after.SpeedFastObjectRatio);
        Assert.Equal(
            before.SpeedDensitySignal,
            after.SpeedDensitySignal);
        Assert.Equal(before.SpeedARSignal, after.SpeedARSignal);

        // Signaux Aim
        Assert.Equal(
            before.AimDistanceSignal,
            after.AimDistanceSignal);
        Assert.Equal(before.AimSpeedSignal, after.AimSpeedSignal);
        Assert.Equal(before.AimAngleSignal, after.AimAngleSignal);
        Assert.Equal(
            before.AimTemporalSignal,
            after.AimTemporalSignal);
    }


    // ============================================================
    // CHAMPS SOURCES DE ReportClassification_Click
    // ============================================================

    [Theory]
    [InlineData(TowerOfHeaven)]
    [InlineData(FreedomDiveFrenZ)]
    [InlineData(FreedomDiveArles)]
    [InlineData(Frozen)]
    [InlineData(ExitPrimordial)]
    [InlineData(AshiotoTarteTatin)]
    public void RoundTrip_PreservesReportClassificationFields(
        string fixtureName)
    {
        var (original, _, restored) = RoundTrip(fixtureName);

        Assert.Equal(original.Title, restored.Title);
        Assert.Equal(original.Version, restored.Version);

        GameplayProfile before = original.GameplayProfile;
        GameplayProfile after = restored.GameplayProfile;

        Assert.Equal(
            before.Identity.FullName,
            after.Identity.FullName);
        Assert.Equal(
            before.Identity.Confidence,
            after.Identity.Confidence);
        Assert.Equal(
            before.Identity.Traits,
            after.Identity.Traits);

        Assert.Equal(before.StreamRatio, after.StreamRatio);
        Assert.Equal(before.JumpRatio, after.JumpRatio);
        Assert.Equal(before.BurstRatio, after.BurstRatio);

        Assert.Equal(before.TechScore, after.TechScore);
        Assert.Equal(before.ReadScore, after.ReadScore);
        Assert.Equal(before.SpeedScore, after.SpeedScore);
        Assert.Equal(before.AimScore, after.AimScore);
    }


    // ============================================================
    // PROPRIÉTÉS DÉRIVÉES
    //
    // Elles ne sont jamais persistées : elles doivent se reconstruire
    // à l'identique depuis les champs sources.
    // ============================================================

    [Theory]
    [InlineData(TowerOfHeaven)]
    [InlineData(FreedomDiveFrenZ)]
    [InlineData(FreedomDiveArles)]
    [InlineData(Frozen)]
    [InlineData(ExitPrimordial)]
    [InlineData(AshiotoTarteTatin)]
    public void RoundTrip_RecalculatesDerivedProperties(
        string fixtureName)
    {
        var (original, _, restored) = RoundTrip(fixtureName);

        GameplayProfile before = original.GameplayProfile;
        GameplayProfile after = restored.GameplayProfile;

        // ClassificationReasons est recalculé par le modèle à partir
        // des sept scalaires dont il dépend.
        Assert.Equal(
            before.ClassificationReasons,
            after.ClassificationReasons);

        Assert.Equal(
            before.Identity.FullName,
            after.Identity.FullName);
        Assert.Equal(
            before.Identity.TraitsDisplay,
            after.Identity.TraitsDisplay);
        Assert.Equal(original.LengthDisplay, restored.LengthDisplay);
    }


    // ============================================================
    // ReadSectionCount
    //
    // Le compteur est la seule information de sections Reading qui
    // survit au cache : les sections elles-mêmes ne sont pas
    // persistées et ne doivent pas être fabriquées.
    // ============================================================

    /// <summary>
    /// Sur une analyse fraîche, le compteur scalaire doit être
    /// strictement égal au cardinal réel des sections Reading.
    ///
    /// C'est l'invariant qui rend le compteur digne de confiance :
    /// s'il se désynchronisait, tout le reste de la chaîne
    /// propagerait une valeur fausse.
    /// </summary>
    [Theory]
    [InlineData(TowerOfHeaven)]
    [InlineData(FreedomDiveFrenZ)]
    [InlineData(FreedomDiveArles)]
    [InlineData(Frozen)]
    [InlineData(ExitPrimordial)]
    [InlineData(AshiotoTarteTatin)]
    public void FreshAnalysis_ReadSectionCountMatchesReadSections(
        string fixtureName)
    {
        Beatmap original = FixtureLoader.Load(fixtureName);

        Assert.Equal(
            original.GameplayProfile.ReadSections.Count,
            original.GameplayProfile.ReadSectionCount);
    }

    [Theory]
    [InlineData(TowerOfHeaven)]
    [InlineData(FreedomDiveFrenZ)]
    [InlineData(FreedomDiveArles)]
    [InlineData(Frozen)]
    [InlineData(ExitPrimordial)]
    [InlineData(AshiotoTarteTatin)]
    public void ToRecord_CapturesReadSectionCount(string fixtureName)
    {
        var (original, record, _) = RoundTrip(fixtureName);

        Assert.Equal(
            original.GameplayProfile.ReadSectionCount,
            record.Profile.ReadSectionCount);

        // Redondant par construction, mais verrouille l'invariant
        // à l'endroit exact où la valeur entre en persistance.
        Assert.Equal(
            original.GameplayProfile.ReadSections.Count,
            record.Profile.ReadSectionCount);
    }

    /// <summary>
    /// Garde-fou anti-trivialité.
    ///
    /// Si toutes les fixtures produisaient zéro section Reading, les
    /// tests de round-trip du compteur passeraient sans rien
    /// démontrer. Ce test garantit qu'au moins une fixture exerce
    /// réellement une valeur non nulle.
    /// </summary>
    [Fact]
    public void Fixtures_ProduceAtLeastOneNonZeroReadSectionCount()
    {
        string[] fixtures =
        [
            TowerOfHeaven,
            FreedomDiveFrenZ,
            FreedomDiveArles,
            Frozen,
            ExitPrimordial,
            AshiotoTarteTatin,
        ];

        int total = 0;

        foreach (string fixtureName in fixtures)
        {
            total += FixtureLoader
                .Load(fixtureName)
                .GameplayProfile
                .ReadSectionCount;
        }

        Assert.True(
            total > 0,
            "Aucune fixture ne produit de section Reading : le "
            + "round-trip de ReadSectionCount ne serait pas "
            + "réellement testé.");
    }

    /// <summary>
    /// Le compteur doit survivre au round-trip complet, alors que les
    /// sections restent vides sur le snapshot.
    /// </summary>
    [Theory]
    [InlineData(TowerOfHeaven)]
    [InlineData(FreedomDiveFrenZ)]
    [InlineData(FreedomDiveArles)]
    [InlineData(Frozen)]
    [InlineData(ExitPrimordial)]
    [InlineData(AshiotoTarteTatin)]
    public void RoundTrip_PreservesReadSectionCount(string fixtureName)
    {
        var (original, _, restored) = RoundTrip(fixtureName);

        Assert.Equal(
            original.GameplayProfile.ReadSectionCount,
            restored.GameplayProfile.ReadSectionCount);

        // Le compteur reste exploitable même si les sections ont
        // disparu : c'est précisément ce qui permet aux rapports de
        // rester exacts sur un cache hit.
        Assert.Empty(restored.GameplayProfile.ReadSections);
    }


    // ============================================================
    // CHAMPS D'IDENTITÉ ET D'INVALIDATION
    // ============================================================

    [Theory]
    [InlineData(TowerOfHeaven)]
    [InlineData(AshiotoTarteTatin)]
    public void ToRecord_StampsIdentityAndInvalidationFields(
        string fixtureName)
    {
        var (_, record, _) = RoundTrip(fixtureName);

        Assert.Equal(StubPath, record.FilePath);
        Assert.Equal(StubSize, record.FileSize);
        Assert.Equal(StubLastWriteUtc, record.FileLastWriteUtc);
        Assert.Equal(StubAnalysedAtUtc, record.AnalysedAtUtc);
        Assert.Equal(StubBeatmapId, record.BeatmapId);

        Assert.Equal(
            BeatInsight.Analysis.AnalyzerVersion.Current,
            record.AnalyzerVersion);
        Assert.Equal(
            PersistenceSchemaVersion.Current,
            record.SchemaVersion);

        // Md5 reste réservé et non alimenté à ce stade.
        Assert.Null(record.Md5);
    }


    // ============================================================
    // LIMITATION ASSUMÉE : SNAPSHOT DE PRÉSENTATION
    //
    // Ce test verrouille le fait que le mapper ne fabrique AUCUNE
    // donnée non persistée. Si un jour ces collections sont
    // reconstruites artificiellement, ce test doit échouer et forcer
    // une décision explicite.
    // ============================================================

    [Theory]
    [InlineData(TowerOfHeaven)]
    [InlineData(ExitPrimordial)]
    public void ToBeatmap_DoesNotFabricateUnpersistedData(
        string fixtureName)
    {
        var (original, _, restored) = RoundTrip(fixtureName);

        // La beatmap d'origine possède bien ces données...
        Assert.NotEmpty(original.HitObjects);
        Assert.NotEmpty(original.TimingPoints);

        // ...mais le snapshot ne les invente pas.
        Assert.Empty(restored.HitObjects);
        Assert.Empty(restored.TimingPoints);
        Assert.Empty(restored.GameplayProfile.ReadSections);
        Assert.Empty(restored.GameplayProfile.StreamSections);
        Assert.Empty(restored.GameplayProfile.JumpSections);
        Assert.Empty(restored.GameplayProfile.TechSections);
        Assert.Empty(restored.GameplayProfile.SpeedSections);
        Assert.Empty(restored.GameplayProfile.StreamSequences);
        Assert.Empty(restored.GameplayProfile.JumpSequences);
        Assert.Empty(restored.GameplayProfile.BurstSequences);

        // Community Evidence n'est jamais mis en cache.
        Assert.Empty(restored.CommunityTags);
        Assert.Null(restored.TagComparison);
        Assert.Null(restored.CommunityIdentityAgreement);
    }
}
