using Xunit;
using Xunit.Abstractions;
namespace BeatInsight.Tests.Analysis;
public sealed class TechPatternPhase8CompoundMotifAuditTests(ITestOutputHelper output){[Fact]public void RunCompoundMotifAudit(){var r=TechPatternPhase8CompoundMotifAudit.Run();output.WriteLine(r);Assert.Contains("PHASE 8",r);}}
