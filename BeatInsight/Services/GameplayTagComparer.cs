using System;
using System.Collections.Generic;
using System.Linq;
using BeatInsight;

namespace BeatInsight.Services;

public static class GameplayTagComparer
{
    public static GameplayTagComparisonResult Compare(
        IEnumerable<CommunityTag> communityTags,
        string identityName,
        IEnumerable<string> identityTraits)
    {
        System.Collections.Generic.List<CommunityTag> tags =
            communityTags
                .Where(t => !string.IsNullOrWhiteSpace(t.Name))
                .ToList();

        if (tags.Count == 0)
        {
            return new GameplayTagComparisonResult
            {
                HasTags = false,
                Score = 0,
                Status = "No community tags",
                TotalVotes = 0,
                Matches =
                    new System.Collections.Generic.List<GameplayTagComparison>()
            };
        }

        string identity =
            identityName?.ToLowerInvariant() ?? "";

        System.Collections.Generic.List<string> traits =
            identityTraits?
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.ToLowerInvariant())
                .ToList()
            ?? new System.Collections.Generic.List<string>();

        // ============================================================
        // IDENTITÉ → CONCEPTS
        // ============================================================

        HashSet<string> identityConcepts =
            new HashSet<string>();

        AddConcepts(identity, identityConcepts);

        foreach (string trait in traits)
            AddConcepts(trait, identityConcepts);

        // ============================================================
        // COMPARAISON
        // ============================================================

        var matches =
            new System.Collections.Generic.List<GameplayTagComparison>();

        foreach (CommunityTag tag in tags)
        {
            string tagName =
                Normalize(tag.Name);

            System.Collections.Generic.List<string> concepts =
                ResolveTagConcepts(tagName);

            double score = 0.0;

            tag.MatchScore = score;
            tag.Verdict = GetTagVerdict(score);

            


            // --------------------------------------------------------
            // Correspondance directe
            // --------------------------------------------------------

            if (!string.IsNullOrWhiteSpace(identity) &&
                (identity.Contains(tagName) ||
                 tagName.Contains(identity)))
            {
                score = 1.0;
            }

            // --------------------------------------------------------
            // Correspondance par concepts
            // --------------------------------------------------------

            if (score < 1.0 && concepts.Count > 0)
            {
                int matchedConcepts =
                    concepts.Count(
                        concept =>
                            identityConcepts.Contains(concept));

                score =
                    (double)matchedConcepts /
                    concepts.Count;
            }

            // --------------------------------------------------------
            // Correspondance avec les traits
            // --------------------------------------------------------

            if (score < 1.0)
            {
                foreach (string trait in traits)
                {
                    string normalizedTrait =
                        Normalize(trait);

                    if (normalizedTrait.Contains(tagName) ||
                        tagName.Contains(normalizedTrait))
                    {
                        score =
                            Math.Max(score, 0.75);
                    }
                }
            }

            score =
                Math.Clamp(score, 0.0, 1.0);

            // ========================================================
            // POIDS DES VOTES
            // ========================================================

            // 1 vote   ≈ 0.301
            // 10 votes ≈ 1.041
            // 27 votes ≈ 1.447
            //
            // Les votes augmentent la confiance mais ne changent
            // jamais la compatibilité intrinsèque du tag.

            double voteWeight =
                Math.Log10(
                    Math.Max(0, tag.Votes) + 1);

            string status;

            if (score >= 0.75)
            {
                status = "✓ Cohérent";
            }
            else if (score >= 0.35)
            {
                status = "~ Partiellement cohérent";
            }
            else
            {
                status = "? Non confirmé";
            }

            matches.Add(
                new GameplayTagComparison
                {
                    Tag = tag.Name,
                    Votes = Math.Max(0, tag.Votes),
                    Status = status,
                    Score = score,
                    VoteWeight = voteWeight,
                    Concepts = concepts
                });
        }

        // ============================================================
        // SCORE GLOBAL PONDÉRÉ PAR LES VOTES
        // ============================================================

        double totalWeight =
            matches.Sum(m => m.VoteWeight);

        double weightedScore =
            totalWeight > 0
                ? matches.Sum(
                    m => m.Score * m.VoteWeight)
                  / totalWeight
                : 0.0;

        int totalVotes =
            tags.Sum(
                t => Math.Max(0, t.Votes));

        string globalStatus;

        if (weightedScore >= 0.75)
        {
            globalStatus = "Strong consistency";
        }
        else if (weightedScore >= 0.40)
        {
            globalStatus = "Moderate consistency";
        }
        else
        {
            globalStatus = "Weak consistency";
        }

