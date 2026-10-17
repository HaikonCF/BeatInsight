using BeatInsight.Diagnostics.NativeLazer;

namespace BeatInsight.Tests.Diagnostics;

public sealed class NativeLazerClrGameplayStatsMethodTableResolverTests
{
    [Fact]
    public void MetadataTokens_AreScopedToTheirDeclaringModules()
    {
        Assert.True(NativeLazerGameplayStatsModuleNames.IsGame("osu.Game.dll"));
        Assert.False(NativeLazerGameplayStatsModuleNames.IsGame("osu.Game.Rulesets.Osu.dll"));
        Assert.True(NativeLazerGameplayStatsModuleNames.IsOsuRuleset(
            "osu.Game.Rulesets.Osu.dll"));
        Assert.False(NativeLazerGameplayStatsModuleNames.IsOsuRuleset("osu.Game.dll"));
    }
}
