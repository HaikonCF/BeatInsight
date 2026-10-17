using BeatInsight.Diagnostics.NativeStable;
using BeatInsight.Integration.KeyOverlay;
using Xunit;

namespace StableKeyOverlayReader.Tests;

/// <summary>V3.14-COM-PP-02 : sonde des mods de SongSelect (mémoire simulée, déterministe).</summary>
public class SongSelectModsProbeTests
{
    private const uint Code = FakeMemory.HeapBase + 0x80000;
    private const uint Global = FakeMemory.HeapBase + 0x90000;
    private const string Identity = "4242:2026-10-15T16:00:00.0000000Z";
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 16, 0, 0, TimeSpan.Zero);

    private static byte[] Instruction(uint global) =>
    [
        0xC8, 0xFF, 0x11, 0x22, 0x33, 0x44, 0x55, 0x81, 0x0D,
        .. BitConverter.GetBytes(global), 0x00, 0x08, 0x00, 0x00,
    ];

    private static (FakeMemory Memory, RegionIndex Regions) Setup(int mask, uint global = Global, int copies = 1)
    {
        var memory = new FakeMemory();
        for (int i = 0; i < copies; i++)
            memory.Write(Code + (uint)(0x100 * i), Instruction(global));
        if (global >= FakeMemory.HeapBase && global < FakeMemory.HeapBase + FakeMemory.HeapSize)
            memory.WriteI32(global, mask);
        return (memory, new RegionIndex(FakeMemory.InitialRegions));
    }

    private static SongSelectModsObservation Observe(FakeMemory memory, RegionIndex regions, int? status = 5)
    {
        SongSelectModsDiscovery discovery = SongSelectModsProbe.Discover(memory, FakeMemory.InitialRegions, regions);
        return SongSelectModsProbe.Read(memory, discovery, Identity, status, Now);
    }

    [Fact]
    public void Valid_mask_is_read_through_the_operand_and_normalized()
    {
        var (memory, regions) = Setup(8 | 16 | 64); // HD HR DT
        SongSelectModsObservation o = Observe(memory, regions);
        Assert.Equal(SongSelectModsValidity.Valid, o.Validity);
        Assert.Equal(88, o.RawModsMask);
        Assert.Equal(new[] { "HD", "HR", "DT" }, o.NormalizedMods);
        Assert.Equal(Identity, o.ProcessIdentity);
        Assert.NotNull(o.DiscoveryDuration);
    }

    [Fact]
    public void Nightcore_implies_double_time_and_perfect_implies_sudden_death()
    {
        Assert.Equal(new[] { "NC" }, SongSelectModsProbe.Normalize(512 | 64));
        Assert.Equal(new[] { "HD", "NC" }, SongSelectModsProbe.Normalize(8 | 512 | 64));
        Assert.Equal(new[] { "PF" }, SongSelectModsProbe.Normalize(16384 | 32));
    }

    [Fact]
    public void Zero_is_no_mod_only_with_a_valid_read_in_song_select()
    {
        var (memory, regions) = Setup(0);
        Assert.Equal(SongSelectModsValidity.Valid, Observe(memory, regions, status: 5).Validity);
        Assert.Empty(Observe(memory, regions, status: 5).NormalizedMods);
        Assert.Equal(SongSelectModsValidity.NotSongSelect, Observe(memory, regions, status: 2).Validity);
        var noRoute = Observe(memory, regions, status: null);
        Assert.Equal(SongSelectModsValidity.Unknown, noRoute.Validity);
        Assert.Equal(SongSelectModsFailure.RouteUnavailable, noRoute.FailureReason);
    }

    [Fact]
    public void Absent_signature_is_unknown()
    {
        var memory = new FakeMemory();
        SongSelectModsObservation o = Observe(memory, new RegionIndex(FakeMemory.InitialRegions));
        Assert.Equal(SongSelectModsValidity.Unknown, o.Validity);
        Assert.Equal(SongSelectModsFailure.SignatureAbsent, o.FailureReason);
        Assert.Null(o.RawModsMask);
    }

    [Fact]
    public void Ambiguous_signature_is_unknown()
    {
        var (memory, regions) = Setup(8, copies: 2);
        Assert.Equal(SongSelectModsFailure.SignatureAmbiguous, Observe(memory, regions).FailureReason);
    }

    [Theory]
    [InlineData(0x5000_0000u)] // hors régions
    [InlineData(Global + 2)]   // non aligné
    [InlineData(0u)]           // nul
    public void Invalid_global_pointer_is_unknown(uint global)
    {
        var (memory, regions) = Setup(8, global: global);
        Assert.Equal(SongSelectModsFailure.GlobalAddressInvalid, Observe(memory, regions).FailureReason);
    }

    [Fact]
    public void Unreadable_region_at_read_time_is_unknown()
    {
        var (memory, regions) = Setup(8);
        SongSelectModsDiscovery discovery = SongSelectModsProbe.Discover(memory, FakeMemory.InitialRegions, regions);
        memory.Dead = true;
        var o = SongSelectModsProbe.Read(memory, discovery, Identity, 5, Now);
        Assert.Equal(SongSelectModsFailure.ReadIncomplete, o.FailureReason);
        Assert.Null(o.RawModsMask);
    }

    [Fact]
    public void Unknown_bits_are_reported_and_never_valid()
    {
        var (memory, regions) = Setup(8 | (1 << 15)); // HD + bit 15 (mod de touches, hors table)
        SongSelectModsObservation o = Observe(memory, regions);
        Assert.Equal(SongSelectModsValidity.UnknownBits, o.Validity);
        Assert.Contains("?0x8000", o.NormalizedMods);
    }

    [Fact]
    public void Negative_mask_is_unknown()
    {
        var (memory, regions) = Setup(-1);
        Assert.Equal(SongSelectModsFailure.NegativeMask, Observe(memory, regions).FailureReason);
    }

    [Fact]
    public void Discovery_failure_then_recovery_after_code_appears()
    {
        var memory = new FakeMemory();
        var regions = new RegionIndex(FakeMemory.InitialRegions);
        Assert.Equal(SongSelectModsFailure.SignatureAbsent, Observe(memory, regions).FailureReason);
        memory.Write(Code, Instruction(Global)); // code JIT apparu plus tard
        memory.WriteI32(Global, 16);
        SongSelectModsObservation o = Observe(memory, regions);
        Assert.Equal(SongSelectModsValidity.Valid, o.Validity);
        Assert.Equal(new[] { "HR" }, o.NormalizedMods);
    }

    [Fact]
    public void Mask_changes_are_seen_without_a_new_discovery()
    {
        var (memory, regions) = Setup(0);
        SongSelectModsDiscovery discovery = SongSelectModsProbe.Discover(memory, FakeMemory.InitialRegions, regions);
        long readsBefore = memory.Reads;
        memory.WriteI32(Global, 8 | 64);
        var o = SongSelectModsProbe.Read(memory, discovery, Identity, 5, Now);
        Assert.Equal(new[] { "HD", "DT" }, o.NormalizedMods);
        Assert.Equal(1, memory.Reads - readsBefore); // une seule lecture après découverte
    }
}

