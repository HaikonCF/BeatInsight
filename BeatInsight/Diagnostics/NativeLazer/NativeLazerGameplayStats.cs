using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace BeatInsight.Diagnostics.NativeLazer;

internal enum NativeLazerGameplayStatsOutcome
{
    Valid,
    Unavailable,
    Invalid
}

internal sealed record NativeLazerGameplayStatsLayout(
    ulong PlayerScoreProcessor,
    ulong ScoreProcessorTotalScore,
    ulong ScoreProcessorAccuracy,
    ulong ScoreProcessorCombo,
    ulong ScoreProcessorHighestCombo,
    ulong ScoreProcessorMods,
    ulong ScoreProcessorResultCounts,
    ulong BindableLongValue,
    ulong BindableDoubleValue,
    ulong BindableIntValue,
    ulong BindableModsValue,
    ulong DictionaryEntries,
    ulong DictionaryCount,
    ulong ListItems,
    ulong ListSize,
    int EntryStride,
    int EntryNext,
    int EntryKey,
    int EntryValue,
    int SoloPlayerToken,
    int ScoreProcessorToken,
    IReadOnlyDictionary<int, string> SupportedModTokens)
{
    internal const int ArrayLength = 8;
    internal const int ArrayData = 16;
}

internal sealed record NativeLazerGameplayStatsMethodTables(
    NativeLazerGenerationIdentity Generation,
    ulong SoloPlayer,
    ulong ScoreProcessor,
    IReadOnlyDictionary<ulong, string> SupportedMods)
{
    internal bool IsCoherent =>
        Generation.ProcessId > 0 && SoloPlayer >= 0x10000 && ScoreProcessor >= 0x10000;
}

internal sealed record NativeLazerGameplayStatsResult
{
    internal required NativeLazerGenerationIdentity Generation { get; init; }
    internal NativeLazerGameplayStatsOutcome Outcome { get; init; }
    internal double? CurrentTimeMilliseconds { get; init; }
    internal int? CurrentCombo { get; init; }
    internal int? HighestCombo { get; init; }
    internal int? CountGreat { get; init; }
    internal int? CountOk { get; init; }
    internal int? CountMeh { get; init; }
    internal int? CountMiss { get; init; }
    internal int? CountLargeTickMiss { get; init; }
    internal int? CountSliderTailHit { get; init; }
    internal double? Accuracy { get; init; }
    internal int? Score { get; init; }
    internal IReadOnlyList<string> Mods { get; init; } = [];
    internal DateTimeOffset ObservedAtUtc { get; init; }
    internal string? FailureReason { get; init; }
    internal double DurationMs { get; init; }
    internal long BytesRead { get; init; }
    internal int ReadOperations { get; init; }
}

internal static class NativeLazerGameplayStatsModuleNames
{
    internal static bool IsGame(string file) =>
        string.Equals(file, "osu.Game.dll", StringComparison.OrdinalIgnoreCase);

    internal static bool IsOsuRuleset(string file) =>
        string.Equals(file, "osu.Game.Rulesets.Osu.dll", StringComparison.OrdinalIgnoreCase);
}

