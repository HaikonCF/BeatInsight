using System.IO;
using BeatInsight.CommunityOverlays.Poc.Managed;
using BeatInsight.Integration.CommunityOverlays.Managed;
using Xunit;

namespace BeatInsight.CommunityOverlays.Poc.Tests;

/// <summary>FEAT-003 : mode géré (<c>--managed-by</c>) sans effet sur le mode CLI autonome.</summary>
public class ManagedModeTests
{
    [Fact]
    public void Standalone_cli_is_not_managed()
    {
        var options = PocOptions.Parse(["--overlay", @"C:\overlays\KeyOverlay", "--source", "live-pipe", "--precise-pipe"]);

        Assert.False(options.IsManaged);
        Assert.Null(options.ManagedByPid);
        Assert.Equal("live-pipe", options.Source);
        Assert.True(options.PrecisePipe);
    }

    [Fact]
    public void Standalone_cli_keeps_the_original_zoom_by_default()
    {
        Assert.Equal(100, PocOptions.Parse(["--overlay", @"C:\overlays\KeyOverlay"]).ScalePercent);
    }

    [Theory]
    [InlineData("50", 50)]
    [InlineData("150", 150)]
    [InlineData("12", 25)]
    [InlineData("500", 200)]
    [InlineData("72", 70)]
    [InlineData("abc", 100)]
    public void Scale_argument_is_normalized(string value, int expected)
    {
        Assert.Equal(expected, PocOptions.Parse(["--scale", value]).ScalePercent);
    }

    [Fact]
    public void Managed_by_parses_a_positive_pid_only()
    {
        Assert.Equal(1234, PocOptions.Parse(["--managed-by", "1234"]).ManagedByPid);
        Assert.Null(PocOptions.Parse(["--managed-by", "-5"]).ManagedByPid);
        Assert.Null(PocOptions.Parse(["--managed-by", "abc"]).ManagedByPid);
        Assert.Null(PocOptions.Parse(["--managed-by"]).ManagedByPid);
    }

    [Fact]
    public void Managed_arguments_built_by_BeatInsight_are_understood()
    {
        var bounds = new CompanionBounds(100, 50.5, 640, 360);
        var options = PocOptions.Parse(["--overlay", @"C:\overlays\KeyOverlay", "--source", "live-pipe", "--precise-pipe", "--precise-v2",
            "--selection-pipe", "--window", CommunityOverlayCompanionProtocol.WindowArgument(bounds), "--managed-by", "4242"]);

        Assert.Equal(4242, options.ManagedByPid);
        Assert.True(options.PreciseV2);
        Assert.True(options.SelectionPipe);
        Assert.Equal(new WindowBounds(100, 50.5, 640, 360), options.WindowBounds);
    }

    [Fact]
    public void Second_managed_companion_for_the_same_overlay_is_refused()
    {
        string profile = CommunityOverlayWindow.ProfileNameFor(Path.Combine(Path.GetTempPath(), $"overlay-{Guid.NewGuid():N}"));
        using var acquired = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        ManagedCompanionHost? first = null;
        // Le détenteur reste vivant comme le thread UI du compagnon (sinon le mutex serait abandonné).
        var holder = new Thread(() =>
        {
            first = ManagedCompanionHost.TryAcquire(profile, TextWriter.Null);
            acquired.Set();
            release.Wait();
            first?.Dispose();
        });
        holder.Start();
        acquired.Wait();
        Assert.NotNull(first);

        var secondOutput = new StringWriter();
        ManagedCompanionHost? second = null;
        var contender = new Thread(() => second = ManagedCompanionHost.TryAcquire(profile, secondOutput));
        contender.Start();
        contender.Join();

        release.Set();
        holder.Join();

        Assert.Null(second);
        Assert.Equal(CompanionMessageKind.Duplicate,
            CommunityOverlayCompanionProtocol.Parse(secondOutput.ToString().Trim())!.Kind);

        // Libéré proprement : un nouveau compagnon peut reprendre l'overlay.
        ManagedCompanionHost? third = null;
        var next = new Thread(() =>
        {
            third = ManagedCompanionHost.TryAcquire(profile, TextWriter.Null);
            third?.Dispose();
        });
        next.Start();
        next.Join();
        Assert.NotNull(third);
    }

    [Fact]
    public void Different_overlays_do_not_block_each_other()
    {
        string a = CommunityOverlayWindow.ProfileNameFor(Path.Combine(Path.GetTempPath(), $"a-{Guid.NewGuid():N}"));
        string b = CommunityOverlayWindow.ProfileNameFor(Path.Combine(Path.GetTempPath(), $"b-{Guid.NewGuid():N}"));
        ManagedCompanionHost? first = null, second = null;
        var thread = new Thread(() =>
        {
            first = ManagedCompanionHost.TryAcquire(a, TextWriter.Null);
            second = ManagedCompanionHost.TryAcquire(b, TextWriter.Null);
            first?.Dispose();
            second?.Dispose();
        });
        thread.Start();
        thread.Join();

        Assert.NotNull(first);
        Assert.NotNull(second);
    }
}