        return new GameplayTagComparisonResult
        {
            HasTags = true,
            Score = weightedScore,
            Status = globalStatus,
            TotalVotes = totalVotes,
            Matches = matches
        };
    }

    // ================================================================
    // IDENTITÉ → CONCEPTS
    // ================================================================

    private static void AddConcepts(
     string text,
     HashSet<string> concepts)
    {
        string normalized =
            Normalize(text);

        // ============================================================
        // CONCEPTS SEMANTIQUES
        // ============================================================

        if (normalized.Contains("speed"))
        {
            concepts.Add("speed");
        }

        if (normalized.Contains("stream"))
        {
            concepts.Add("stream");
            concepts.Add("speed");
        }

        if (normalized.Contains("burst"))
        {
            concepts.Add("burst");
            concepts.Add("speed");
        }

        if (normalized.Contains("jump"))
        {
            concepts.Add("jump");
            concepts.Add("aim");
        }

        if (normalized.Contains("aim"))
        {
            concepts.Add("aim");
        }

        if (normalized.Contains("reading")
            || normalized.Contains("read"))
        {
            concepts.Add("reading");
        }

        if (normalized.Contains("tech")
            || normalized.Contains("technical"))
        {
            concepts.Add("tech");
        }

        if (normalized.Contains("timing")
            || normalized.Contains("rhythm"))
        {
            concepts.Add("timing");
            concepts.Add("rhythm");
        }

        if (normalized.Contains("density"))
        {
            concepts.Add("density");
            concepts.Add("reading");
        }

        if (normalized.Contains("clutter")
            || normalized.Contains("visual"))
        {
            concepts.Add("reading");
        }

        if (normalized.Contains("progression"))
        {
            concepts.Add("progression");
        }

        if (normalized.Contains("difficulty")
            || normalized.Contains("spike"))
        {
            concepts.Add("difficulty");
        }

        if (normalized.Contains("variable"))
        {
            concepts.Add("timing");
        }

        if (normalized.Contains("bpm")
            || normalized.Contains("accelerat"))
        {
            concepts.Add("speed");
        }

        if (normalized.Contains("clean"))
        {
            concepts.Add("clean");
        }

        if (normalized.Contains("repetition")
            || normalized.Contains("repeat"))
        {
            concepts.Add("repetition");
        }

        if (normalized.Contains("improvisation")
            || normalized.Contains("improv"))
        {
            concepts.Add("improvisation");
        }

        // ============================================================
        // MOTS GENERIQUES
        // ============================================================

        foreach (string word in normalized.Split(
                     new[]
                     {
                     ' ',
                     '-',
                     '_',
                     '/',
                     ','
                     },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            if (word.Length >= 4)
            {
                concepts.Add(word);
            }
        }
    }

    // ================================================================
    // TAG → CONCEPTS
    // ================================================================

    private static List<string> ResolveTagConcepts(string tag)
    {
        tag = Normalize(tag);

        HashSet<string> concepts = [];

        if (tag.Contains("stream"))
        {
            concepts.Add("stream");
            concepts.Add("speed");
        }

        if (tag.Contains("burst"))
        {
            concepts.Add("burst");
            concepts.Add("speed");
        }

        if (tag.Contains("speed")
            || tag.Contains("accelerat")
            || tag.Contains("bpm")
            || tag.Contains("high bpm"))
        {
            concepts.Add("speed");
        }

        if (tag.Contains("jump"))
        {
            concepts.Add("jump");
            concepts.Add("aim");
        }

        if (tag.Contains("aim")
            || tag.Contains("spacing")
            || tag.Contains("distance"))
        {
            concepts.Add("aim");
        }

        if (tag.Contains("tech")
            || tag.Contains("technical"))
        {
            concepts.Add("tech");
        }

        if (tag.Contains("finger"))
        {
            concepts.Add("tech");
            concepts.Add("fingercontrol");
        }

        if (tag.Contains("control"))
        {
            concepts.Add("fingercontrol");
        }

        if (tag.Contains("reading")
            || tag.Contains("read")
            || tag.Contains("visual")
            || tag.Contains("clutter")
            || tag.Contains("overlap"))
        {
            concepts.Add("reading");
        }

        if (tag.Contains("rhythm")
            || tag.Contains("timing"))
        {
            concepts.Add("rhythm");
            concepts.Add("timing");
        }

        if (tag.Contains("progression")
            || tag.Contains("difficulty spike")
            || tag.Contains("spike"))
        {
            concepts.Add("progression");
            concepts.Add("difficulty");
        }

        if (tag.Contains("repetition")
            || tag.Contains("repeat"))
        {
            concepts.Add("repetition");
        }

        if (tag.Contains("improvisation")
            || tag.Contains("improv"))
        {
            concepts.Add("improvisation");
        }

        if (tag.Contains("clean"))
        {
            concepts.Add("clean");
        }

        return concepts.ToList();
    }

    // ================================================================
    // NORMALISATION
    // ================================================================

    private static string Normalize(string value)
    {
        return value
            .Trim()
            .ToLowerInvariant()
            .Replace("_", " ")
            .Replace("-", " ");
    }

    private static string GetTagVerdict(double score)
    {
        if (score >= 80.0)
            return "Strongly supported";

        if (score >= 60.0)
            return "Supported";

        if (score >= 40.0)
            return "Partially supported";

        if (score > 0.0)
            return "Weakly supported";

        return "Not supported";
    }
}

// ====================================================================
// TAG COMPARISON
// ====================================================================

public sealed class GameplayTagComparison
{
    public string Tag { get; init; } = "";

    public int Votes { get; init; }

    public string Status { get; init; } = "";

    public double Score { get; init; }

    public double VoteWeight { get; init; }

    public System.Collections.Generic.List<string> Concepts { get; init; } =
        new System.Collections.Generic.List<string>();
}


// ====================================================================
// RESULT
// ====================================================================

public sealed class GameplayTagComparisonResult
{
    public bool HasTags { get; init; }

    public double Score { get; init; }

    public string Status { get; init; } = "";

    public int TotalVotes { get; init; }

    public System.Collections.Generic.List<GameplayTagComparison> Matches { get; init; } =
        new System.Collections.Generic.List<GameplayTagComparison>();
}