internal static class NativeLazerGameplayStatsAssemblyInspector
{
    private static readonly IReadOnlyDictionary<string, string> supportedMods =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["osu.Game.Rulesets.Osu.Mods.OsuModHidden"] = "HD",
            ["osu.Game.Rulesets.Osu.Mods.OsuModHardRock"] = "HR",
            ["osu.Game.Rulesets.Osu.Mods.OsuModDoubleTime"] = "DT",
            ["osu.Game.Rulesets.Osu.Mods.OsuModNightcore"] = "NC",
            ["osu.Game.Rulesets.Osu.Mods.OsuModEasy"] = "EZ",
            ["osu.Game.Rulesets.Osu.Mods.OsuModHalfTime"] = "HT",
            ["osu.Game.Rulesets.Osu.Mods.OsuModFlashlight"] = "FL",
            ["osu.Game.Rulesets.Osu.Mods.OsuModNoFail"] = "NF",
            ["osu.Game.Rulesets.Osu.Mods.OsuModSuddenDeath"] = "SD",
            ["osu.Game.Rulesets.Osu.Mods.OsuModPerfect"] = "PF",
            ["osu.Game.Rulesets.Osu.Mods.OsuModSpunOut"] = "SO"
        };

    internal static NativeLazerGameplayStatsLayout Inspect(string installDirectory)
    {
        var context = new StatsLoadContext(installDirectory);
        try
        {
            Assembly game = context.LoadFromAssemblyPath(Path.Combine(installDirectory, "osu.Game.dll"));
            Assembly framework = context.LoadFromAssemblyPath(Path.Combine(installDirectory, "osu.Framework.dll"));
            Assembly osu = context.LoadFromAssemblyPath(Path.Combine(installDirectory, "osu.Game.Rulesets.Osu.dll"));
            Type soloPlayer = Require(game, "osu.Game.Screens.Play.SoloPlayer");
            Type scoreProcessor = Require(osu, "osu.Game.Rulesets.Osu.Scoring.OsuScoreProcessor");
            Type hitResult = Require(game, "osu.Game.Rulesets.Scoring.HitResult");
            Type mod = Require(game, "osu.Game.Rulesets.Mods.Mod");
            Type modsInterface = typeof(IReadOnlyList<>).MakeGenericType(mod);
            Type bindableMods = Require(framework, "osu.Framework.Bindables.Bindable`1")
                .MakeGenericType(modsInterface);
            Type dictionary = typeof(Dictionary<,>).MakeGenericType(hitResult, typeof(int));
            Type entry = dictionary.GetNestedType("Entry", BindingFlags.NonPublic)!
                .MakeGenericType(hitResult, typeof(int));
            Type modList = typeof(List<>).MakeGenericType(mod);
            int stride = ElementStride(entry);
            var tokens = new Dictionary<int, string>();
            foreach ((string name, string acronym) in supportedMods)
            {
                Type? type = osu.GetType(name, throwOnError: false, ignoreCase: false);
                if (type is not null)
                    tokens[type.MetadataToken] = acronym;
            }

            return new(
                Measure(soloPlayer, "<ScoreProcessor>k__BackingField"),
                Measure(scoreProcessor, "TotalScore"),
                Measure(scoreProcessor, "Accuracy"),
                Measure(scoreProcessor, "Combo"),
                Measure(scoreProcessor, "HighestCombo"),
                Measure(scoreProcessor, "Mods"),
                Measure(scoreProcessor, "ScoreResultCounts"),
                Measure(Require(framework, "osu.Framework.Bindables.BindableLong"), "value"),
                Measure(Require(framework, "osu.Framework.Bindables.BindableDouble"), "value"),
                Measure(Require(framework, "osu.Framework.Bindables.BindableInt"), "value"),
                Measure(bindableMods, "value"),
                Measure(dictionary, "_entries"),
                Measure(dictionary, "_count"),
                Measure(modList, "_items"),
                Measure(modList, "_size"),
                stride,
                Marshal.OffsetOf(entry, "next").ToInt32(),
                Marshal.OffsetOf(entry, "key").ToInt32(),
                Marshal.OffsetOf(entry, "value").ToInt32(),
                soloPlayer.MetadataToken,
                scoreProcessor.MetadataToken,
                tokens);
        }
        finally
        {
            context.Unload();
        }
    }

    private static int ElementStride(Type type)
    {
        Array values = Array.CreateInstance(type, 2);
        GCHandle pin = GCHandle.Alloc(values, GCHandleType.Pinned);
        try
        {
            return checked((int)(Marshal.UnsafeAddrOfPinnedArrayElement(values, 1) -
                Marshal.UnsafeAddrOfPinnedArrayElement(values, 0)));
        }
        finally
        {
            pin.Free();
        }
    }

    private static ulong Measure(Type concrete, string fieldName)
    {
        FieldInfo field = FindField(concrete, fieldName);
        object instance = RuntimeHelpers.GetUninitializedObject(concrete);
        var method = new DynamicMethod("stats_field_address", typeof(nint), [typeof(object)],
            typeof(NativeLazerGameplayStatsAssemblyInspector).Module, skipVisibility: true);
        ILGenerator il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, field.DeclaringType!);
        il.Emit(OpCodes.Ldflda, field);
        il.Emit(OpCodes.Conv_I);
        il.Emit(OpCodes.Ret);
        var accessor = method.CreateDelegate<Func<object, nint>>();
        bool noGc = GC.TryStartNoGCRegion(1024 * 1024);
        try
        {
            long offset = accessor(instance) - Unsafe.As<object, nint>(ref instance);
            if (offset is < 8 or > 0x4000)
                throw new InvalidOperationException($"Implausible offset for {concrete.FullName}.{fieldName}.");
            return checked((ulong)offset);
        }
        finally
        {
            GC.KeepAlive(instance);
            if (noGc) GC.EndNoGCRegion();
        }
    }

    private static FieldInfo FindField(Type type, string name)
    {
        for (Type? current = type; current is not null; current = current.BaseType)
        {
            FieldInfo? field = current.GetField(name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field is not null) return field;
        }
        throw new MissingFieldException(type.FullName, name);
    }

    private static Type Require(Assembly assembly, string name) =>
        assembly.GetType(name, throwOnError: true, ignoreCase: false)!;

    private sealed class StatsLoadContext(string directory) : AssemblyLoadContext(isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName name)
        {
            string candidate = Path.Combine(directory, name.Name + ".dll");
            return File.Exists(candidate) ? LoadFromAssemblyPath(candidate) : null;
        }
    }
}

