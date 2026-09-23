using Xunit;using Xunit.Abstractions;
namespace BeatInsight.Tests.Analysis;public sealed class TechPatternPhase9PrimaryPrecisionAuditTests(ITestOutputHelper o){[Fact]public void Run(){var r=TechPatternPhase9PrimaryPrecisionAudit.Run();o.WriteLine(r);Assert.Contains("PHASE 9",r);}}
