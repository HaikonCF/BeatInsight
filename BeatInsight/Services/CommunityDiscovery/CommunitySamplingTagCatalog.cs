using BeatInsight.Models.Discovery;

namespace BeatInsight.Services.CommunityDiscovery;

/// <summary>
/// Correspondance isolée entre la taxonomie de sampling BeatInsight et les
/// noms officiels des user tags osu! applicables au mode osu!standard.
///
/// Cette correspondance ne référence jamais MlHumanLabel et ne crée aucun
/// label. Les noms viennent du catalogue officiel osu! :
/// https://osu.ppy.sh/wiki/en/Beatmap/Beatmap_tags
/// </summary>
internal static class CommunitySamplingTagCatalog
{
    private static readonly IReadOnlyDictionary<
        CommunitySamplingFamily,
        IReadOnlySet<string>> TagsByFamily =
        new Dictionary<CommunitySamplingFamily, IReadOnlySet<string>>
        {
            [CommunitySamplingFamily.Jump] = NewTagSet(
                "skillset/jumps",
                "jumps/sharp",
                "jumps/wide",
                "jumps/linear",
                "jumps/triangles",
                "jumps/squares",
                "jumps/stars",
                "jumps/back and forth",
                "jumps/freeform",
                "jumps/cross-screen",
                "jumps/stamina"),
            [CommunitySamplingFamily.Stream] = NewTagSet(
                "skillset/streams",
                "streams/doubles",
                "streams/quads",
                "streams/bursts",
                "streams/stamina",
                "streams/speed",
                "streams/flow aim",
                "streams/spaced streams",
                "streams/cutstreams"),
            [CommunitySamplingFamily.Tech] = NewTagSet(
                "skillset/tech",
                "tech/slider tech",
                "tech/aim control",
                "tech/finger control"),
            [CommunitySamplingFamily.Reading] = NewTagSet(
                "skillset/reading",
                "reading/overlaps",
                "reading/perfect stacks",
                "reading/visually dense"),
        };

    // Peu de requêtes de départ, choisies parmi les tags officiels les plus
    // génériques. Les tags complets ci-dessus servent ensuite au filtrage et
    // au scoring. Hybrid requiert toujours au moins deux familles réelles.
    private static readonly IReadOnlyDictionary<
        CommunitySamplingFamily,
        IReadOnlyList<string>> SearchTagsByFamily =
        new Dictionary<CommunitySamplingFamily, IReadOnlyList<string>>
        {
            [CommunitySamplingFamily.Jump] =
                ["skillset/jumps", "jumps/sharp", "jumps/cross-screen"],
            [CommunitySamplingFamily.Stream] =
                ["skillset/streams", "streams/stamina", "streams/speed"],
            [CommunitySamplingFamily.Tech] =
                ["skillset/tech", "tech/slider tech", "tech/aim control"],
            [CommunitySamplingFamily.Reading] =
                ["skillset/reading", "reading/overlaps", "reading/visually dense"],
            [CommunitySamplingFamily.Hybrid] =
                ["skillset/jumps", "skillset/streams", "skillset/tech", "skillset/reading"],
        };

    internal static IReadOnlyList<string> GetSearchTags(
        CommunitySamplingFamily family) =>
        SearchTagsByFamily.TryGetValue(family, out IReadOnlyList<string>? tags)
            ? tags
            : [];

    internal static IReadOnlyList<CommunitySamplingFamily> ResolveFamilies(
        string tagName)
    {
        string normalized = Normalize(tagName);

        return TagsByFamily
            .Where(pair => pair.Value.Contains(normalized))
            .Select(pair => pair.Key)
            .OrderBy(family => family)
            .ToArray();
    }

    internal static bool MatchesFamily(
        IEnumerable<CommunityBeatmapUserTag> tags,
        CommunitySamplingFamily family)
    {
        IReadOnlyDictionary<CommunitySamplingFamily, double> evidence =
            CalculateFamilyEvidence(tags);

        if (family == CommunitySamplingFamily.Hybrid)
        {
            return evidence.Count(pair => pair.Value > 0.0) >= 2;
        }

        return evidence.TryGetValue(family, out double value) && value > 0.0;
    }

    /// <summary>
    /// Vérifie la provenance d'un candidat retourné par la requête
    /// <c>tag="..."</c> osu!web. Cela établit une pertinence de recherche,
    /// mais ne fabrique ni vote ni preuve communautaire détaillée.
    /// </summary>
    internal static bool SearchTagsMatchFamily(
        IEnumerable<string> searchTagNames,
        CommunitySamplingFamily family)
    {
        ArgumentNullException.ThrowIfNull(searchTagNames);

        CommunitySamplingFamily[] matchedFamilies = searchTagNames
            .Where(tagName => !string.IsNullOrWhiteSpace(tagName))
            .SelectMany(ResolveFamilies)
            .Distinct()
            .ToArray();

        return family == CommunitySamplingFamily.Hybrid
            ? matchedFamilies.Length >= 2
            : matchedFamilies.Contains(family);
    }

    internal static int CountSearchTagMatches(
        IEnumerable<string> searchTagNames,
        CommunitySamplingFamily family) => searchTagNames
            .Where(tagName => !string.IsNullOrWhiteSpace(tagName))
            .Count(tagName => ResolveFamilies(tagName).Contains(family));

    internal static IReadOnlyDictionary<CommunitySamplingFamily, double>
        CalculateFamilyEvidence(IEnumerable<CommunityBeatmapUserTag> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);

        var evidence = new Dictionary<CommunitySamplingFamily, double>();

        foreach (CommunityBeatmapUserTag tag in tags)
        {
            int votes = Math.Max(0, tag.Votes);

            if (votes == 0)
            {
                continue;
            }

            double weight = Math.Log10(votes + 1.0);

            foreach (CommunitySamplingFamily family in ResolveFamilies(tag.Name))
            {
                evidence[family] = evidence.GetValueOrDefault(family) + weight;
            }
        }

        return evidence;
    }

    internal static double GetEvidenceScore(
        IReadOnlyDictionary<CommunitySamplingFamily, double> evidence,
        CommunitySamplingFamily family)
    {
        if (family != CommunitySamplingFamily.Hybrid)
        {
            return evidence.GetValueOrDefault(family);
        }

        double[] strongestFamilies = evidence.Values
            .Where(value => value > 0.0)
            .OrderByDescending(value => value)
            .Take(2)
            .ToArray();

        if (strongestFamilies.Length < 2)
        {
            return 0.0;
        }

        // Hybrid valorise une seconde preuve significative plutôt qu'une
        // unique famille dominante. Aucun équivalent de Classic/Mixed n'est
        // introduit dans la taxonomie communautaire.
        return strongestFamilies[1] + strongestFamilies[0] * 0.25;
    }

    private static IReadOnlySet<string> NewTagSet(params string[] tags) =>
        new HashSet<string>(tags.Select(Normalize), StringComparer.Ordinal);

    private static string Normalize(string value) =>
        value.Trim().ToLowerInvariant();
}
