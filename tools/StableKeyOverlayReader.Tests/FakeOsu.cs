using BeatInsight.Diagnostics.NativeStable;
using BeatInsight.Integration.KeyOverlay;

namespace StableKeyOverlayReader.Tests;

/// <summary>Mémoire 32 bits simulée : deux régions (types, tas). Hors régions → lecture échouée.</summary>
internal sealed class FakeMemory : INativeStableMemoryReader
{
    public const uint TypesBase = 0x0800_0000;
    public const uint HeapBase = 0x2000_0000;
    public const uint HeapSize = 0x0010_0000;
    public const uint UnmappedRegionBase = 0x3000_0000; // lisible pour ReadBytes, absent de l'instantané initial

    private readonly byte[] types = new byte[0x1000];
    private readonly byte[] heap = new byte[HeapSize];
    private readonly byte[] late = new byte[0x1000];

    public bool Is32Bit { get; set; } = true;

    public long Reads;

    /// <summary>Appelé avant chaque lecture (n° de lecture) : permet de simuler GC, fermeture, blocage.</summary>
    public Action<long>? BeforeRead;

    /// <summary>Adresse dont la lecture est renvoyée incomplète.</summary>
    public uint? IncompleteAt;

    public bool Dead;

    public static IReadOnlyList<NativeStableMemoryRegion> InitialRegions =>
    [
        new(TypesBase, 0x1000, 0x1000, 0x04, 0x20000),
        new(HeapBase, HeapSize, 0x1000, 0x04, 0x20000),
    ];

    public static IReadOnlyList<NativeStableMemoryRegion> AllRegions =>
    [
        .. InitialRegions,
        new(UnmappedRegionBase, 0x1000, 0x1000, 0x04, 0x20000),
    ];

    public NativeStableMemoryReadResult ReadBytes(nuint address, int count)
    {
        long n = Interlocked.Increment(ref Reads);
        BeforeRead?.Invoke(n);
        if (Dead) return NativeStableMemoryReadResult.Failed;
        if (IncompleteAt is uint bad && address <= bad && bad < address + (nuint)count)
            return new(new byte[1], false, 1);
        if (!TryLocate(address, count, out byte[]? buffer, out int offset))
            return NativeStableMemoryReadResult.Failed;
        var data = new byte[count];
        Array.Copy(buffer!, offset, data, 0, count);
        return new(data, true, count);
    }

    public bool TryReadPointer(nuint address, out nuint value)
    {
        NativeStableMemoryReadResult r = ReadBytes(address, 4);
        value = r.Complete ? BitConverter.ToUInt32(r.Data, 0) : 0;
        return r.Complete && value != 0;
    }

    public void WriteU32(uint address, uint value) => Write(address, BitConverter.GetBytes(value));

    public void WriteI32(uint address, int value) => Write(address, BitConverter.GetBytes(value));

    public void WriteByte(uint address, byte value) => Write(address, [value]);

    public void Write(uint address, byte[] bytes)
    {
        if (!TryLocate(address, bytes.Length, out byte[]? buffer, out int offset))
            throw new ArgumentOutOfRangeException(nameof(address));
        Array.Copy(bytes, 0, buffer!, offset, bytes.Length);
    }

    private bool TryLocate(nuint address, int count, out byte[]? buffer, out int offset)
    {
        foreach ((uint b, byte[] buf) in new[] { (TypesBase, types), (HeapBase, heap), (UnmappedRegionBase, late) })
        {
            if (address >= b && address + (nuint)count <= b + (nuint)buf.Length)
            {
                buffer = buf;
                offset = checked((int)(address - b));
                return true;
            }
        }
        buffer = null;
        offset = 0;
        return false;
    }
}

internal sealed class FakeRegions : IKeyOverlayRegionSource
{
    public RegionIndex Current { get; set; } = new(FakeMemory.InitialRegions);

    public int Refreshes;

    public bool AllowRefresh = true;

    public bool TryRefresh()
    {
        if (!AllowRefresh) return false;
        Refreshes++;
        Current = new RegionIndex(FakeMemory.AllRegions);
        return true;
    }
}

internal sealed class FakeGame : IKeyOverlayGameSource
{
    public string? Identity = FakeOsu.Identity;
    public int? Status = 2;
    public int? PlayTime = 5_000;
    public string? Beatmap;

    public string? CurrentProcessIdentity() => Identity;

    public int? ReadStatus() => Status;

    public int? ReadPlayTime() => PlayTime;

    public string? ReadBeatmapIdentity() => Beatmap;
}