/// <summary>V3.14-COM-PP-02 : suivi par identité de processus (découverte unique, reprise, redémarrage, péremption).</summary>
public class SongSelectModsTrackerTests
{
    private const uint Code = FakeMemory.HeapBase + 0x80000;
    private const uint Global = FakeMemory.HeapBase + 0x90000;

    private sealed class Target : ISongSelectModsTarget
    {
        public string? Identity = "1:A";
        public int? Status = 5;
        public FakeMemory Mem = new();
        public string? CurrentIdentity() => Identity;
        public INativeStableMemoryReader Memory => Mem;
        public IReadOnlyList<NativeStableMemoryRegion> SearchRegions() => FakeMemory.InitialRegions;
        public RegionIndex ReadableRegions() => new(FakeMemory.InitialRegions);
        public int? ReadStatus() => Status;

        public void Install(int mask)
        {
            Mem.Write(Code, [0xC8, 0xFF, 1, 2, 3, 4, 5, 0x81, 0x0D, .. BitConverter.GetBytes(Global), 0x00, 0x08, 0x00, 0x00]);
            Mem.WriteI32(Global, mask);
        }
    }

    private DateTimeOffset now = new(2026, 10, 15, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Discovery_happens_once_per_process_not_per_tick()
    {
        var t = new Target(); t.Install(8);
        var tracker = new SongSelectModsTracker(() => now);
        for (int i = 0; i < 50; i++) { tracker.Observe(t); now = now.AddMilliseconds(500); }
        Assert.Equal(1, tracker.Discoveries);
    }

    [Fact]
    public void Process_change_or_restart_rediscovers_and_never_reuses_the_old_address()
    {
        var t = new Target(); t.Install(8);
        var tracker = new SongSelectModsTracker(() => now);
        Assert.Equal(SongSelectModsValidity.Valid, tracker.Observe(t).Validity);
        t.Identity = null; // osu! fermé
        Assert.Equal(SongSelectModsFailure.ProcessIdentityChanged, tracker.Observe(t).FailureReason);
        t.Identity = "2:B"; t.Mem = new FakeMemory(); // redémarré, code pas encore JIT
        var o = tracker.Observe(t);
        Assert.Equal(SongSelectModsFailure.SignatureAbsent, o.FailureReason);
        Assert.Equal(2, tracker.Discoveries);
    }

    [Fact]
    public void Failed_discovery_retries_with_bounded_backoff_then_recovers()
    {
        var t = new Target();
        var tracker = new SongSelectModsTracker(() => now);
        tracker.Observe(t);                       // échec n°1
        now = now.AddMilliseconds(500);
        tracker.Observe(t);                       // en attente (1 s)
        Assert.Equal(1, tracker.Discoveries);
        t.Install(16);
        now = now.AddSeconds(1);
        var o = tracker.Observe(t);
        Assert.Equal(2, tracker.Discoveries);
        Assert.Equal(new[] { "HR" }, o.NormalizedMods);
    }

    [Fact]
    public void Route_transition_changes_validity_without_rediscovery()
    {
        var t = new Target(); t.Install(64);
        var tracker = new SongSelectModsTracker(() => now);
        Assert.Equal(SongSelectModsValidity.Valid, tracker.Observe(t).Validity);
        t.Status = 2; // gameplay
        Assert.Equal(SongSelectModsValidity.NotSongSelect, tracker.Observe(t).Validity);
        t.Status = 5;
        Assert.Equal(SongSelectModsValidity.Valid, tracker.Observe(t).Validity);
        Assert.Equal(1, tracker.Discoveries);
    }

    [Fact]
    public void Stale_or_foreign_observation_is_not_current()
    {
        var t = new Target(); t.Install(8);
        var tracker = new SongSelectModsTracker(() => now);
        var o = tracker.Observe(t);
        Assert.True(SongSelectModsTracker.IsCurrent(o, "1:A", now));
        Assert.False(SongSelectModsTracker.IsCurrent(o, "1:A", now.AddSeconds(2)));   // périmée
        Assert.False(SongSelectModsTracker.IsCurrent(o, "2:B", now));                 // autre processus
    }
}
