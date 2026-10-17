using BeatInsight.Diagnostics.NativeStable;

namespace BeatInsight.Integration.KeyOverlay;

/// <summary>Index trié des régions lisibles (même règle que IsReadableAlignedPointer de BeatInsight).</summary>
internal sealed class RegionIndex
{
    private readonly NativeStableMemoryRegion[] regions;

    public RegionIndex(IEnumerable<NativeStableMemoryRegion> source)
    {
        regions = source.OrderBy(r => r.BaseAddress).ToArray();
    }

    public int Count => regions.Length;

    public bool IsReadableAligned(nuint address, int requiredBytes)
    {
        if (address < 0x10000 || (address & 3) != 0)
            return false;
        int lo = 0, hi = regions.Length - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            NativeStableMemoryRegion r = regions[mid];
            if (address < r.BaseAddress) hi = mid - 1;
            else if (address - r.BaseAddress >= r.Size) lo = mid + 1;
            else return r.Size - (address - r.BaseAddress) >= (nuint)requiredBytes;
        }
        return false;
    }
}

/// <summary>Adresses lues pendant un échantillon. Usage strictement local à l'échantillon.</summary>
internal readonly record struct KeyCounterChainAddresses(
    nuint Ruleset, nuint KeyOverlay, nuint List, nuint Items, nuint E0, nuint E1, nuint E2, nuint E3);

internal readonly record struct KeyCounterChainRead(
    KeyOverlayReadFailureReason Failure,
    KeyCounterChainAddresses Addresses,
    KeyOverlayKeys? Keys)
{
    public bool Ok => Failure == KeyOverlayReadFailureReason.None;

    public static KeyCounterChainRead Fail(KeyOverlayReadFailureReason reason, nuint ruleset = 0) =>
        new(reason, new(ruleset, 0, 0, 0, 0, 0, 0, 0), null);
}

/// <summary>
/// Lecture de la chaîne validée en COM-KEY-02, sans aucun cache d'adresse d'objet :
///
///   ruleset    = [container + 0x4]
///   mode       = int [[[ruleset + 0x64] + 0x38] + 0x64]        (0 = osu!standard)
///   keyOverlay = [ruleset + 0xAC]
///   list       = [keyOverlay + 0x10]     List&lt;T&gt; : +0x4 _items, +0xC _size
///   items      = [list + 0x4]            tableau : +0x4 length, +0x8 éléments
///   key[i]     = [items + 0x8 + 4i]      count = int32 +0x14, isPressed = octet +0x1C
///
/// Puis relecture des références racines et des emplacements du tableau : si l'une a changé
/// pendant l'échantillon (compaction GC), l'échantillon est rejeté en bloc.
/// </summary>
internal static class KeyCounterChain
{
    public const int RulesetSlot = 0x4;
    public const int GameplayBaseOffset = 0x64;
    public const int ScoreBaseOffset = 0x38;
    public const int ModeOffset = 0x64;
    public const int KeyOverlayOffset = 0xAC;
    public const int ListOffset = 0x10;
    public const int ListItemsOffset = 0x4;
    public const int ListSizeOffset = 0xC;
    public const int ArrayLengthOffset = 0x4;
    public const int ArrayFirstElementOffset = 0x8;
    public const int CountOffset = 0x14;
    public const int PressedOffset = 0x1C;
    public const int ElementBytes = 0x20;
    public const int ExpectedKeys = 4;
    public const int MaxCount = 1_000_000;
    public const int StandardMode = 0;

