using BeatInsight.Diagnostics.NativeStable;
using BeatInsight.Integration.SongSelect;

namespace BeatInsight.Tests.Integration.SongSelect;

/// <summary>V3.14-COM-PP-03 : reader des mods de sélection (mémoire simulée, déterministe).</summary>
public sealed class NativeStableSongSelectModsReaderTests
{
    private const nuint CodeBase = 0x0100_0000;
    private const nuint DataBase = 0x0200_0000;
    private const nuint Code = CodeBase + 0x100;
    private const nuint Global = DataBase + 0x40;
    private const nuint Status = DataBase + 0x80;
    private const string IdentityA = "4242:2026-10-15T16:00:00.0000000Z";
    private const string IdentityB = "5151:2026-10-15T17:00:00.0000000Z";

    private DateTimeOffset now = new(2026, 10, 15, 16, 0, 0, TimeSpan.Zero);

    private static byte[] Instruction(uint global) =>
        [0xC8, 0xFF, 0x11, 0x22, 0x33, 0x44, 0x55, 0x81, 0x0D, .. BitConverter.GetBytes(global), 0x00, 0x08, 0x00, 0x00];

    private sealed class FakeProcess : ISongSelectModsProcess
    {
        public readonly Dictionary<nuint, byte[]> Regions = [];
        public readonly Dictionary<nuint, uint> Protect = [];
        public bool Alive = true;
        public bool Disposed;
        public long Reads;

        public FakeProcess()
        {
            Regions[CodeBase] = new byte[0x1000];
            Protect[CodeBase] = 0x20; // PAGE_EXECUTE_READ
            Regions[DataBase] = new byte[0x1000];
            Protect[DataBase] = 0x04; // PAGE_READWRITE
        }

        public bool IsAlive => Alive && !Disposed;

        public INativeStableMemoryReader Memory => new Reader(this);

        public IReadOnlyList<NativeStableMemoryRegion> EnumerateReadableRegions() =>
            Regions.Select(r => new NativeStableMemoryRegion(r.Key, (nuint)r.Value.Length, 0x1000, Protect[r.Key], 0x20000)).ToArray();

        public void Write(nuint address, byte[] data)
        {
            foreach ((nuint b, byte[] bytes) in Regions)
                if (address >= b && address + (nuint)data.Length <= b + (nuint)bytes.Length)
                {
                    data.CopyTo(bytes, (int)(address - b));
                    return;
                }
            throw new ArgumentOutOfRangeException(nameof(address));
        }

        public void WriteInt32(nuint address, int value) => Write(address, BitConverter.GetBytes(value));

        public void Dispose() => Disposed = true;

        private sealed class Reader(FakeProcess owner) : INativeStableMemoryReader
        {
            public bool Is32Bit => true;

            public NativeStableMemoryReadResult ReadBytes(nuint address, int count)
            {
                owner.Reads++;
                if (!owner.IsAlive) return NativeStableMemoryReadResult.Failed;
                foreach ((nuint b, byte[] bytes) in owner.Regions)
                    if (address >= b && address + (nuint)count <= b + (nuint)bytes.Length)
                        return new(bytes.AsSpan((int)(address - b), count).ToArray(), true, count);
                return NativeStableMemoryReadResult.Failed;
            }

            public bool TryReadPointer(nuint address, out nuint value)
            {
                NativeStableMemoryReadResult r = ReadBytes(address, 4);
                value = r.Complete ? BitConverter.ToUInt32(r.Data, 0) : 0;
                return r.Complete;
            }
        }
    }

    private sealed class FakeHost : ISongSelectModsHost
    {
        public string? Identity = IdentityA;
        public nuint? StatusAddr = Status;
        public Func<FakeProcess?> Factory;
        public readonly List<FakeProcess> Opened = [];
        public int Opens;

        public FakeHost(Func<FakeProcess?> factory) => Factory = factory;

        public string? CurrentStableIdentity() => Identity;

        public nuint? StatusAddress(string processIdentity) => processIdentity == Identity ? StatusAddr : null;

        public ISongSelectModsProcess? TryOpen(string processIdentity, out string reason)
        {
            Opens++;
            FakeProcess? p = Factory();
            reason = p is null ? "absent" : "ok";
            if (p is not null) Opened.Add(p);
            return p;
        }
    }

    private static FakeProcess Process(int mask, int status = 5, int copies = 1, uint global = (uint)Global, bool inData = false)
    {
        var p = new FakeProcess();
        for (int i = 0; i < copies; i++)
            p.Write((inData ? DataBase + 0x200 : Code) + (nuint)(0x40 * i), Instruction(global));
        p.WriteInt32(Global, mask);
        p.WriteInt32(Status, status);
        return p;
    }

    /// <summary>Découverte synchrone (déterministe) pour la plupart des tests.</summary>
    private NativeStableSongSelectModsReader Reader(FakeHost host) =>
        new(host, () => now, runInBackground: work => Task.FromResult(work()));