internal interface INativeLazerGameplayStatsMethodTableResolver
{
    NativeLazerGameplayStatsMethodTables? Resolve(
        NativeLazerProcessCandidate process,
        NativeLazerGenerationIdentity generation,
        NativeLazerGameplayStatsLayout layout);
}

internal sealed class NativeLazerGameplayStatsReader
{
    private const int MaximumDictionaryEntries = 64;
    private const int MaximumMods = 32;

    internal NativeLazerGameplayStatsResult Read(
        NativeLazerGenerationIdentity generation,
        NativeLazerGenerationIdentity validatedGeneration,
        ulong player,
        double currentTimeMilliseconds,
        NativeLazerGameplayStatsLayout layout,
        NativeLazerGameplayStatsMethodTables tables,
        INativeLazerGameplayClockMemory memory,
        Func<NativeLazerGenerationIdentity, bool> generationStillCurrent)
    {
        long started = Stopwatch.GetTimestamp();
        long initialBytes = memory.BytesRead;
        int initialOperations = memory.ReadOperationCount;
        DateTimeOffset observed = DateTimeOffset.UtcNow;

        NativeLazerGameplayStatsResult Finish(NativeLazerGameplayStatsOutcome outcome, string? reason = null,
            int? combo = null, int? highest = null, int? great = null, int? ok = null, int? meh = null,
            int? miss = null, int? largeTickMiss = null, int? sliderTailHit = null,
            double? accuracy = null, int? score = null, IReadOnlyList<string>? mods = null) => new()
        {
            Generation = generation,
            Outcome = outcome,
            CurrentTimeMilliseconds = outcome == NativeLazerGameplayStatsOutcome.Valid ? currentTimeMilliseconds : null,
            CurrentCombo = combo,
            HighestCombo = highest,
            CountGreat = great,
            CountOk = ok,
            CountMeh = meh,
            CountMiss = miss,
            CountLargeTickMiss = largeTickMiss,
            CountSliderTailHit = sliderTailHit,
            Accuracy = accuracy,
            Score = score,
            Mods = mods ?? [],
            ObservedAtUtc = observed,
            FailureReason = reason,
            DurationMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            BytesRead = memory.BytesRead - initialBytes,
            ReadOperations = memory.ReadOperationCount - initialOperations
        };

        if (generation != validatedGeneration || tables.Generation != generation ||
            !tables.IsCoherent || !generationStillCurrent(generation))
            return Finish(NativeLazerGameplayStatsOutcome.Unavailable, "Process generation is not current.");
        if (!double.IsFinite(currentTimeMilliseconds) || currentTimeMilliseconds < 0)
            return Finish(NativeLazerGameplayStatsOutcome.Invalid, "Gameplay time is invalid.");
        if (player < 0x10000 || memory.ReadPointer(player) != tables.SoloPlayer)
            return Finish(NativeLazerGameplayStatsOutcome.Unavailable, "Validated SoloPlayer is unavailable.");

        ulong? processor = memory.ReadPointer(player + layout.PlayerScoreProcessor);
        if (processor is not >= 0x10000)
        {
            return Finish(NativeLazerGameplayStatsOutcome.Unavailable,
                $"ScoreProcessor pointer is unavailable at Player+0x{layout.PlayerScoreProcessor:X}.");
        }
        ulong? processorMethodTable = memory.ReadPointer(processor.Value);
        if (processorMethodTable != tables.ScoreProcessor)
        {
            return Finish(NativeLazerGameplayStatsOutcome.Unavailable,
                $"ScoreProcessor MethodTable mismatch: expected 0x{tables.ScoreProcessor:X}, " +
                $"observed {(processorMethodTable.HasValue ? $"0x{processorMethodTable.Value:X}" : "unavailable")}.");
        }

        ulong? comboBindable = memory.ReadPointer(processor.Value + layout.ScoreProcessorCombo);
        ulong? highestBindable = memory.ReadPointer(processor.Value + layout.ScoreProcessorHighestCombo);
        ulong? accuracyBindable = memory.ReadPointer(processor.Value + layout.ScoreProcessorAccuracy);
        ulong? scoreBindable = memory.ReadPointer(processor.Value + layout.ScoreProcessorTotalScore);
        ulong? counts = memory.ReadPointer(processor.Value + layout.ScoreProcessorResultCounts);
        ulong? modsBindable = memory.ReadPointer(processor.Value + layout.ScoreProcessorMods);
        if (!Object(comboBindable, memory) || !Object(highestBindable, memory) ||
            !Object(accuracyBindable, memory) || !Object(scoreBindable, memory) ||
            !Object(counts, memory) || !Object(modsBindable, memory))
            return Finish(NativeLazerGameplayStatsOutcome.Unavailable, "One or more score fields are unavailable.");

        int? combo = memory.ReadInt32(comboBindable!.Value + layout.BindableIntValue);
        int? highest = memory.ReadInt32(highestBindable!.Value + layout.BindableIntValue);
        double? accuracy = memory.ReadDouble(accuracyBindable!.Value + layout.BindableDoubleValue);
        long? score64 = memory.ReadInt64(scoreBindable!.Value + layout.BindableLongValue);
        if (!TryReadCounts(counts!.Value, layout, memory,
                out int great, out int ok, out int meh, out int miss,
                out int smallTickMiss, out int smallTickHit,
                out int largeTickMiss, out int largeTickHit, out int sliderTailHit,
                out string allCounts, out string? countsFailure))
            return Finish(NativeLazerGameplayStatsOutcome.Unavailable,
                $"Hitcounts are unavailable: {countsFailure}");
        if (!TryReadMods(modsBindable!.Value, layout, tables, memory,
                out IReadOnlyList<string> mods, out string? modsFailure))
            return Finish(NativeLazerGameplayStatsOutcome.Unavailable,
                $"Mods are unavailable: {modsFailure}");

        if (combo is null or < 0 || highest is null || highest < combo ||
            accuracy is null || !double.IsFinite(accuracy.Value) || accuracy is < 0 or > 1 ||
            score64 is null or < 0 or > int.MaxValue)
        {
            return Finish(NativeLazerGameplayStatsOutcome.Invalid,
                $"Score values are incoherent: combo={combo?.ToString() ?? "unavailable"}, " +
                $"highest={highest?.ToString() ?? "unavailable"}, " +
                $"accuracy={accuracy?.ToString("R") ?? "unavailable"}, " +
                $"score={score64?.ToString() ?? "unavailable"}, " +
                $"great={great}, ok={ok}, meh={meh}, miss={miss}, " +
                $"smallTickHit={smallTickHit}, smallTickMiss={smallTickMiss}, " +
                $"largeTickHit={largeTickHit}, largeTickMiss={largeTickMiss}, " +
                $"sliderTailHit={sliderTailHit}, all=[{allCounts}].");
        }

        if (!generationStillCurrent(generation) || memory.ReadPointer(player + layout.PlayerScoreProcessor) != processor)
            return Finish(NativeLazerGameplayStatsOutcome.Unavailable, "ScoreProcessor changed during the read.");

        return Finish(NativeLazerGameplayStatsOutcome.Valid, combo: combo, highest: highest,
            great: great, ok: ok, meh: meh, miss: miss,
            largeTickMiss: largeTickMiss, sliderTailHit: sliderTailHit, accuracy: accuracy,
            score: (int)score64.Value, mods: mods);
    }

