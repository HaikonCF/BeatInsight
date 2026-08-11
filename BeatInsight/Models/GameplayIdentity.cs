using System.Collections.Generic;

namespace BeatInsight.Models;

public sealed class GameplayIdentity
{
    public string Primary { get; init; } = "";

    public string Secondary { get; init; } = "";

    public string Pattern { get; init; } = "";

    public double Confidence { get; init; }

    public List<string> Traits { get; init; } = [];

    public string FullName => $"{Pattern} {Primary}";
    public string TraitsDisplay =>
    Traits.Count == 0
        ? "None"
        : string.Join(" • ", Traits.Distinct());
}