    [Fact]
    public void Valid_selection_is_read_and_normalized_with_the_calculator_key()
    {
        var host = new FakeHost(() => Process(8 | 16 | 64));
        using var reader = Reader(host);
        SongSelectModsSnapshot s = reader.Observe();

        Assert.Equal(SongSelectModsValidity.Valid, s.Validity);
        Assert.Equal(88, s.RawModsMask);
        Assert.Equal(new[] { "HD", "HR", "DT" }, s.NormalizedMods);
        Assert.Equal("DT+HD+HR", s.ModsKey);
        Assert.Equal(IdentityA, s.ProcessIdentity);
    }

    [Fact]
    public void Nightcore_is_normalized_and_zero_is_no_mod_only_in_confirmed_song_select()
    {
        Assert.Equal(new[] { "NC" }, StableModsMask.Normalize(512 | 64));
        Assert.Equal(new[] { "PF" }, StableModsMask.Normalize(16384 | 32));
        var host = new FakeHost(() => Process(0));
        using var reader = Reader(host);
        SongSelectModsSnapshot s = reader.Observe();
        Assert.Equal(SongSelectModsValidity.Valid, s.Validity);
        Assert.Equal("NM", s.ModsKey);

        host.Opened[0].WriteInt32(Status, 2); // gameplay : la variable garde les mods, mais ce n'est plus la sélection
        SongSelectModsSnapshot playing = reader.Observe();
        Assert.Equal(SongSelectModsValidity.NotSongSelect, playing.Validity);
        Assert.Null(playing.ModsKey);

        host.StatusAddr = null; // statut partagé pas encore découvert
        SongSelectModsSnapshot noRoute = reader.Observe();
        Assert.Equal(SongSelectModsValidity.Unknown, noRoute.Validity);
        Assert.Equal(SongSelectModsFailure.RouteUnavailable, noRoute.FailureReason);
    }

    [Fact]
    public void Unknown_bits_and_negative_masks_are_never_valid()
    {
        var host = new FakeHost(() => Process(8 | (1 << 15)));
        using var reader = Reader(host);
        Assert.Equal(SongSelectModsValidity.UnknownBits, reader.Observe().Validity);
        host.Opened[0].WriteInt32(Global, -1);
        Assert.Equal(SongSelectModsFailure.NegativeMask, reader.Observe().FailureReason);
    }

    [Fact]
    public void Signature_is_searched_only_in_executable_regions()
    {
        var host = new FakeHost(() => Process(8, inData: true));
        using var reader = Reader(host);
        Assert.Equal(SongSelectModsFailure.SignatureAbsent, reader.Observe().FailureReason);
        Assert.True(host.Opened[0].Disposed); // le handle d'une découverte ratée est fermé
    }

    [Fact]
    public void Ambiguous_signature_and_invalid_global_are_unknown()
    {
        var ambiguous = new FakeHost(() => Process(8, copies: 2));
        using (var reader = Reader(ambiguous))
            Assert.Equal(SongSelectModsFailure.SignatureAmbiguous, reader.Observe().FailureReason);

        var invalid = new FakeHost(() => Process(8, global: 0x5000_0000));
        using (var reader = Reader(invalid))
            Assert.Equal(SongSelectModsFailure.GlobalAddressInvalid, reader.Observe().FailureReason);

        var unaligned = new FakeHost(() => Process(8, global: (uint)Global + 2));
        using (var reader = Reader(unaligned))
            Assert.Equal(SongSelectModsFailure.GlobalAddressInvalid, reader.Observe().FailureReason);
    }

    [Fact]
    public void One_discovery_per_process_and_one_mask_read_per_observation()
    {
        var host = new FakeHost(() => Process(64));
        using var reader = Reader(host);
        reader.Observe();
        long reads = host.Opened[0].Reads;
        for (int i = 0; i < 50; i++)
        {
            reader.Observe();
            now = now.AddMilliseconds(100);
        }
        Assert.Equal(1, reader.Discoveries);
        Assert.Equal(1, host.Opens);
        Assert.Equal(100, host.Opened[0].Reads - reads); // statut + masque, rien d'autre
    }

    [Fact]
    public void Mask_changes_are_seen_without_rediscovery()
    {
        var host = new FakeHost(() => Process(0));
        using var reader = Reader(host);
        Assert.Equal("NM", reader.Observe().ModsKey);
        host.Opened[0].WriteInt32(Global, 8 | 64);
        Assert.Equal("DT+HD", reader.Observe().ModsKey);
        Assert.Equal(1, reader.Discoveries);
    }

