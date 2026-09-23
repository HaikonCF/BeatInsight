using Xunit;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

public sealed class TechV2Phase233LoadARegressionValidationTests(ITestOutputHelper output)
{
    [Fact]
    public void RunLoadARegressionValidation()
    {
        var results = TechV2Phase233LoadARegressionValidation.Run();
        output.WriteLine(TechV2Phase233LoadARegressionValidation.FormatReport(results));
        Assert.NotEmpty(results);
    }
}