    private static bool TryReadCounts(ulong dictionary, NativeLazerGameplayStatsLayout layout,
        INativeLazerGameplayClockMemory memory, out int great, out int ok, out int meh, out int miss,
        out int smallTickMiss, out int smallTickHit, out int largeTickMiss, out int largeTickHit,
        out int sliderTailHit, out string allCounts, out string? failureReason)
    {
        great = ok = meh = miss = 0;
        smallTickMiss = smallTickHit = largeTickMiss = largeTickHit = sliderTailHit = 0;
        allCounts = string.Empty;
        failureReason = null;
        ulong? entries = memory.ReadPointer(dictionary + layout.DictionaryEntries);
        int? count = memory.ReadInt32(dictionary + layout.DictionaryCount);
        int? capacity = entries is >= 0x10000
            ? memory.ReadInt32(entries.Value + NativeLazerGameplayStatsLayout.ArrayLength)
            : null;
        if (entries is not >= 0x10000)
        {
            failureReason = "dictionary entries pointer is unavailable.";
            return false;
        }
        if (count is null or < 0 or > MaximumDictionaryEntries)
        {
            failureReason = $"dictionary count is invalid ({count?.ToString() ?? "unavailable"}).";
            return false;
        }
        if (capacity is null || capacity < count || capacity > MaximumDictionaryEntries)
        {
            failureReason = $"dictionary capacity is invalid ({capacity?.ToString() ?? "unavailable"}, count={count}).";
            return false;
        }
        var observedCounts = new List<string>();
        for (int index = 0; index < count.Value; index++)
        {
            ulong entry = entries.Value + NativeLazerGameplayStatsLayout.ArrayData +
                checked((ulong)(index * layout.EntryStride));
            int? next = memory.ReadInt32(entry + checked((ulong)layout.EntryNext));
            int? key = memory.ReadInt32(entry + checked((ulong)layout.EntryKey));
            int? value = memory.ReadInt32(entry + checked((ulong)layout.EntryValue));
            if (next is null || key is null || value is null || value < 0)
            {
                failureReason = $"dictionary entry {index} is unreadable or invalid.";
                return false;
            }
            if (next < -1) continue;
            observedCounts.Add($"{key.Value}={value.Value}");
            switch (key.Value)
            {
                case 1: miss = value.Value; break;
                case 2: meh = value.Value; break;
                case 3: ok = value.Value; break;
                case 5: great = value.Value; break;
                case 7: smallTickMiss = value.Value; break;
                case 8: smallTickHit = value.Value; break;
                case 9: largeTickMiss = value.Value; break;
                case 10: largeTickHit = value.Value; break;
                case 16: sliderTailHit = value.Value; break;
            }
        }
        allCounts = string.Join(',', observedCounts.Order(StringComparer.Ordinal));
        return true;
    }

