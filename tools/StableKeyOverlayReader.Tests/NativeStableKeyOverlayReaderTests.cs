using BeatInsight.Integration.KeyOverlay;
using Xunit;

namespace StableKeyOverlayReader.Tests;

public class NativeStableKeyOverlayReaderTests
{
    private static void AssertUnknown(KeyOverlaySample s, KeyOverlayReadFailureReason reason)
    {
        Assert.Equal(KeyOverlayReadStatus.Unknown, s.Status);
        Assert.Equal(reason, s.Reason);
        Assert.Null(s.Keys);
    }

    // ---------------------------------------------------------------- structure

    [Fact]
    public void Valid_chain_returns_four_real_keys()
    {
        var osu = new FakeOsu();
        osu.SetKey(0, 10, true);
        osu.SetKey(1, 4, false);
        osu.SetKey(2, 1, false);
        osu.SetKey(3, 2, true);
        using var reader = osu.CreateReader();

        KeyOverlaySample s = reader.Read();

        Assert.True(s.IsValid);
        Assert.Equal(KeyOverlayReadFailureReason.None, s.Reason);
        Assert.Equal(new KeyCounterState(10, true), s.Keys!.K1);
        Assert.Equal(new KeyCounterState(4, false), s.Keys.K2);
        Assert.Equal(new KeyCounterState(1, false), s.Keys.M1);
        Assert.Equal(new KeyCounterState(2, true), s.Keys.M2);
        Assert.Equal(FakeOsu.Identity, s.ProcessIdentity);
        Assert.NotNull(s.PlayIdentity);
        Assert.Equal(5_000, s.PlayTimeMs);
    }

    [Fact]
    public void Null_key_overlay_pointer_is_unknown()
    {
        var osu = new FakeOsu();
        osu.Memory.WriteU32(osu.Ruleset + 0xAC, 0);
        using var reader = osu.CreateReader();
        AssertUnknown(reader.Read(), KeyOverlayReadFailureReason.KeyOverlayPointerNull);
    }

    [Fact]
    public void Null_ruleset_is_unknown()
    {
        var osu = new FakeOsu();
        osu.Memory.WriteU32(FakeOsu.Container + 0x4, 0);
        using var reader = osu.CreateReader();
        AssertUnknown(reader.Read(), KeyOverlayReadFailureReason.RulesetNull);
    }

    [Fact]
    public void Unreadable_or_unaligned_pointer_is_unknown()
    {
        var osu = new FakeOsu();
        osu.Regions.AllowRefresh = false;
        osu.Memory.WriteU32(osu.Ruleset + 0xAC, 0x5000_0000); // hors régions
        using var reader = osu.CreateReader();
        AssertUnknown(reader.Read(), KeyOverlayReadFailureReason.KeyOverlayUnreadable);

        osu.Memory.WriteU32(osu.Ruleset + 0xAC, osu.KeyOverlay + 2); // non aligné
        AssertUnknown(reader.Read(), KeyOverlayReadFailureReason.KeyOverlayUnreadable);
    }

    [Fact]
    public void Invalid_object_types_are_unknown()
    {
        var osu = new FakeOsu();
        osu.Regions.AllowRefresh = false;
        osu.Memory.WriteU32(osu.KeyOverlay, 0x1234); // type hors régions
        using var reader = osu.CreateReader();
        AssertUnknown(reader.Read(), KeyOverlayReadFailureReason.KeyOverlayTypeInvalid);
    }

