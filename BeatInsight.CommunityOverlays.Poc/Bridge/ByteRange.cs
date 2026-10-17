namespace BeatInsight.CommunityOverlays.Poc.Bridge;

/// <summary>
/// En-tête HTTP <c>Range</c> à intervalle unique (<c>bytes=début-fin</c>,
/// <c>bytes=début-</c>, <c>bytes=-suffixe</c>), utilisé par les éléments
/// audio et vidéo. tosu accepte les mêmes formes à intervalle unique.
/// </summary>
public readonly record struct ByteRange(long Start, long End)
{
    public long Length => End - Start + 1;

    /// <summary>
    /// Null si l'en-tête est absent ou inexploitable (réponse 200 complète).
    /// <paramref name="satisfiable"/> faux si l'intervalle est hors du fichier (416).
    /// </summary>
    public static ByteRange? Parse(string? header, long fileSize, out bool satisfiable)
    {
        satisfiable = true;
        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
            return null;

        string spec = header["bytes=".Length..].Trim();
        if (spec.Contains(','))
            return null; // plusieurs intervalles : non pris en charge, réponse complète

        int dash = spec.IndexOf('-');
        if (dash < 0)
            return null;

        string first = spec[..dash].Trim();
        string last = spec[(dash + 1)..].Trim();

        if (first.Length == 0)
        {
            // bytes=-N : les N derniers octets.
            if (!long.TryParse(last, out long suffix) || suffix <= 0)
                return null;
            if (fileSize == 0) { satisfiable = false; return null; }
            return new ByteRange(Math.Max(0, fileSize - suffix), fileSize - 1);
        }

        if (!long.TryParse(first, out long start) || start < 0)
            return null;
        if (start >= fileSize)
        {
            satisfiable = false;
            return null;
        }

        long end = fileSize - 1;
        if (last.Length > 0)
        {
            if (!long.TryParse(last, out end) || end < start)
                return null;
            end = Math.Min(end, fileSize - 1); // RFC 9110 : borne ramenée à la taille du fichier
        }

        return new ByteRange(start, end);
    }
}