    private static bool TryReadMods(ulong bindable, NativeLazerGameplayStatsLayout layout,
        NativeLazerGameplayStatsMethodTables tables, INativeLazerGameplayClockMemory memory,
        out IReadOnlyList<string> mods, out string? failureReason)
    {
        mods = [];
        failureReason = null;
        ulong? collection = memory.ReadPointer(bindable + layout.BindableModsValue);
        if (collection is not >= 0x10000)
        {
            failureReason = "mod collection pointer is unavailable.";
            return false;
        }
        if (TryReadModArray(collection.Value, tables, memory, out mods, out _)) return true;
        ulong? array = memory.ReadPointer(collection.Value + layout.ListItems);
        int? count = memory.ReadInt32(collection.Value + layout.ListSize);
        if (array is not >= 0x10000 || count is not >= 0 or > MaximumMods)
        {
            failureReason = $"mod collection is neither an array nor a valid list (items={array?.ToString("X") ?? "unavailable"}, count={count?.ToString() ?? "unavailable"}).";
            return false;
        }
        return TryReadModArray(array.Value, tables, memory, out mods, out failureReason, count);
    }

    private static bool TryReadModArray(ulong array, NativeLazerGameplayStatsMethodTables tables,
        INativeLazerGameplayClockMemory memory, out IReadOnlyList<string> mods,
        out string? failureReason, int? expectedCount = null)
    {
        mods = [];
        failureReason = null;
        int? length = memory.ReadInt32(array + NativeLazerGameplayStatsLayout.ArrayLength);
        int count = expectedCount ?? length ?? -1;
        if (length is null or < 0 or > MaximumMods || count < 0 || count > length)
        {
            failureReason = $"mod array length/count is invalid (length={length?.ToString() ?? "unavailable"}, count={count}).";
            return false;
        }
        var result = new List<string>(count);
        for (int index = 0; index < count; index++)
        {
            ulong? mod = memory.ReadPointer(array + NativeLazerGameplayStatsLayout.ArrayData + checked((ulong)index * 8));
            ulong? methodTable = mod is >= 0x10000 ? memory.ReadPointer(mod.Value) : null;
            if (methodTable is not { } mt || !tables.SupportedMods.TryGetValue(mt, out string? acronym))
            {
                failureReason = $"mod {index} has an unavailable or unsupported method table ({methodTable?.ToString("X") ?? "unavailable"}).";
                return false;
            }
            result.Add(acronym);
        }
        mods = result.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return true;
    }