    [Theory]
    [InlineData("length5", KeyOverlayReadFailureReason.ItemsLengthInvalid)]
    [InlineData("length3", KeyOverlayReadFailureReason.ItemsLengthInvalid)]
    [InlineData("size3", KeyOverlayReadFailureReason.ListSizeInvalid)]
    [InlineData("duplicate", KeyOverlayReadFailureReason.ElementsNotDistinct)]
    [InlineData("typeMismatch", KeyOverlayReadFailureReason.ElementTypeMismatch)]
    public void Inconsistent_array_is_unknown(string corruption, KeyOverlayReadFailureReason expected)
    {
        var osu = new FakeOsu();
        switch (corruption)
        {
            case "length5": osu.Memory.WriteI32(osu.Items + 0x4, 5); break;
            case "length3": osu.Memory.WriteI32(osu.Items + 0x4, 3); osu.Memory.WriteI32(osu.List + 0xC, 3); break;
            case "size3": osu.Memory.WriteI32(osu.List + 0xC, 3); break;
            case "duplicate": osu.Memory.WriteU32(osu.Items + 0x8 + 4, osu.Elements[0]); break;
            case "typeMismatch": osu.Memory.WriteU32(osu.Elements[3], FakeOsu.OtherType); break;
        }
        using var reader = osu.CreateReader();
        AssertUnknown(reader.Read(), expected);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1_000_001)]
    [InlineData(int.MinValue)]
    public void Negative_or_aberrant_count_is_unknown(int count)
    {
        var osu = new FakeOsu();
        osu.Memory.WriteI32(osu.Elements[2] + 0x14, count);
        using var reader = osu.CreateReader();
        AssertUnknown(reader.Read(), KeyOverlayReadFailureReason.CountOutOfRange);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(255)]
    public void Non_boolean_pressed_is_unknown(byte value)
    {
        var osu = new FakeOsu();
        osu.Memory.WriteByte(osu.Elements[1] + 0x1C, value);
        using var reader = osu.CreateReader();
        AssertUnknown(reader.Read(), KeyOverlayReadFailureReason.PressedNotBoolean);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Non_standard_mode_is_unknown(int mode)
    {
        var osu = new FakeOsu(mode: mode);
        using var reader = osu.CreateReader();
        AssertUnknown(reader.Read(), KeyOverlayReadFailureReason.ModeUnsupported);
    }

    [Fact]
    public void Partial_read_is_unknown_without_partial_data()
    {
        var osu = new FakeOsu();
        osu.SetKey(0, 3, false);
        osu.Memory.IncompleteAt = osu.Elements[2] + 0x14;
        using var reader = osu.CreateReader();
        AssertUnknown(reader.Read(), KeyOverlayReadFailureReason.ReadIncomplete);
    }

    [Fact]
    public void Region_snapshot_is_refreshed_once_when_object_lies_in_new_region()
    {
        var osu = new FakeOsu();
        // Un KeyCounter alloué dans une région absente de l'instantané initial.
        uint moved = FakeMemory.UnmappedRegionBase + 0x100;
        osu.Memory.WriteU32(moved, FakeOsu.KeyCounterType);
        osu.Memory.WriteI32(moved + 0x14, 7);
        osu.Memory.WriteU32(osu.Items + 0x8 + 12, moved);
        using var reader = osu.CreateReader();

        KeyOverlaySample s = reader.Read();

        Assert.True(s.IsValid);
        Assert.Equal(7, s.Keys!.M2.Count);
        Assert.Equal(1, osu.Regions.Refreshes);
    }

    [Fact]
    public void Unsupported_architecture_is_unknown()
    {
        var osu = new FakeOsu();
        osu.Memory.Is32Bit = false;
        using var reader = osu.CreateReader();
        AssertUnknown(reader.Read(), KeyOverlayReadFailureReason.UnsupportedProcessArchitecture);
    }

    // ---------------------------------------------------------------- processus

    [Fact]
    public void Changed_process_identity_is_unknown()
    {
        var osu = new FakeOsu();
        using var reader = osu.CreateReader();
        Assert.True(reader.Read().IsValid);

        osu.Game.Identity = "9999:2026-10-14T17:00:00.0000000Z";
        AssertUnknown(reader.Read(), KeyOverlayReadFailureReason.ProcessIdentityMismatch);
    }

    [Fact]
    public void Exited_process_is_unknown()
    {
        var osu = new FakeOsu();
        using var reader = osu.CreateReader();
        osu.Game.Identity = null;
        AssertUnknown(reader.Read(), KeyOverlayReadFailureReason.ProcessExited);
    }

    // ---------------------------------------------------------------- GC

    [Fact]
    public void Ruleset_relocated_by_gc_between_samples_keeps_the_same_play()
    {
        var osu = new FakeOsu();
        osu.SetKey(0, 5, false);
        using var reader = osu.CreateReader();
        KeyOverlaySample before = reader.Read();

        osu.Relocate(FakeMemory.HeapBase + 0x8000);
        osu.SetKey(0, 6, true);
        osu.Game.PlayTime += 10;
        KeyOverlaySample after = reader.Read();

        Assert.True(after.IsValid);
        Assert.Equal(before.PlayIdentity, after.PlayIdentity);
        Assert.NotEqual(before.DiagnosticRulesetAddress, after.DiagnosticRulesetAddress);
        Assert.Equal(6, after.Keys!.K1.Count);
    }

    [Fact]
    public void Gc_relocation_during_a_read_rejects_the_whole_sample()
    {
        var osu = new FakeOsu();
        osu.SetKey(0, 5, false);
        using var reader = osu.CreateReader();
        long start = osu.Memory.Reads;
        bool moved = false;
        // Déplacement au milieu de la lecture des éléments (avant la garde finale).
        osu.Memory.BeforeRead = n =>
        {
            if (!moved && n == start + 9)
            {
                moved = true;
                osu.Memory.BeforeRead = null;
                osu.Relocate(FakeMemory.HeapBase + 0x9000);
            }
        };

        KeyOverlaySample s = reader.Read();

        Assert.True(moved);
        AssertUnknown(s, KeyOverlayReadFailureReason.StructureChangedDuringRead);
    }

    // ---------------------------------------------------------------- identité de partie

    [Fact]
    public void Retry_starts_a_new_play()
    {
        var osu = new FakeOsu();
        osu.SetKey(0, 30, false);
        using var reader = osu.CreateReader();
        KeyOverlaySample first = reader.Read();

        // Retry : KeyCounter momentanément absent, temps revenu à 0, nouveau KeyOverlay à zéro.
        osu.Memory.WriteU32(osu.Ruleset + 0xAC, 0);
        osu.Game.PlayTime = 5_200;
        AssertUnknown(reader.Read(), KeyOverlayReadFailureReason.KeyOverlayPointerNull);
        osu.Game.PlayTime = 0;
        osu.Build(FakeMemory.HeapBase + 0x4000, 0, [0, 0, 0, 0], [false, false, false, false]);
        KeyOverlaySample retried = reader.Read();

        Assert.True(retried.IsValid);
        Assert.NotEqual(first.PlayIdentity, retried.PlayIdentity);
        Assert.Equal(0, retried.Keys!.K1.Count);
    }

    [Fact]
    public void Pause_keeps_the_play_and_reports_pressed_state_without_counting()
    {
        var osu = new FakeOsu();
        osu.SetKey(0, 12, false);
        using var reader = osu.CreateReader();
        KeyOverlaySample running = reader.Read();

        // Pause : temps figé, touche physique enfoncée, compteur inchangé.
        osu.SetKey(0, 12, true);
        KeyOverlaySample paused = reader.Read();
        osu.SetKey(0, 12, false);
        KeyOverlaySample paused2 = reader.Read();

        Assert.True(paused.IsValid);
        Assert.True(paused2.IsValid);
        Assert.Equal(running.PlayIdentity, paused.PlayIdentity);
        Assert.True(paused.Keys!.K1.IsPressed);
        Assert.Equal(12, paused2.Keys!.K1.Count);
    }

    [Fact]
    public void Song_select_is_unknown_and_next_gameplay_is_a_new_play()
    {
        var osu = new FakeOsu();
        using var reader = osu.CreateReader();
        KeyOverlaySample play1 = reader.Read();

        osu.Game.Status = 5;
        long readsBefore = osu.Memory.Reads;
        KeyOverlaySample menu = reader.Read();
        AssertUnknown(menu, KeyOverlayReadFailureReason.RouteNotGameplay);
        Assert.Null(menu.PlayIdentity);
        Assert.Equal(readsBefore, osu.Memory.Reads); // aucune lecture de chaîne hors gameplay

        osu.Game.Status = 2;
        osu.Game.PlayTime = 9_000; // même sans recul du temps
        KeyOverlaySample play2 = reader.Read();
        Assert.True(play2.IsValid);
        Assert.NotEqual(play1.PlayIdentity, play2.PlayIdentity);
    }

    [Fact]
    public void Late_beatmap_identity_is_adopted_without_a_fake_new_play()
    {
        // COM-KEY-04 : l'identité de beatmap de GameStateService peut arriver un rafraîchissement
        // après l'entrée en gameplay. Elle est adoptée, sans couper la partie en deux.
        var osu = new FakeOsu();
        osu.Game.Beatmap = "map:1";
        using var reader = osu.CreateReader();
        KeyOverlaySample a = reader.Read();
        osu.Game.Beatmap = "map:2";
        osu.Game.PlayTime += 100;
        KeyOverlaySample b = reader.Read();

        Assert.True(b.IsValid);
        Assert.Equal(a.PlayIdentity, b.PlayIdentity);
        Assert.Equal("map:2", b.BeatmapIdentity);
    }

    [Fact]
    public void New_beatmap_after_song_select_is_a_new_play_with_its_identity()
    {
        var osu = new FakeOsu();
        osu.Game.Beatmap = "map:1";
        using var reader = osu.CreateReader();
        KeyOverlaySample a = reader.Read();
        osu.Game.Status = 5;
        AssertUnknown(reader.Read(), KeyOverlayReadFailureReason.RouteNotGameplay);
        osu.Game.Status = 2;
        osu.Game.Beatmap = "map:2";
        KeyOverlaySample b = reader.Read();

        Assert.True(b.IsValid);
        Assert.NotEqual(a.PlayIdentity, b.PlayIdentity);
        Assert.Equal("map:1", a.BeatmapIdentity);
        Assert.Equal("map:2", b.BeatmapIdentity);
    }

    [Fact]
    public void Momentary_disappearance_is_not_a_reset()
    {
        var osu = new FakeOsu();
        osu.SetKey(1, 20, false);
        using var reader = osu.CreateReader();
        KeyOverlaySample a = reader.Read();

        osu.Memory.WriteU32(osu.Ruleset + 0xAC, 0);
        osu.Game.PlayTime += 10;
        AssertUnknown(reader.Read(), KeyOverlayReadFailureReason.KeyOverlayPointerNull);

        osu.Memory.WriteU32(osu.Ruleset + 0xAC, osu.KeyOverlay);
        osu.SetKey(1, 21, false);
        osu.Game.PlayTime += 10;
        KeyOverlaySample b = reader.Read();

        Assert.True(b.IsValid);
        Assert.Equal(a.PlayIdentity, b.PlayIdentity);
        Assert.Equal(21, b.Keys!.K2.Count);
    }

    [Fact]
    public void Counter_regression_without_new_play_signal_stays_unknown_until_coherent()
    {
        var osu = new FakeOsu();
        osu.SetKey(0, 40, false);
        using var reader = osu.CreateReader();
        Assert.True(reader.Read().IsValid);

        osu.SetKey(0, 3, false); // baisse sans recul du temps ni changement d'état
        osu.Game.PlayTime += 10;
        AssertUnknown(reader.Read(), KeyOverlayReadFailureReason.CounterRegression);
        osu.SetKey(0, 50, false);
        osu.Game.PlayTime += 10;
        AssertUnknown(reader.Read(), KeyOverlayReadFailureReason.PlayIdentityAmbiguous);

        // Signal certain : retry (recul du temps).
        osu.Game.PlayTime = 0;
        osu.SetKey(0, 0, false);
        Assert.True(reader.Read().IsValid);
    }

    [Fact]
    public void Fresh_zeroed_key_overlay_before_any_increment_rebaselines()
    {
        var osu = new FakeOsu();
        osu.SetKey(0, 30, false); // ancien KeyOverlay encore visible au premier échantillon de la partie
        osu.Game.PlayTime = 0;
        using var reader = osu.CreateReader();
        Assert.True(reader.Read().IsValid);

        osu.Build(FakeMemory.HeapBase + 0x5000, 0, [0, 0, 0, 0], [false, false, false, false]);
        osu.Game.PlayTime = 20;
        KeyOverlaySample s = reader.Read();
        Assert.True(s.IsValid);
        Assert.Equal(0, s.Keys!.K1.Count);
    }

    [Fact]
    public void Restarted_osu_gets_a_new_identity_and_play()
    {
        var osu = new FakeOsu();
        using var reader = osu.CreateReader();
        Assert.True(reader.Read().IsValid);
        osu.Game.Identity = null; // fermeture
        AssertUnknown(reader.Read(), KeyOverlayReadFailureReason.ProcessExited);
        osu.Game.Identity = "5000:2026-10-14T18:00:00.0000000Z"; // redémarrage : identité différente
        AssertUnknown(reader.Read(), KeyOverlayReadFailureReason.ProcessIdentityMismatch);
    }

    // ---------------------------------------------------------------- fermeture

    [Fact]
    public async Task Dispose_during_a_read_waits_and_no_native_access_follows()
    {
        var osu = new FakeOsu();
        var reader = osu.CreateReader();
        using var inRead = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        osu.Memory.BeforeRead = _ =>
        {
            osu.Memory.BeforeRead = null;
            inRead.Set();
            release.Wait(TimeSpan.FromSeconds(5));
        };

        KeyOverlaySample? first = null;
        var t = new Thread(() => first = reader.Read());
        t.Start();
        Assert.True(inRead.Wait(TimeSpan.FromSeconds(5)));

        var disposer = Task.Run(reader.Dispose);
        Assert.NotSame(disposer, await Task.WhenAny(disposer, Task.Delay(100))); // Dispose attend la lecture en cours
        release.Set();
        await disposer.WaitAsync(TimeSpan.FromSeconds(5));
        t.Join();

        long readsAfterDispose = osu.Memory.Reads;
        AssertUnknown(reader.Read(), KeyOverlayReadFailureReason.Disposed);
        Assert.Equal(readsAfterDispose, osu.Memory.Reads);
        Assert.NotNull(first);
    }

    [Fact]
    public void Process_closing_during_a_read_yields_unknown()
    {
        var osu = new FakeOsu();
        using var reader = osu.CreateReader();
        long start = osu.Memory.Reads;
        osu.Memory.BeforeRead = n => { if (n == start + 6) osu.Memory.Dead = true; };

        KeyOverlaySample s = reader.Read();

        Assert.Equal(KeyOverlayReadStatus.Unknown, s.Status);
        Assert.Null(s.Keys);
        Assert.Contains(s.Reason, new[]
        {
            KeyOverlayReadFailureReason.ReadIncomplete,
            KeyOverlayReadFailureReason.StructureChangedDuringRead,
            KeyOverlayReadFailureReason.RulesetNull,
        });
    }
}