    public static KeyCounterChainRead Read(INativeStableMemoryReader memory, RegionIndex regions, nuint container)
    {
        if (!regions.IsReadableAligned(container, RulesetSlot + 4))
            return KeyCounterChainRead.Fail(KeyOverlayReadFailureReason.ContainerUnreadable);
        if (!TryU32(memory, container + RulesetSlot, out nuint ruleset, out bool complete))
            return KeyCounterChainRead.Fail(complete ? KeyOverlayReadFailureReason.RulesetNull : KeyOverlayReadFailureReason.ReadIncomplete);
        if (ruleset == 0)
            return KeyCounterChainRead.Fail(KeyOverlayReadFailureReason.RulesetNull);
        if (!regions.IsReadableAligned(ruleset, KeyOverlayOffset + 4))
            return KeyCounterChainRead.Fail(KeyOverlayReadFailureReason.RulesetUnreadable, ruleset);

        // Mode osu!standard.
        KeyOverlayReadFailureReason modeFailure = ReadMode(memory, regions, ruleset, out int mode);
        if (modeFailure != KeyOverlayReadFailureReason.None)
            return KeyCounterChainRead.Fail(modeFailure, ruleset);
        if (mode != StandardMode)
            return KeyCounterChainRead.Fail(KeyOverlayReadFailureReason.ModeUnsupported, ruleset);

        // KeyOverlay.
        if (!TryU32(memory, ruleset + KeyOverlayOffset, out nuint keyOverlay, out complete))
            return KeyCounterChainRead.Fail(KeyOverlayReadFailureReason.ReadIncomplete, ruleset);
        if (keyOverlay == 0)
            return KeyCounterChainRead.Fail(KeyOverlayReadFailureReason.KeyOverlayPointerNull, ruleset);
        if (!regions.IsReadableAligned(keyOverlay, ListOffset + 4))
            return KeyCounterChainRead.Fail(KeyOverlayReadFailureReason.KeyOverlayUnreadable, ruleset);
        NativeStableMemoryReadResult koBlock = memory.ReadBytes(keyOverlay, ListOffset + 4);
        if (!koBlock.Complete)
            return KeyCounterChainRead.Fail(KeyOverlayReadFailureReason.ReadIncomplete, ruleset);
        if (!regions.IsReadableAligned(U32(koBlock.Data, 0), 4))
            return KeyCounterChainRead.Fail(KeyOverlayReadFailureReason.KeyOverlayTypeInvalid, ruleset);
        nuint list = U32(koBlock.Data, ListOffset);

        // List<KeyCounter>.
        if (!regions.IsReadableAligned(list, ListSizeOffset + 4))
            return KeyCounterChainRead.Fail(KeyOverlayReadFailureReason.ListUnreadable, ruleset);
        NativeStableMemoryReadResult listBlock = memory.ReadBytes(list, ListSizeOffset + 4);
        if (!listBlock.Complete)
            return KeyCounterChainRead.Fail(KeyOverlayReadFailureReason.ReadIncomplete, ruleset);
        nuint listType = U32(listBlock.Data, 0);
        nuint items = U32(listBlock.Data, ListItemsOffset);
        int size = BitConverter.ToInt32(listBlock.Data, ListSizeOffset);
        if (!regions.IsReadableAligned(listType, 4))
            return KeyCounterChainRead.Fail(KeyOverlayReadFailureReason.ListTypeInvalid, ruleset);

        // Tableau + emplacements, lus d'un bloc.
        int arrayBytes = ArrayFirstElementOffset + 4 * ExpectedKeys;
        if (!regions.IsReadableAligned(items, arrayBytes))
            return KeyCounterChainRead.Fail(KeyOverlayReadFailureReason.ItemsUnreadable, ruleset);
        NativeStableMemoryReadResult arrayBlock = memory.ReadBytes(items, arrayBytes);
        if (!arrayBlock.Complete)
            return KeyCounterChainRead.Fail(KeyOverlayReadFailureReason.ReadIncomplete, ruleset);
        if (!regions.IsReadableAligned(U32(arrayBlock.Data, 0), 4))
            return KeyCounterChainRead.Fail(KeyOverlayReadFailureReason.ItemsTypeInvalid, ruleset);
        int length = BitConverter.ToInt32(arrayBlock.Data, ArrayLengthOffset);
        if (length != ExpectedKeys)
            return KeyCounterChainRead.Fail(KeyOverlayReadFailureReason.ItemsLengthInvalid, ruleset);
        if (size != length)
            return KeyCounterChainRead.Fail(KeyOverlayReadFailureReason.ListSizeInvalid, ruleset);

        Span<nuint> elements = stackalloc nuint[ExpectedKeys];
        var states = new KeyCounterState[ExpectedKeys];
        nuint elementType = 0;
        for (int i = 0; i < ExpectedKeys; i++)
        {
            nuint element = U32(arrayBlock.Data, ArrayFirstElementOffset + 4 * i);
            elements[i] = element;
            if (!regions.IsReadableAligned(element, ElementBytes))
                return KeyCounterChainRead.Fail(KeyOverlayReadFailureReason.ElementUnreadable, ruleset);
            for (int j = 0; j < i; j++)
                if (elements[j] == element)
                    return KeyCounterChainRead.Fail(KeyOverlayReadFailureReason.ElementsNotDistinct, ruleset);
            NativeStableMemoryReadResult block = memory.ReadBytes(element, ElementBytes);
            if (!block.Complete)
                return KeyCounterChainRead.Fail(KeyOverlayReadFailureReason.ReadIncomplete, ruleset);
            nuint type = U32(block.Data, 0);
            if (i == 0)
            {
                if (!regions.IsReadableAligned(type, 4))
                    return KeyCounterChainRead.Fail(KeyOverlayReadFailureReason.ElementTypeInvalid, ruleset);
                elementType = type;
            }
            else if (type != elementType)
            {
                return KeyCounterChainRead.Fail(KeyOverlayReadFailureReason.ElementTypeMismatch, ruleset);
            }
            int count = BitConverter.ToInt32(block.Data, CountOffset);
            byte pressed = block.Data[PressedOffset];
            if (count is < 0 or > MaxCount)
                return KeyCounterChainRead.Fail(KeyOverlayReadFailureReason.CountOutOfRange, ruleset);
            if (pressed > 1)
                return KeyCounterChainRead.Fail(KeyOverlayReadFailureReason.PressedNotBoolean, ruleset);
            states[i] = new(count, pressed == 1);
        }

        var addresses = new KeyCounterChainAddresses(
            ruleset, keyOverlay, list, items, elements[0], elements[1], elements[2], elements[3]);

        // Garde de cohérence : la chaîne n'a pas bougé pendant l'échantillon.
        if (!StillSame(memory, container, addresses))
            return KeyCounterChainRead.Fail(KeyOverlayReadFailureReason.StructureChangedDuringRead, ruleset);

        return new(KeyOverlayReadFailureReason.None, addresses,
            new KeyOverlayKeys(states[0], states[1], states[2], states[3]));
    }