    private static bool Object(ulong? address, INativeLazerGameplayClockMemory memory) =>
        address is >= 0x10000 && memory.ReadPointer(address.Value) is >= 0x10000;
}

public sealed record NativeLazerGameplayStatsResponse
{
    public string? ProcessIdentity { get; init; }
    public int? ProcessId { get; init; }
    public string Outcome { get; init; } = NativeLazerGameplayStatsOutcome.Unavailable.ToString();
    public double? CurrentTimeMilliseconds { get; init; }
    public int? CurrentCombo { get; init; }
    public int? HighestCombo { get; init; }
    public int? CountGreat { get; init; }
    public int? CountOk { get; init; }
    public int? CountMeh { get; init; }
    public int? CountMiss { get; init; }
    public int? CountLargeTickMiss { get; init; } = 0;
    public int? CountSliderTailHit { get; init; } = 0;
    public double? Accuracy { get; init; }
    public int? Score { get; init; }
    public string[] Mods { get; init; } = [];
    public DateTimeOffset ObservedAtUtc { get; init; }
    public string? FailureReason { get; init; }
    public double DurationMs { get; init; }
    public long BytesRead { get; init; }
    public int ReadOperations { get; init; }

    internal static NativeLazerGameplayStatsResponse From(NativeLazerGameplayStatsResult result) => new()
    {
        ProcessIdentity = result.Generation.ProcessId > 0 ? result.Generation.Display : null,
        ProcessId = result.Generation.ProcessId > 0 ? result.Generation.ProcessId : null,
        Outcome = result.Outcome.ToString(),
        CurrentTimeMilliseconds = result.CurrentTimeMilliseconds,
        CurrentCombo = result.CurrentCombo,
        HighestCombo = result.HighestCombo,
        CountGreat = result.CountGreat,
        CountOk = result.CountOk,
        CountMeh = result.CountMeh,
        CountMiss = result.CountMiss,
        CountLargeTickMiss = result.CountLargeTickMiss,
        CountSliderTailHit = result.CountSliderTailHit,
        Accuracy = result.Accuracy,
        Score = result.Score,
        Mods = result.Mods.ToArray(),
        ObservedAtUtc = result.ObservedAtUtc,
        FailureReason = result.FailureReason,
        DurationMs = result.DurationMs,
        BytesRead = result.BytesRead,
        ReadOperations = result.ReadOperations
    };

    internal static NativeLazerGameplayStatsResponse Unavailable(
        NativeLazerGenerationIdentity generation, string reason) => new()
    {
        ProcessIdentity = generation.ProcessId > 0 ? generation.Display : null,
        ProcessId = generation.ProcessId > 0 ? generation.ProcessId : null,
        FailureReason = reason,
        ObservedAtUtc = DateTimeOffset.UtcNow
    };
}
