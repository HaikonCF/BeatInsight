using BeatInsight.CommunityOverlays.Poc.OverlayVisibility;
using Xunit;

namespace BeatInsight.CommunityOverlays.Poc.Tests;

/// <summary>O1 (V3.14-COM-06) : décisions de suspension et de reprise.</summary>
public class OverlayVisibilityPolicyTests
{
    private static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void Visible_overlay_is_never_suspended()
    {
        var policy = new OverlayVisibilityPolicy();

        for (int t = 0; t < 10_000; t += 250)
            Assert.False(policy.Update(Ms(t), OverlayHiddenReason.None));
        Assert.False(policy.IsSuspended);
    }

    [Theory]
    [InlineData(OverlayHiddenReason.Minimized)]
    [InlineData(OverlayHiddenReason.Disabled)]
    [InlineData(OverlayHiddenReason.ExclusiveFullscreen)]
    [InlineData(OverlayHiddenReason.Minimized | OverlayHiddenReason.ExclusiveFullscreen)]
    public void Hidden_overlay_is_suspended_after_the_delay(OverlayHiddenReason reason)
    {
        var policy = new OverlayVisibilityPolicy(Ms(1000));

        Assert.False(policy.Update(Ms(0), reason));
        Assert.False(policy.Update(Ms(750), reason));
        Assert.True(policy.Update(Ms(1000), reason));
        Assert.True(policy.IsSuspended);
        Assert.False(policy.Update(Ms(1250), reason)); // pas de transition répétée
    }

    [Fact]
    public void Short_hide_does_not_suspend_no_oscillation()
    {
        var policy = new OverlayVisibilityPolicy(Ms(1000));

        // Alt+Tab rapide : 0,5 s masqué, visible, 0,5 s masqué...
        for (int cycle = 0; cycle < 10; cycle++)
        {
            int t0 = cycle * 1000;
            Assert.False(policy.Update(Ms(t0), OverlayHiddenReason.ExclusiveFullscreen));
            Assert.False(policy.Update(Ms(t0 + 500), OverlayHiddenReason.ExclusiveFullscreen));
            Assert.False(policy.Update(Ms(t0 + 750), OverlayHiddenReason.None));
        }
        Assert.False(policy.IsSuspended);
    }

    [Fact]
    public void Resume_is_immediate_when_visible_again()
    {
        var policy = new OverlayVisibilityPolicy(Ms(1000));
        policy.Update(Ms(0), OverlayHiddenReason.Minimized);
        policy.Update(Ms(1000), OverlayHiddenReason.Minimized);

        Assert.True(policy.Update(Ms(1100), OverlayHiddenReason.None));
        Assert.False(policy.IsSuspended);
    }

    [Fact]
    public void New_suspension_requires_a_new_full_delay()
    {
        var policy = new OverlayVisibilityPolicy(Ms(1000));
        policy.Update(Ms(0), OverlayHiddenReason.Minimized);
        policy.Update(Ms(1000), OverlayHiddenReason.Minimized);
        policy.Update(Ms(1100), OverlayHiddenReason.None);

        Assert.False(policy.Update(Ms(1200), OverlayHiddenReason.Minimized));
        Assert.False(policy.Update(Ms(2100), OverlayHiddenReason.Minimized));
        Assert.True(policy.Update(Ms(2200), OverlayHiddenReason.Minimized));
    }

    [Fact]
    public void Options_enable_O1_and_O2_by_default()
    {
        var defaults = PocOptions.Parse([]);
        Assert.True(defaults.VisibilityManager);
        Assert.Equal(System.Windows.Threading.DispatcherPriority.Normal, defaults.PublishPriority);

        var baseline = PocOptions.Parse(["--visibility-manager", "off", "--publish-priority", "Background", "--minimize-at", "8000"]);
        Assert.False(baseline.VisibilityManager);
        Assert.Equal(System.Windows.Threading.DispatcherPriority.Background, baseline.PublishPriority);
        Assert.Equal(8000, baseline.ScriptedVisibilityEvents["minimize-at"]);
    }
}