    private static KeyOverlayReadFailureReason ReadMode(
        INativeStableMemoryReader memory, RegionIndex regions, nuint ruleset, out int mode)
    {
        mode = -1;
        if (!TryU32(memory, ruleset + GameplayBaseOffset, out nuint gameplayBase, out bool complete))
            return complete ? KeyOverlayReadFailureReason.ModeChainUnreadable : KeyOverlayReadFailureReason.ReadIncomplete;
        if (!regions.IsReadableAligned(gameplayBase, ScoreBaseOffset + 4))
            return KeyOverlayReadFailureReason.ModeChainUnreadable;
        if (!TryU32(memory, gameplayBase + ScoreBaseOffset, out nuint scoreBase, out complete))
            return complete ? KeyOverlayReadFailureReason.ModeChainUnreadable : KeyOverlayReadFailureReason.ReadIncomplete;
        if (!regions.IsReadableAligned(scoreBase, ModeOffset + 4))
            return KeyOverlayReadFailureReason.ModeChainUnreadable;
        NativeStableMemoryReadResult read = memory.ReadBytes(scoreBase + ModeOffset, 4);
        if (!read.Complete)
            return KeyOverlayReadFailureReason.ReadIncomplete;
        mode = BitConverter.ToInt32(read.Data, 0);
        return KeyOverlayReadFailureReason.None;
    }

    private static bool StillSame(INativeStableMemoryReader memory, nuint container, KeyCounterChainAddresses a)
    {
        return TryU32(memory, container + RulesetSlot, out nuint ruleset, out _) && ruleset == a.Ruleset &&
               TryU32(memory, a.Ruleset + KeyOverlayOffset, out nuint keyOverlay, out _) && keyOverlay == a.KeyOverlay &&
               TryU32(memory, a.KeyOverlay + ListOffset, out nuint list, out _) && list == a.List &&
               TryU32(memory, a.List + ListItemsOffset, out nuint items, out _) && items == a.Items &&
               SlotsSame(memory, a);
    }

    private static bool SlotsSame(INativeStableMemoryReader memory, KeyCounterChainAddresses a)
    {
        NativeStableMemoryReadResult slots = memory.ReadBytes(a.Items + ArrayFirstElementOffset, 4 * ExpectedKeys);
        return slots.Complete &&
               U32(slots.Data, 0) == a.E0 && U32(slots.Data, 4) == a.E1 &&
               U32(slots.Data, 8) == a.E2 && U32(slots.Data, 12) == a.E3;
    }

    /// <summary>Lecture d'un pointeur 32 bits. <paramref name="complete"/> distingue « nul » de « illisible ».</summary>
    private static bool TryU32(INativeStableMemoryReader memory, nuint address, out nuint value, out bool complete)
    {
        NativeStableMemoryReadResult read = memory.ReadBytes(address, 4);
        complete = read.Complete;
        value = complete ? U32(read.Data, 0) : 0;
        return complete;
    }

    private static nuint U32(byte[] data, int offset) => BitConverter.ToUInt32(data, offset);
}
