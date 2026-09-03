namespace BeatInsight.Services;

/// <summary>
/// Catégorie de sampling utilisée pour construire un pack de
/// calibration. Sert uniquement à guider quelles maps prioriser lors
/// de la labellisation ; ne correspond à aucune valeur de
/// <see cref="BeatInsight.Models.Persistence.MlHumanLabel"/> et ne
/// doit jamais être écrite comme telle (voir <see cref="CalibrationQueue"/>).
/// </summary>
internal enum CalibrationPackBucket
{
    Aim,
    Stream,
    Alternate,
    Reading,
    TechControl,
}

/// <summary>
/// Un identifiant osu! du pack, associé à son bucket d'origine à titre
/// purement indicatif.
/// </summary>
internal readonly record struct CalibrationPackEntry(
    int BeatmapId,
    CalibrationPackBucket Bucket);

/// <summary>
/// BeatInsight Calibration Pack #1 : 100 Beatmap IDs communautaires
/// utilisés pour prioriser la labellisation humaine parmi le dataset
/// déjà capturé.
///
/// Ce pack ne crée, ne modifie ni ne supprime aucun sample : il ne
/// sert qu'à filtrer/ordonner des <c>MlDatasetSample</c> déjà présents
/// via leur <c>BeatmapId</c>. L'ordre de la liste ci-dessous (Aim,
/// Stream, Alternate, Reading, TechControl, chacun dans l'ordre reçu)
/// est la source de vérité de l'ordre déterministe de la file de
/// calibration.
/// </summary>
internal static class CalibrationPack
{
    internal static IReadOnlyList<CalibrationPackEntry> Pack1 { get; } = BuildPack1();

    private static IReadOnlyList<CalibrationPackEntry> BuildPack1()
    {
        List<CalibrationPackEntry> entries = new(100);

        AddBucket(entries, CalibrationPackBucket.Aim,
        [
            774965, 790218, 2123381, 192508, 93842, 1593298, 1286225,
            1980432, 2064221, 2338623, 2701875, 1046830, 678461, 959406,
            3544929, 1209160, 674350,
        ]);

        AddBucket(entries, CalibrationPackBucket.Stream,
        [
            1469240, 847314, 3018109, 2061330, 638017, 221777, 759056,
            550235, 176960, 140665, 205282, 3385969, 2469254, 1620228,
            2316176, 1989718, 2163407, 3122865, 2615748, 418761, 2591748,
            1256809, 2223742, 2132926, 1537313, 742728, 4392407, 83974,
            2021990, 1171254, 2317735, 4282790, 4379484, 4651523,
            2015472, 2352543,
        ]);

        AddBucket(entries, CalibrationPackBucket.Alternate,
        [
            2114276, 973607, 1580330, 2087332, 3457798, 736214, 1391928,
            2449773, 96763, 3160676, 1211730, 235879, 669374, 2534930,
            897968, 137296, 928868,
        ]);

        AddBucket(entries, CalibrationPackBucket.Reading,
        [
            797206, 2755563, 4392029, 2247022, 2226170, 2701267,
            3573455, 3505833, 5255506, 3355800, 4500445, 2666318,
            4420171, 3309941, 4241518, 1142960,
        ]);

        AddBucket(entries, CalibrationPackBucket.TechControl,
        [
            785982, 3186407, 1821081, 2916575, 2549390, 1939172,
            1040942, 471598, 2612742, 2061428, 3014448, 4187139,
            4423300, 2119076,
        ]);

        return entries;
    }

    private static void AddBucket(
        List<CalibrationPackEntry> entries,
        CalibrationPackBucket bucket,
        int[] beatmapIds)
    {
        foreach (int beatmapId in beatmapIds)
        {
            entries.Add(new CalibrationPackEntry(beatmapId, bucket));
        }
    }
}
