using System.Text.RegularExpressions;
using Xunit;

namespace BeatInsight.CommunityOverlays.Poc.Tests;

/// <summary>Profil WebView2 par overlay (V3.14-COM-03) : nom stable, distinct et valide.</summary>
public class OverlayProfileTests
{
    [Fact]
    public void Profile_name_is_stable_and_case_insensitive()
    {
        Assert.Equal(
            CommunityOverlayWindow.ProfileNameFor(@"C:\Tosu\static\KeyOverlay by cyperdark"),
            CommunityOverlayWindow.ProfileNameFor(@"c:\tosu\static\keyoverlay by cyperdark\"));
    }

    [Fact]
    public void Different_overlays_get_different_profiles()
    {
        Assert.NotEqual(
            CommunityOverlayWindow.ProfileNameFor(@"C:\Tosu\static\KeyOverlay by cyperdark"),
            CommunityOverlayWindow.ProfileNameFor(@"C:\Tosu\static\Simplistic by jassper0"));
    }

    [Theory]
    [InlineData(@"C:\Tosu\static\OBSDecoratePack - Background by Citrusis")]
    [InlineData(@"C:\Tosu\static\名前 by 作者")]
    public void Profile_name_uses_only_safe_characters(string folder)
    {
        string name = CommunityOverlayWindow.ProfileNameFor(folder);

        Assert.Matches(new Regex("^overlay_[0-9a-f]{16}$"), name);
    }

    /// <summary>COM-05 : les options expérimentales de limitation sont désactivées par défaut.</summary>
    [Fact]
    public void Frame_limiting_options_are_opt_in()
    {
        var defaults = PocOptions.Parse([]);
        Assert.Equal(0, defaults.FpsCap);
        Assert.False(defaults.ThrottleMainFrame);
        Assert.False(defaults.TraceFrames);

        var limited = PocOptions.Parse(["--fps-cap", "60", "--throttle-main-frame", "--trace-frames"]);
        Assert.Equal(60, limited.FpsCap);
        Assert.True(limited.ThrottleMainFrame);
        Assert.True(limited.TraceFrames);
    }

    [Fact]
    public void Frame_limiter_script_is_embedded_with_placeholder()
    {
        string script = BeatInsight.CommunityOverlays.Poc.Bridge.TosuCompatibleBridge.LoadEmbeddedScript(
            "BeatInsight.CommunityOverlays.Poc.frame-limiter.js");

        Assert.Contains("__BI_FPS_CAP__", script);
        Assert.Contains("requestAnimationFrame", script);
    }

    [Fact]
    public void Default_profile_mode_is_per_overlay()
    {
        Assert.Equal(OverlayProfileMode.PerOverlay, PocOptions.Parse([]).Profile);
        Assert.Equal(OverlayProfileMode.Shared, PocOptions.Parse(["--profile", "shared"]).Profile);
    }
}
