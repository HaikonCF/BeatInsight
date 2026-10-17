using System.Globalization;

namespace BeatInsight.Diagnostics.NativeStable;

/// <summary>
/// EXPERIMENTAL — V3 PROOF OF CONCEPT — NOT PRODUCTION INTEGRATION.
/// Independently authored wildcard byte-pattern parser and scanner.
/// </summary>
internal sealed class BytePattern
{
    private readonly byte?[] bytes;

    private BytePattern(byte?[] bytes)
    {
        this.bytes = bytes;
    }

    internal int Length => bytes.Length;

    internal static BytePattern Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        string[] tokens = value.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            throw new ArgumentException("A byte pattern must contain at least one token.", nameof(value));
        }

        var parsed = new byte?[tokens.Length];
        for (int i = 0; i < tokens.Length; i++)
        {
            if (tokens[i] == "??")
            {
                parsed[i] = null;
                continue;
            }

            if (tokens[i].Length != 2 ||
                !byte.TryParse(
                    tokens[i],
                    NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture,
                    out byte exact))
            {
                throw new ArgumentException($"Invalid byte-pattern token at index {i}.", nameof(value));
            }

            parsed[i] = exact;
        }

        return new BytePattern(parsed);
    }

    internal static BytePattern FromBytes(ReadOnlySpan<byte> value)
    {
        if (value.IsEmpty)
        {
            throw new ArgumentException("The pattern cannot be empty.", nameof(value));
        }

        var parsed = new byte?[value.Length];
        for (int i = 0; i < value.Length; i++)
        {
            parsed[i] = value[i];
        }

        return new BytePattern(parsed);
    }

    internal IReadOnlyList<int> FindAll(
        ReadOnlySpan<byte> buffer,
        int maximumMatches = int.MaxValue)
    {
        if (maximumMatches <= 0 || buffer.Length < bytes.Length)
        {
            return [];
        }

        var matches = new List<int>();
        int lastStart = buffer.Length - bytes.Length;

        for (int start = 0; start <= lastStart; start++)
        {
            bool matchesAtStart = true;
            for (int offset = 0; offset < bytes.Length; offset++)
            {
                byte? expected = bytes[offset];
                if (expected.HasValue && buffer[start + offset] != expected.Value)
                {
                    matchesAtStart = false;
                    break;
                }
            }

            if (!matchesAtStart)
            {
                continue;
            }

            matches.Add(start);
            if (matches.Count >= maximumMatches)
            {
                break;
            }
        }

        return matches;
    }
}

internal static class NativeStableProcessPatternSearch
{
    private const int ChunkBytes = 1024 * 1024;

    internal static IReadOnlyList<nuint> FindAll(
        NativeStableProcessMemory memory,
        BytePattern pattern,
        int maximumMatches,
        CancellationToken cancellationToken = default) =>
        FindAll(
            memory,
            memory.EnumerateReadableRegions(),
            pattern,
            maximumMatches,
            cancellationToken);

    internal static IReadOnlyList<nuint> FindAll(
        INativeStableMemoryReader memory,
        IReadOnlyList<NativeStableMemoryRegion> regions,
        BytePattern pattern,
        int maximumMatches,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentNullException.ThrowIfNull(pattern);

        var matches = new List<nuint>();
        foreach (NativeStableMemoryRegion region in regions)
        {
            nuint offset = 0;
            byte[] overlap = [];

            while (offset < region.Size && matches.Count < maximumMatches)
            {
                cancellationToken.ThrowIfCancellationRequested();

                nuint remaining = region.Size - offset;
                int count = remaining > ChunkBytes
                    ? ChunkBytes
                    : checked((int)remaining);
                NativeStableMemoryReadResult read = memory.ReadBytes(
                    region.BaseAddress + offset,
                    count);

                if (read.BytesRead == 0)
                {
                    offset += checked((nuint)count);
                    overlap = [];
                    continue;
                }

                var searchable = new byte[overlap.Length + read.BytesRead];
                overlap.CopyTo(searchable, 0);
                read.Data.CopyTo(searchable, overlap.Length);

                int remainingMatchCapacity = maximumMatches - matches.Count;
                foreach (int localOffset in pattern.FindAll(searchable, remainingMatchCapacity))
                {
                    long adjustedOffset = checked((long)offset - overlap.Length + localOffset);
                    if (adjustedOffset >= 0)
                    {
                        matches.Add(region.BaseAddress + checked((nuint)adjustedOffset));
                    }
                }

                int overlapLength = Math.Min(pattern.Length - 1, searchable.Length);
                overlap = overlapLength == 0
                    ? []
                    : searchable[^overlapLength..];
                offset += checked((nuint)read.BytesRead);

                if (read.BytesRead < count)
                {
                    offset += checked((nuint)(count - read.BytesRead));
                    overlap = [];
                }
            }

            if (matches.Count >= maximumMatches)
            {
                break;
            }
        }

        return matches;
    }

