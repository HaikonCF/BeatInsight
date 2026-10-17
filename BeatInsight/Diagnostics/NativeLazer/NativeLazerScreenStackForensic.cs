using System.Diagnostics;

namespace BeatInsight.Diagnostics.NativeLazer;

internal enum NativeLazerScreenStackOutcome
{
    Unique,
    Unknown
}

internal sealed record NativeLazerScreenStackEntry(
    int Index,
    ulong ObjectAddress,
    ulong MethodTable,
    bool IsTop);

internal sealed record NativeLazerScreenStackMetrics(
    double DurationMs,
    long BytesRead,
    int ReadOperations,
    int PointerDereferences);

internal sealed record NativeLazerScreenStackSample
{
    internal required NativeLazerGenerationIdentity Generation { get; init; }
    internal NativeLazerScreenStackOutcome Outcome { get; init; }
    internal ulong? RootAddress { get; init; }
    internal ulong? ScreenStackAddress { get; init; }
    internal int? Count { get; init; }
    internal IReadOnlyList<NativeLazerScreenStackEntry> Entries { get; init; } = [];
    internal DateTimeOffset ObservedAtUtc { get; init; }
    internal string? FailureReason { get; init; }
    internal required NativeLazerScreenStackMetrics Metrics { get; init; }
}

/// <summary>
/// Bounded ScreenStack reader over an already validated root.
/// It performs no discovery, retains no prior state, and fails closed if the
/// stack identity or any sampled entry changes during the read.
/// </summary>
internal sealed class NativeLazerScreenStackReader
{
    internal const int MaximumStackCount = 64;
    internal const int MaximumCapturedEntries = 8;

    internal NativeLazerScreenStackSample Read(
        NativeLazerGenerationIdentity generation,
        NativeLazerGenerationIdentity validatedGeneration,
        ulong validatedRootAddress,
        NativeLazerGameplayClockLayout layout,
        INativeLazerGameplayClockMemory memory,
        Func<NativeLazerGenerationIdentity, bool> generationStillCurrent)
    {
        long started = Stopwatch.GetTimestamp();
        long initialBytes = memory.BytesRead;
        int initialOperations = memory.ReadOperationCount;
        int pointers = 0;
        DateTimeOffset observedAt = DateTimeOffset.UtcNow;

        NativeLazerScreenStackSample Finish(
            NativeLazerScreenStackOutcome outcome,
            string? reason = null,
            ulong? screenStack = null,
            int? count = null,
            IReadOnlyList<NativeLazerScreenStackEntry>? entries = null) => new()
        {
            Generation = generation,
            Outcome = outcome,
            RootAddress = outcome == NativeLazerScreenStackOutcome.Unique
                ? validatedRootAddress
                : null,
            ScreenStackAddress = outcome == NativeLazerScreenStackOutcome.Unique
                ? screenStack
                : null,
            Count = outcome == NativeLazerScreenStackOutcome.Unique ? count : null,
            Entries = outcome == NativeLazerScreenStackOutcome.Unique
                ? entries ?? []
                : [],
            ObservedAtUtc = observedAt,
            FailureReason = reason,
            Metrics = new(
                Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                memory.BytesRead - initialBytes,
                memory.ReadOperationCount - initialOperations,
                pointers)
        };

        ulong? Pointer(ulong address)
        {
            pointers++;
            return memory.ReadPointer(address);
        }

        if (generation != validatedGeneration || !generationStillCurrent(generation))
            return Finish(NativeLazerScreenStackOutcome.Unknown,
                "Process generation is not current.");
        if (validatedRootAddress < 0x10000 || !IsObject(validatedRootAddress, memory))
            return Finish(NativeLazerScreenStackOutcome.Unknown,
                "Validated root is unavailable or incoherent.");

        ulong? screenStack = Pointer(validatedRootAddress + layout.OsuGameScreenStack);
        ulong? stack = screenStack is >= 0x10000
            ? Pointer(screenStack.Value + layout.ScreenStackStack)
            : null;
        ulong? array = stack is >= 0x10000
            ? Pointer(stack.Value + layout.StackArray)
            : null;
        int? count = stack is >= 0x10000
            ? memory.ReadInt32(stack.Value + layout.StackSize)
            : null;
        int? capacity = array is >= 0x10000
            ? memory.ReadInt32(array.Value + layout.ArrayLength)
            : null;
        if (!IsObject(screenStack, memory) || !IsObject(stack, memory) ||
            !IsObject(array, memory) || count is null or <= 0 or > MaximumStackCount ||
            capacity is null || capacity < count || capacity > 256)
        {
            return Finish(NativeLazerScreenStackOutcome.Unknown,
                "ScreenStack structure is unavailable or incoherent.");
        }

        int firstIndex = Math.Max(0, count.Value - MaximumCapturedEntries);
        var entries = new List<NativeLazerScreenStackEntry>(count.Value - firstIndex);
        for (int index = firstIndex; index < count.Value; index++)
        {
            ulong slot = array!.Value + layout.ArrayData +
                checked((ulong)index * NativeLazerGameplayClockLayout.PointerSize);
            ulong? address = Pointer(slot);
            ulong? methodTable = address is >= 0x10000 ? Pointer(address.Value) : null;
            if (address is not >= 0x10000 || methodTable is not >= 0x10000)
            {
                return Finish(NativeLazerScreenStackOutcome.Unknown,
                    $"ScreenStack entry {index} is unavailable or not a readable CLR object.");
            }

            entries.Add(new(
                index,
                address.Value,
                methodTable.Value,
                index == count.Value - 1));
        }

        if (!generationStillCurrent(generation))
            return Finish(NativeLazerScreenStackOutcome.Unknown,
                "Process generation changed during the ScreenStack read.");

        ulong? endingScreenStack = Pointer(validatedRootAddress + layout.OsuGameScreenStack);
        ulong? endingStack = screenStack is >= 0x10000
            ? Pointer(screenStack.Value + layout.ScreenStackStack)
            : null;
        ulong? endingArray = stack is >= 0x10000
            ? Pointer(stack.Value + layout.StackArray)
            : null;
        int? endingCount = stack is >= 0x10000
            ? memory.ReadInt32(stack.Value + layout.StackSize)
            : null;
        if (endingScreenStack != screenStack || endingStack != stack ||
            endingArray != array || endingCount != count)
        {
            return Finish(NativeLazerScreenStackOutcome.Unknown,
                "ScreenStack identity changed during the read.");
        }

        foreach (NativeLazerScreenStackEntry entry in entries)
        {
            ulong slot = array!.Value + layout.ArrayData +
                checked((ulong)entry.Index * NativeLazerGameplayClockLayout.PointerSize);
            ulong? endingAddress = Pointer(slot);
            ulong? endingMethodTable = endingAddress is >= 0x10000
                ? Pointer(endingAddress.Value)
                : null;
            if (endingAddress != entry.ObjectAddress || endingMethodTable != entry.MethodTable)
            {
                return Finish(NativeLazerScreenStackOutcome.Unknown,
                    $"ScreenStack entry {entry.Index} changed during the read.");
            }
        }

        return Finish(
            NativeLazerScreenStackOutcome.Unique,
            screenStack: screenStack,
            count: count,
            entries: entries);
    }

    private static bool IsObject(
        ulong? address,
        INativeLazerGameplayClockMemory memory) =>
        address is >= 0x10000 && memory.ReadPointer(address.Value) is >= 0x10000;
}