    [Fact]
    public void Absent_jit_code_retries_with_bounded_backoff_then_recovers()
    {
        FakeProcess? current = null;
        bool jitReady = false;
        var host = new FakeHost(() => current = jitReady ? Process(16) : new FakeProcess());
        using var reader = Reader(host);

        Assert.Equal(SongSelectModsFailure.SignatureAbsent, reader.Observe().FailureReason); // échec n°1 → 1 s
        now = now.AddMilliseconds(500);
        reader.Observe();
        Assert.Equal(1, reader.Discoveries);
        now = now.AddMilliseconds(600);
        reader.Observe();                                                                      // échec n°2 → 2 s
        Assert.Equal(2, reader.Discoveries);
        now = now.AddSeconds(1.5);
        reader.Observe();
        Assert.Equal(2, reader.Discoveries);
        now = now.AddSeconds(1);
        reader.Observe();                                                                      // échec n°3 → 5 s
        Assert.Equal(3, reader.Discoveries);
        now = now.AddSeconds(4);
        reader.Observe();
        Assert.Equal(3, reader.Discoveries);

        jitReady = true;
        now = now.AddSeconds(1.1);
        SongSelectModsSnapshot s = reader.Observe();
        Assert.Equal(4, reader.Discoveries);
        Assert.Equal(SongSelectModsValidity.Valid, s.Validity);
        Assert.Equal("HR", s.ModsKey);
    }

    [Fact]
    public void Discovery_runs_in_background_never_blocks_and_only_one_is_active()
    {
        var gate = new TaskCompletionSource<NativeStableSongSelectModsReader.Attempt>();
        Func<NativeStableSongSelectModsReader.Attempt>? queued = null;
        var host = new FakeHost(() => Process(72));
        using var reader = new NativeStableSongSelectModsReader(host, () => now, runInBackground: work =>
        {
            queued = work;
            return gate.Task;
        });

        for (int i = 0; i < 20; i++)
        {
            SongSelectModsSnapshot pending = reader.Observe();
            Assert.Equal(SongSelectModsFailure.Discovering, pending.FailureReason);
            now = now.AddSeconds(1);
        }
        Assert.Equal(1, reader.Discoveries); // une seule découverte active, quelles que soient les observations

        gate.SetResult(queued!());
        SongSelectModsSnapshot s = reader.Observe();
        Assert.Equal(SongSelectModsValidity.Valid, s.Validity);
        Assert.Equal("DT+HD", s.ModsKey);
    }

    [Fact]
    public void Process_change_never_reuses_the_old_address_and_closes_the_old_handle()
    {
        var host = new FakeHost(() => Process(8));
        using var reader = Reader(host);
        Assert.Equal(SongSelectModsValidity.Valid, reader.Observe().Validity);
        FakeProcess first = host.Opened[0];

        host.Identity = null; // osu! fermé
        Assert.Equal(SongSelectModsFailure.SourceUnavailable, reader.Observe().FailureReason);
        Assert.True(first.Disposed);

        host.Identity = IdentityB;
        host.Factory = () => new FakeProcess(); // redémarré, code pas encore JIT
        SongSelectModsSnapshot s = reader.Observe();
        Assert.Equal(SongSelectModsFailure.SignatureAbsent, s.FailureReason);
        Assert.Equal(IdentityB, s.ProcessIdentity);
        Assert.Equal(2, reader.Discoveries);
    }

    [Fact]
    public void Pending_discovery_of_a_previous_process_is_rejected_and_closed()
    {
        var gate = new TaskCompletionSource<NativeStableSongSelectModsReader.Attempt>();
        Func<NativeStableSongSelectModsReader.Attempt>? queued = null;
        int calls = 0;
        var host = new FakeHost(() => Process(8));
        using var reader = new NativeStableSongSelectModsReader(host, () => now, runInBackground: work =>
        {
            calls++;
            if (calls == 1)
            {
                queued = work;
                return gate.Task;
            }
            return Task.FromResult(work());
        });

        reader.Observe();                 // découverte pour A, en cours
        host.Identity = IdentityB;        // redémarrage pendant la découverte
        gate.SetResult(queued!());        // termine pour A
        SongSelectModsSnapshot s = reader.Observe();

        Assert.True(host.Opened[0].Disposed);          // le handle de A n'est jamais utilisé
        Assert.Equal(IdentityB, s.ProcessIdentity);
        Assert.Equal(SongSelectModsValidity.Valid, s.Validity); // B découvert aussitôt, sans pénalité de reprise
        Assert.NotSame(host.Opened[0], host.Opened[1]);
    }

    [Fact]
    public void Process_exit_is_unknown_then_rediscovered()
    {
        var host = new FakeHost(() => Process(8));
        using var reader = Reader(host);
        reader.Observe();
        host.Opened[0].Alive = false;
        Assert.Equal(SongSelectModsFailure.ProcessExited, reader.Observe().FailureReason);
        Assert.True(host.Opened[0].Disposed);
    }

    [Fact]
    public void Dispose_closes_the_handle_and_stops_reading()
    {
        var host = new FakeHost(() => Process(8));
        var reader = Reader(host);
        reader.Observe();
        reader.Dispose();
        Assert.True(host.Opened[0].Disposed);
        Assert.Equal(SongSelectModsFailure.Disposed, reader.Observe().FailureReason);
    }
}