    internal static IReadOnlyDictionary<string, IReadOnlyList<nuint>> FindAllMany(
        NativeStableProcessMemory memory,
        IReadOnlyList<NativeStableMemoryRegion> regions,
        IReadOnlyList<NativeStablePatternSearchRequest> requests,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentNullException.ThrowIfNull(requests);

        if (requests.Count == 0)
        {
            return new Dictionary<string, IReadOnlyList<nuint>>();
        }

        if (requests.Select(request => request.Key).Distinct(StringComparer.Ordinal).Count()
            != requests.Count)
        {
            throw new ArgumentException("Pattern request keys must be unique.", nameof(requests));
        }

        int maximumPatternLength = requests.Max(request => request.Pattern.Length);
        var matches = requests.ToDictionary(
            request => request.Key,
            _ => new List<nuint>(),
            StringComparer.Ordinal);

        foreach (NativeStableMemoryRegion region in regions)
        {
            nuint offset = 0;
            byte[] overlap = [];

            while (offset < region.Size)
            {
                cancellationToken.ThrowIfCancellationRequested();

                nuint remaining = region.Size - offset;
                int count = remaining > ChunkBytes
                    ? ChunkBytes
                    : checked((int)remaining);
                NativeStableMemoryReadResult read = memory.ReadBytes(
                    region.BaseAddress + offset,
                    count);

                if (read.BytesRead == 0)
                {
                    offset += checked((nuint)count);
                    overlap = [];
                    continue;
                }

                var searchable = new byte[overlap.Length + read.BytesRead];
                overlap.CopyTo(searchable, 0);
                read.Data.CopyTo(searchable, overlap.Length);

                IReadOnlyDictionary<string, IReadOnlyList<int>> localMatches =
                    FindAllInBuffer(searchable, requests);
                foreach (NativeStablePatternSearchRequest request in requests)
                {
                    foreach (int localOffset in localMatches[request.Key])
                    {
                        if (overlap.Length > 0 &&
                            localOffset + request.Pattern.Length <= overlap.Length)
                        {
                            continue;
                        }

                        long adjustedOffset = checked((long)offset - overlap.Length + localOffset);
                        if (adjustedOffset >= 0)
                        {
                            matches[request.Key].Add(
                                region.BaseAddress + checked((nuint)adjustedOffset));
                        }
                    }
                }

                int overlapLength = Math.Min(maximumPatternLength - 1, searchable.Length);
                overlap = overlapLength == 0
                    ? []
                    : searchable[^overlapLength..];
                offset += checked((nuint)read.BytesRead);

                if (read.BytesRead < count)
                {
                    offset += checked((nuint)(count - read.BytesRead));
                    overlap = [];
                }
            }
        }

        return matches.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<nuint>)pair.Value,
            StringComparer.Ordinal);
    }

    internal static IReadOnlyDictionary<string, IReadOnlyList<int>> FindAllInBuffer(
        ReadOnlySpan<byte> buffer,
        IReadOnlyList<NativeStablePatternSearchRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);
        var matches = new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal);
        foreach (NativeStablePatternSearchRequest request in requests)
        {
            matches.Add(request.Key, request.Pattern.FindAll(buffer));
        }

        return matches;
    }
}

internal sealed record NativeStablePatternSearchRequest(
    string Key,
    BytePattern Pattern);
