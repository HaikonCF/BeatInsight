using BeatInsight.CommunityOverlays.Poc.Bridge;
using Xunit;

namespace BeatInsight.CommunityOverlays.Poc.Tests;

public class ByteRangeTests
{
    [Theory]
    [InlineData("bytes=0-9", 0, 9)]
    [InlineData("bytes=10-", 10, 99)]
    [InlineData("bytes=-10", 90, 99)]
    [InlineData("bytes=90-500", 90, 99)] // borne ramenée à la taille
    [InlineData("bytes=0-0", 0, 0)]
    public void Parses_single_ranges(string header, long start, long end)
    {
        var range = ByteRange.Parse(header, 100, out bool satisfiable);

        Assert.True(satisfiable);
        Assert.Equal(new ByteRange(start, end), range);
    }

    [Theory]
    [InlineData("bytes=100-")]
    [InlineData("bytes=500-600")]
    public void Start_beyond_file_is_not_satisfiable(string header)
    {
        Assert.Null(ByteRange.Parse(header, 100, out bool satisfiable));
        Assert.False(satisfiable);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("items=0-9")]
    [InlineData("bytes=0-9,20-29")]
    [InlineData("bytes=9-0")]
    [InlineData("bytes=abc")]
    public void Unusable_header_means_full_response(string? header)
    {
        Assert.Null(ByteRange.Parse(header, 100, out bool satisfiable));
        Assert.True(satisfiable);
    }
}
