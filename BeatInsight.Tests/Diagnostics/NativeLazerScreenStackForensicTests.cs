using BeatInsight.Diagnostics.NativeLazer;
using System.Buffers.Binary;

namespace BeatInsight.Tests.Diagnostics;

public sealed class NativeLazerScreenStackForensicTests
{
    private static readonly DateTime start =
        new(2026, 10, 6, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ValidStackReturnsBoundedEntriesAndTopDiscriminator()
    {
        Fixture fixture = new(count: 3);

        NativeLazerScreenStackSample sample = fixture.Read();

        Assert.Equal(NativeLazerScreenStackOutcome.Unique, sample.Outcome);
        Assert.Equal(Fixture.Root, sample.RootAddress);
        Assert.Equal(Fixture.ScreenStack, sample.ScreenStackAddress);
        Assert.Equal(3, sample.Count);
        Assert.Equal(3, sample.Entries.Count);
        Assert.Equal([0, 1, 2], sample.Entries.Select(entry => entry.Index));
        Assert.Single(sample.Entries, entry => entry.IsTop);
        Assert.Equal(Fixture.Objects[2], sample.Entries[^1].ObjectAddress);
        Assert.Equal(Fixture.MethodTables[2], sample.Entries[^1].MethodTable);
        Assert.True(sample.Metrics.BytesRead > 0);
        Assert.True(sample.Metrics.ReadOperations > 0);
    }

    [Fact]
    public void LargeStackCapturesOnlyLastEightEntries()
    {
        Fixture fixture = new(count: 10);

        NativeLazerScreenStackSample sample = fixture.Read();

        Assert.Equal(NativeLazerScreenStackOutcome.Unique, sample.Outcome);
        Assert.Equal(10, sample.Count);
        Assert.Equal(8, sample.Entries.Count);
        Assert.Equal(2, sample.Entries[0].Index);
        Assert.Equal(9, sample.Entries[^1].Index);
        Assert.True(sample.Entries[^1].IsTop);
    }

    [Fact]
    public void DifferentGenerationFailsBeforeReadingMemory()
    {
        Fixture fixture = new(count: 1);
        NativeLazerGenerationIdentity other = new(42, start.AddSeconds(1));

        NativeLazerScreenStackSample sample = fixture.Reader.Read(
            fixture.Generation,
            other,
            Fixture.Root,
            fixture.Layout,
            fixture.Memory,
            _ => true);

        Assert.Equal(NativeLazerScreenStackOutcome.Unknown, sample.Outcome);
        Assert.Empty(sample.Entries);
        Assert.Null(sample.RootAddress);
        Assert.Equal(0, sample.Metrics.BytesRead);
        Assert.Equal(0, sample.Metrics.ReadOperations);
    }

    [Fact]
    public void IncoherentStackCountFailsClosed()
    {
        Fixture fixture = new(count: 1);
        fixture.Memory.SetInt32(
            Fixture.Stack + fixture.Layout.StackSize,
            NativeLazerScreenStackReader.MaximumStackCount + 1);

        NativeLazerScreenStackSample sample = fixture.Read();

        Assert.Equal(NativeLazerScreenStackOutcome.Unknown, sample.Outcome);
        Assert.Empty(sample.Entries);
        Assert.Null(sample.Count);
        Assert.Contains("incoherent", sample.FailureReason);
    }

    [Fact]
    public void StackMutationDuringReadFailsClosedWithoutReturningEntries()
    {
        Fixture fixture = new(count: 1);
        ulong slot = Fixture.Array + fixture.Layout.ArrayData;
        int reads = 0;
        fixture.Memory.BeforeRead = (address, _) =>
        {
            if (address == slot && ++reads == 2)
                fixture.Memory.SetPointer(slot, Fixture.ReplacementObject);
        };

        NativeLazerScreenStackSample sample = fixture.Read();

        Assert.Equal(NativeLazerScreenStackOutcome.Unknown, sample.Outcome);
        Assert.Empty(sample.Entries);
        Assert.Contains("changed", sample.FailureReason);
    }

    [Fact]
    public void GenerationChangeDuringReadFailsClosed()
    {
        Fixture fixture = new(count: 1);
        int validationCount = 0;

        NativeLazerScreenStackSample sample = fixture.Reader.Read(
            fixture.Generation,
            fixture.Generation,
            Fixture.Root,
            fixture.Layout,
            fixture.Memory,
            _ => ++validationCount == 1);

        Assert.Equal(NativeLazerScreenStackOutcome.Unknown, sample.Outcome);
        Assert.Empty(sample.Entries);
        Assert.Contains("generation changed", sample.FailureReason);
    }

    [Theory]
    [InlineData("SongSelect")]
    [InlineData("Gameplay")]
    [InlineData("Results")]
    public void KnownTopMethodTableResolvesExpectedRoute(string expectedName)
    {
        Fixture fixture = new(count: 3);
        NativeLazerGameRoute expected = Enum.Parse<NativeLazerGameRoute>(expectedName);

        NativeLazerGameRouteResult result = fixture.ReadRoute(
            fixture.TablesFor(expected));

        Assert.Equal(expected, result.Route);
        Assert.Null(result.FailureReason);
        Assert.True(result.Metrics.ReadOperations > 0);
    }

    [Fact]
    public void SongSelectIsClassifiedWhenOnlyItsMethodTableIsConstructed()
    {
        Fixture fixture = new(count: 3);
        var partial = new NativeLazerGameRouteMethodTables(
            fixture.Generation,
            Fixture.MethodTables[2],
            null,
            null);

        NativeLazerGameRouteResult result = fixture.ReadRoute(partial);

        Assert.True(partial.IsCoherent);
        Assert.True(partial.HasMissing);
        Assert.Equal(NativeLazerGameRoute.SongSelect, result.Route);
    }

    [Fact]
    public void LaterPlayerAndResultsMethodTablesAreMergedAndClassified()
    {
        Fixture fixture = new(count: 3);
        var state = new NativeLazerGameRouteMethodTableState();
        state.Initialize(Resolution(
            fixture.Generation,
            songSelect: 0xE1000,
            player: null,
            results: null));

        ulong playerTop = Fixture.MethodTables[2];
        Assert.True(state.TryEnrich(
            fixture.Generation,
            playerTop,
            () => Resolution(fixture.Generation, null, playerTop, null)));
        Assert.Equal(
            NativeLazerGameRoute.Gameplay,
            fixture.ReadRoute(state.Current!.MethodTables!).Route);

        ulong resultsTop = 0xB0000;
        fixture.SetTopMethodTable(resultsTop);
        Assert.True(state.TryEnrich(
            fixture.Generation,
            resultsTop,
            () => Resolution(fixture.Generation, null, null, resultsTop)));
        Assert.Equal(
            NativeLazerGameRoute.Results,
            fixture.ReadRoute(state.Current!.MethodTables!).Route);
        Assert.Equal(3, state.Current.InvocationCount);
    }

    [Fact]
    public void KnownEntryDoesNotRequestAnotherResolution()
    {
        Fixture fixture = new(count: 3);
        ulong top = Fixture.MethodTables[2];
        var state = new NativeLazerGameRouteMethodTableState();
        state.Initialize(Resolution(fixture.Generation, top, null, null));
        int resolutions = 0;

        bool enriched = state.TryEnrich(
            fixture.Generation,
            top,
            () =>
            {
                resolutions++;
                return Resolution(fixture.Generation, top, null, null);
            });

        Assert.False(enriched);
        Assert.Equal(0, resolutions);
        Assert.Equal(1, state.Current!.InvocationCount);
    }

    [Fact]
    public void UnknownTopWithoutNewEntryGetsOneBoundedRetryThenStaysUnknown()
    {
        Fixture fixture = new(count: 3);
        var state = new NativeLazerGameRouteMethodTableState();
        state.Initialize(Resolution(fixture.Generation, 0xE1000, null, null));
        int resolutions = 0;
        NativeLazerGameRouteResolution NoNewEntry()
        {
            resolutions++;
            return Resolution(fixture.Generation, 0xE1000, null, null);
        }

        ulong top = Fixture.MethodTables[2];
        Assert.True(state.TryEnrich(fixture.Generation, top, NoNewEntry));
        Assert.True(state.TryEnrich(fixture.Generation, top, NoNewEntry));
        Assert.False(state.TryEnrich(fixture.Generation, top, NoNewEntry));
        NativeLazerGameRouteResult result = fixture.ReadRoute(
            state.Current!.MethodTables!);

        Assert.Equal(2, resolutions);
        Assert.Equal(3, state.Current.InvocationCount);
        Assert.Equal(NativeLazerGameRoute.Unknown, result.Route);
        Assert.Contains("no new", state.Current.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EmptyInitialResolutionCanBeRecoveredFromObservedTop()
    {
        Fixture fixture = new(count: 3);
        ulong top = Fixture.MethodTables[2];
        var state = new NativeLazerGameRouteMethodTableState();
        state.Initialize(new(
            fixture.Generation,
            null,
            4,
            "No route MethodTables are currently constructed."));

        NativeLazerGameRouteResult before = fixture.ReadRoute(null);
        Assert.Equal(NativeLazerGameRoute.Unknown, before.Route);
        Assert.Equal(top, before.TopMethodTable);

        Assert.True(state.TryEnrich(
            fixture.Generation,
            before.TopMethodTable!.Value,
            () => Resolution(fixture.Generation, top, null, null)));

        Assert.Equal(2, state.Current!.InvocationCount);
        Assert.Equal(
            NativeLazerGameRoute.SongSelect,
            fixture.ReadRoute(state.Current.MethodTables).Route);
    }

    [Fact]
    public void FailedResolutionForSameTopCanRecoverOnSecondAttempt()
    {
        Fixture fixture = new(count: 3);
        ulong top = Fixture.MethodTables[2];
        var state = new NativeLazerGameRouteMethodTableState();
        state.Initialize(Resolution(fixture.Generation, 0xE1000, null, null));

        Assert.True(state.TryEnrich(
            fixture.Generation,
            top,
            () => new(fixture.Generation, null, 4, "Transient CLR failure.")));
        Assert.True(state.TryEnrich(
            fixture.Generation,
            top,
            () => Resolution(fixture.Generation, null, top, null)));

        Assert.Equal(
            NativeLazerGameRoute.Gameplay,
            fixture.ReadRoute(state.Current!.MethodTables).Route);
        Assert.Equal(3, state.Current.InvocationCount);
    }

    [Fact]
    public void FailedEnrichmentKeepsKnownEntriesButNeverReturnsThemForUnknownTop()
    {
        Fixture fixture = new(count: 3);
        var state = new NativeLazerGameRouteMethodTableState();
        state.Initialize(Resolution(fixture.Generation, 0xE1000, null, null));

        Assert.True(state.TryEnrich(
            fixture.Generation,
            Fixture.MethodTables[2],
            () => new(
                fixture.Generation,
                null,
                4,
                "CLR resolution failed.")));

        Assert.Equal(
            NativeLazerGameRoute.Unknown,
            fixture.ReadRoute(state.Current!.MethodTables!).Route);
        Assert.Equal("CLR resolution failed.", state.Current.FailureReason);
        Assert.Equal(2, state.Current.InvocationCount);
    }

    [Fact]
    public void RestartReinitializesStateWithoutReusingPreviousGenerationTable()
    {
        Fixture fixture = new(count: 3);
        NativeLazerGenerationIdentity restarted = new(84, start.AddMinutes(1));
        var state = new NativeLazerGameRouteMethodTableState();
        state.Initialize(Resolution(
            fixture.Generation,
            Fixture.MethodTables[2],
            null,
            null));

        state.Initialize(Resolution(restarted, null, 0xF2000, null));

        Assert.Equal(restarted, state.Current!.Generation);
        Assert.Null(state.Current.MethodTables!.SoloSongSelect);
        Assert.Equal(0xF2000UL, state.Current.MethodTables.SoloPlayer);
        Assert.Equal(1, state.Current.InvocationCount);
    }

    [Fact]
    public void UnknownTopMethodTableFailsClosed()
    {
        Fixture fixture = new(count: 3);

        NativeLazerGameRouteResult result = fixture.ReadRoute(new(
            fixture.Generation,
            0xE1000,
            0xE2000,
            0xE3000));

        Assert.Equal(NativeLazerGameRoute.Unknown, result.Route);
        Assert.Contains("unknown", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OldGenerationMethodTableMappingIsRejectedWithoutReadingMemory()
    {
        Fixture fixture = new(count: 3);
        NativeLazerGenerationIdentity oldGeneration = new(42, start.AddSeconds(-1));

        NativeLazerGameRouteResult result = fixture.ReadRoute(new(
            oldGeneration,
            Fixture.MethodTables[2],
            0xE2000,
            0xE3000));

        Assert.Equal(NativeLazerGameRoute.Unknown, result.Route);
        Assert.Equal(0, result.Metrics.ReadOperations);
    }

    [Fact]
    public void NewGenerationAcceptsOnlyItsOwnMapping()
    {
        Fixture fixture = new(count: 3);

        NativeLazerGameRouteResult result = fixture.ReadRoute(
            fixture.TablesFor(NativeLazerGameRoute.Gameplay));

        Assert.Equal(NativeLazerGameRoute.Gameplay, result.Route);
    }

    [Fact]
    public void InvalidRootFailsClosed()
    {
        Fixture fixture = new(count: 3);

        NativeLazerGameRouteResult result = fixture.RouteReader.Read(
            fixture.Generation,
            fixture.Generation,
            0x99999,
            fixture.Layout,
            fixture.TablesFor(NativeLazerGameRoute.SongSelect),
            fixture.Memory,
            _ => true);

        Assert.Equal(NativeLazerGameRoute.Unknown, result.Route);
    }

    [Fact]
    public void UnreadableTopObjectFailsClosed()
    {
        Fixture fixture = new(count: 3);
        fixture.ClearTop();

        NativeLazerGameRouteResult result = fixture.ReadRoute(
            fixture.TablesFor(NativeLazerGameRoute.SongSelect));

        Assert.Equal(NativeLazerGameRoute.Unknown, result.Route);
    }

    [Fact]
    public void PartialTopMethodTableReadFailsClosed()
    {
        Fixture fixture = new(count: 3);
        fixture.Memory.FailReadAtAddress = Fixture.Objects[2];

        NativeLazerGameRouteResult result = fixture.ReadRoute(
            fixture.TablesFor(NativeLazerGameRoute.SongSelect));

        Assert.Equal(NativeLazerGameRoute.Unknown, result.Route);
        Assert.Contains("unavailable", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    private static NativeLazerGameRouteResolution Resolution(
        NativeLazerGenerationIdentity generation,
        ulong? songSelect,
        ulong? player,
        ulong? results) => new(
            generation,
            new NativeLazerGameRouteMethodTables(
                generation,
                songSelect,
                player,
                results),
            3,
            null);

    private sealed class Fixture
    {
        internal const ulong Root = 0x10000;
        internal const ulong ScreenStack = 0x20000;
        internal const ulong Stack = 0x30000;
        internal const ulong Array = 0x40000;
        internal const ulong ReplacementObject = 0x90000;
        internal static readonly ulong[] Objects =
            Enumerable.Range(0, 10).Select(index => 0x50000UL + (ulong)(index * 0x1000)).ToArray();
        internal static readonly ulong[] MethodTables =
            Enumerable.Range(0, 10).Select(index => 0xA0000UL + (ulong)(index * 0x1000)).ToArray();

        internal Fixture(int count)
        {
            Layout = new(
                OsuGameScreenStack: 0x10,
                ScreenStackStack: 0x18,
                StackArray: 0x08,
                StackSize: 0x10,
                ArrayLength: 0x08,
                ArrayData: 0x10,
                PlayerGame: 0x20,
                PlayerGameplayState: 0x28,
                PlayerGameplayClockContainer: 0x30,
                PlayerPlayingState: 0x38,
                GameplayClockContainerClock: 0x20,
                GameplayClockContainerPaused: 0x28,
                GameplayClockContainerStartTime: 0x30,
                GameplayClockContainerGameplayStartTime: 0x38,
                FramedBeatmapClockInitialSeek: 0x20,
                FramedBeatmapClockDecoupledTrack: 0x30,
                FramedBeatmapClockInterpolatedTrack: 0x38,
                FramedBeatmapClockUserBeatmapOffsetClock: 0x40,
                FramedBeatmapClockFinalClockSource: 0x48,
                InterpolatingClockCurrentTime: 0x20,
                DecouplingClockCurrentTime: 0x20,
                FramedClockCurrentTime: 0x28,
                FramedOffsetClockOffset: 0x30,
                BindableBooleanValue: 0x20,
                BindablePlayingStateValue: 0x20);
            Generation = new(42, start);
            Memory.SetObject(Root, 0x110000);
            Memory.SetObject(ScreenStack, 0x120000);
            Memory.SetObject(Stack, 0x130000);
            Memory.SetObject(Array, 0x140000);
            Memory.SetObject(ReplacementObject, 0x190000);
            Memory.SetPointer(Root + Layout.OsuGameScreenStack, ScreenStack);
            Memory.SetPointer(ScreenStack + Layout.ScreenStackStack, Stack);
            Memory.SetPointer(Stack + Layout.StackArray, Array);
            Memory.SetInt32(Stack + Layout.StackSize, count);
            Memory.SetInt32(Array + Layout.ArrayLength, Math.Max(count, 16));
            for (int index = 0; index < count; index++)
            {
                Memory.SetObject(Objects[index], MethodTables[index]);
                Memory.SetPointer(
                    Array + Layout.ArrayData +
                    ((ulong)index * NativeLazerGameplayClockLayout.PointerSize),
                    Objects[index]);
            }
        }

        internal NativeLazerScreenStackReader Reader { get; } = new();
        internal NativeLazerGameRouteReader RouteReader { get; } = new();
        internal NativeLazerGameplayClockLayout Layout { get; }
        internal FakeMemory Memory { get; } = new();
        internal NativeLazerGenerationIdentity Generation { get; }

        internal NativeLazerScreenStackSample Read() => Reader.Read(
            Generation,
            Generation,
            Root,
            Layout,
            Memory,
            identity => identity == Generation);

        internal NativeLazerGameRouteResult ReadRoute(
            NativeLazerGameRouteMethodTables? tables) => RouteReader.Read(
            Generation,
            Generation,
            Root,
            Layout,
            tables,
            Memory,
            identity => identity == Generation);

        internal NativeLazerGameRouteMethodTables TablesFor(NativeLazerGameRoute route)
        {
            ulong top = MethodTables[2];
            return route switch
            {
                NativeLazerGameRoute.SongSelect => new(Generation, top, 0xE2000, 0xE3000),
                NativeLazerGameRoute.Gameplay => new(Generation, 0xE1000, top, 0xE3000),
                NativeLazerGameRoute.Results => new(Generation, 0xE1000, 0xE2000, top),
                _ => new(Generation, 0xE1000, 0xE2000, 0xE3000)
            };
        }

        internal void ClearTop() => Memory.SetPointer(
            Array + Layout.ArrayData +
                ((ulong)(3 - 1) * NativeLazerGameplayClockLayout.PointerSize),
            0);

        internal void SetTopMethodTable(ulong methodTable) =>
            Memory.SetObject(Objects[2], methodTable);
    }

    private sealed class FakeMemory : INativeLazerGameplayClockMemory
    {
        private readonly Dictionary<ulong, byte> bytes = [];

        internal Action<ulong, int>? BeforeRead { get; set; }
        internal ulong? FailReadAtAddress { get; set; }
        public long BytesRead { get; private set; }
        public int ReadOperationCount { get; private set; }

        public byte? ReadByte(ulong address) =>
            Read(address, 1) is { } data ? data[0] : null;

        public int? ReadInt32(ulong address) =>
            Read(address, 4) is { } data
                ? BinaryPrimitives.ReadInt32LittleEndian(data)
                : null;

        public double? ReadDouble(ulong address) =>
            Read(address, 8) is { } data
                ? BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(data))
                : null;

        public ulong? ReadPointer(ulong address) =>
            Read(address, 8) is { } data
                ? BinaryPrimitives.ReadUInt64LittleEndian(data)
                : null;

        internal void SetObject(ulong address, ulong methodTable) =>
            SetPointer(address, methodTable);

        internal void SetInt32(ulong address, int value) =>
            Set(address, BitConverter.GetBytes(value));

        internal void SetPointer(ulong address, ulong value) =>
            Set(address, BitConverter.GetBytes(value));

        private byte[]? Read(ulong address, int count)
        {
            BeforeRead?.Invoke(address, count);
            ReadOperationCount++;
            BytesRead += count;
            if (address == FailReadAtAddress)
                return null;
            var result = new byte[count];
            for (int index = 0; index < count; index++)
            {
                if (!bytes.TryGetValue(address + (ulong)index, out result[index]))
                    return null;
            }
            return result;
        }

        private void Set(ulong address, byte[] value)
        {
            for (int index = 0; index < value.Length; index++)
                bytes[address + (ulong)index] = value[index];
        }
    }
}