/// <summary>
/// Construit la chaîne validée en COM-KEY-02 dans la mémoire simulée :
/// container → ruleset (+0x64 gameplayBase → +0x38 scoreBase → +0x64 mode ; +0xAC keyOverlay)
/// → +0x10 List (+0x4 items, +0xC size) → tableau (+0x4 length, +0x8 éléments) → KeyCounter (+0x14 count, +0x1C pressed).
/// </summary>
internal sealed class FakeOsu
{
    public const string Identity = "4242:2026-10-14T16:06:54.0000000Z";
    public const uint Container = FakeMemory.HeapBase + 0x100;
    public const uint KeyOverlayType = FakeMemory.TypesBase + 0x10;
    public const uint ListType = FakeMemory.TypesBase + 0x20;
    public const uint ArrayType = FakeMemory.TypesBase + 0x30;
    public const uint KeyCounterType = FakeMemory.TypesBase + 0x40;
    public const uint OtherType = FakeMemory.TypesBase + 0x50;

    public readonly FakeMemory Memory = new();
    public readonly FakeRegions Regions = new();
    public readonly FakeGame Game = new();

    public uint Ruleset { get; private set; }
    public uint KeyOverlay { get; private set; }
    public uint List { get; private set; }
    public uint Items { get; private set; }
    public uint[] Elements { get; private set; } = new uint[4];

    public FakeOsu(uint baseAddress = FakeMemory.HeapBase + 0x1000, int mode = 0)
    {
        Build(baseAddress, mode, [0, 0, 0, 0], [false, false, false, false]);
    }

    public NativeStableKeyOverlayReader CreateReader() =>
        new(Identity, Container, Memory, Regions, Game);

    /// <summary>(Re)construit toute la structure à partir de <paramref name="baseAddress"/> (simule une compaction GC).</summary>
    public void Build(uint baseAddress, int mode, int[] counts, bool[] pressed)
    {
        Ruleset = baseAddress;
        uint gameplayBase = baseAddress + 0x200;
        uint scoreBase = baseAddress + 0x300;
        KeyOverlay = baseAddress + 0x400;
        List = baseAddress + 0x500;
        Items = baseAddress + 0x600;
        for (int i = 0; i < 4; i++) Elements[i] = baseAddress + 0x700 + (uint)(i * 0x40);

        Memory.WriteU32(Container + 0x4, Ruleset);
        Memory.WriteU32(Ruleset + 0x64, gameplayBase);
        Memory.WriteU32(gameplayBase + 0x38, scoreBase);
        Memory.WriteI32(scoreBase + 0x64, mode);
        Memory.WriteU32(Ruleset + 0xAC, KeyOverlay);
        Memory.WriteU32(KeyOverlay, KeyOverlayType);
        Memory.WriteU32(KeyOverlay + 0x10, List);
        Memory.WriteU32(List, ListType);
        Memory.WriteU32(List + 0x4, Items);
        Memory.WriteU32(List + 0x8, 0);
        Memory.WriteI32(List + 0xC, 4);
        Memory.WriteU32(Items, ArrayType);
        Memory.WriteI32(Items + 0x4, 4);
        for (int i = 0; i < 4; i++)
        {
            Memory.WriteU32(Items + 0x8 + (uint)(4 * i), Elements[i]);
            Memory.WriteU32(Elements[i], KeyCounterType);
            Memory.WriteI32(Elements[i] + 0x14, counts[i]);
            Memory.WriteByte(Elements[i] + 0x1C, pressed[i] ? (byte)1 : (byte)0);
        }
    }

    public void SetKey(int index, int count, bool pressed)
    {
        Memory.WriteI32(Elements[index] + 0x14, count);
        Memory.WriteByte(Elements[index] + 0x1C, pressed ? (byte)1 : (byte)0);
    }

    public int[] Counts() => Enumerable.Range(0, 4)
        .Select(i => BitConverter.ToInt32(Memory.ReadBytes(Elements[i] + 0x14, 4).Data, 0)).ToArray();

    public bool[] Pressed() => Enumerable.Range(0, 4)
        .Select(i => Memory.ReadBytes(Elements[i] + 0x1C, 1).Data[0] == 1).ToArray();

    /// <summary>Déplace toute la structure (mêmes valeurs) — compaction GC.</summary>
    public void Relocate(uint newBase)
    {
        int[] counts = Counts();
        bool[] pressed = Pressed();
        int mode = BitConverter.ToInt32(Memory.ReadBytes(Ruleset + 0x300 + 0x64, 4).Data, 0);
        Build(newBase, mode, counts, pressed);
    }
}
