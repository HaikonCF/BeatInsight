using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BeatInsight.Diagnostics.NativeStable;

internal enum NativeStableAutonomousQPhase
{
    CaptureCA, CaptureCB, CaptureCA2, FinalizeC,
    CaptureDA, CaptureDB, CaptureDA2, FinalizeD,
    CaptureEA, CaptureEB, CaptureEA2, FinalizeE,
    FinalizeAll
}

internal sealed class NativeStableAutonomousQValidationSession
{
    public int SchemaVersion { get; set; }
    public NativeStableAutonomousQProfile Profile { get; set; } = null!;
    public DateTimeOffset[] HistoricalProcessStarts { get; set; } = [];
    public NativeStableAutonomousGenerationValidation[] Generations { get; set; } = [];
    public NativeStableAutonomousQPhase NextPhase { get; set; }
}

internal sealed class NativeStableAutonomousGenerationValidation
{
    public string Label { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public DateTimeOffset ProcessStartIdentity { get; set; }
    public NativeStableAutonomousQLocatorResult StartupResult { get; set; } = null!;
    public NativeStableAutonomousHarnessTruth[] TruthA { get; set; } = [];
    public NativeStableAutonomousHarnessTruth[] TruthB { get; set; } = [];
    public NativeStableAutonomousHarnessTruth[] TruthA2 { get; set; } = [];
    public bool Finalized { get; set; }
}

internal sealed record NativeStableAutonomousHarnessTruth(
    ulong QAddress,
    ulong? XAddress,
    bool PointerReadable,
    bool FinalObjectReadable,
    int MatchedCoreFields,
    int MatchedOptionalIds,
    int AvailableOptionalIds,
    bool ExpectedMapMatches);

internal sealed record NativeStableAutonomousGenerationSummary(
    string Label,
    int ProcessId,
    DateTimeOffset ProcessStartIdentity,
    NativeStableAutonomousQDiscovery StartupDiscovery,
    int StartupCandidateCount,
    ulong? SelectedQ,
    int SurvivingAfterB,
    int SurvivingAfterA2,
    bool StrongTransitionValidation,
    long StartupLocatorTicks);

internal sealed record NativeStableAutonomousQComparison(
    NativeStableAutonomousGenerationSummary[] Generations,
    NativeStableInvariantComparison[] CrossGenerationInvariants,
    bool MemoryReadPerformed);

internal static class NativeStableAutonomousQHarness
{
    internal const int CurrentSchemaVersion = 1;
    internal const string SessionEnvironmentVariable =
        "BEATINSIGHT_NATIVE_STABLE_AUTONOMOUS_Q_SESSION";
    internal const string ProfileEnvironmentVariable =
        "BEATINSIGHT_NATIVE_STABLE_AUTONOMOUS_Q_PROFILE";
    internal const string ComparisonEnvironmentVariable =
        "BEATINSIGHT_NATIVE_STABLE_AUTONOMOUS_Q_COMPARISON";
    internal static string DefaultSessionPath => Path.Combine(
        Path.GetTempPath(), "BeatInsight", "NativeStable",
        "native-stable-autonomous-q-validation-session.json");
    internal static string DefaultProfilePath => Path.Combine(
        Path.GetTempPath(), "BeatInsight", "NativeStable",
        "native-stable-autonomous-q-profile.json");
    internal static string DefaultComparisonPath => Path.Combine(
        Path.GetTempPath(), "BeatInsight", "NativeStable",
        "native-stable-autonomous-q-comparison.json");

    private static readonly NativeStableAutonomousQPhase[] Ordered =
        Enum.GetValues<NativeStableAutonomousQPhase>();

    internal static NativeStableAutonomousQValidationSession Create(
        NativeStableAutonomousQProfile profile,
        params DateTimeOffset[] historicalStarts)
    {
        profile.Validate();
        return new()
        {
            SchemaVersion = CurrentSchemaVersion,
            Profile = profile,
            HistoricalProcessStarts = historicalStarts.Distinct().ToArray(),
            Generations = [],
            NextPhase = NativeStableAutonomousQPhase.CaptureCA
        };
    }

    internal static void ApplyStartup(
        NativeStableAutonomousQValidationSession session,
        string label,
        NativeStableAutonomousQLocatorResult result,
        NativeStableAutonomousHarnessTruth[] truth)
    {
        NativeStableAutonomousQPhase expected = label switch
        {
            "C" => NativeStableAutonomousQPhase.CaptureCA,
            "D" => NativeStableAutonomousQPhase.CaptureDA,
            "E" => NativeStableAutonomousQPhase.CaptureEA,
            _ => throw new ArgumentOutOfRangeException(nameof(label))
        };
        EnsurePhase(session, expected);
        if (session.HistoricalProcessStarts.Contains(result.ProcessStartIdentity) ||
            session.Generations.Any(item =>
                item.ProcessStartIdentity == result.ProcessStartIdentity))
        {
            throw new InvalidOperationException(
                "A fresh osu! process generation is required for this label.");
        }
        if (truth.Select(item => item.QAddress).Except(
                result.Candidates.Select(item => item.QAddress)).Any())
        {
            throw new InvalidDataException("Harness truth contains an unknown locator candidate.");
        }
        session.Generations = session.Generations.Append(new()
        {
            Label = label,
            ProcessId = result.ProcessId,
            ProcessStartIdentity = result.ProcessStartIdentity,
            StartupResult = result,
            TruthA = truth
        }).ToArray();
        session.NextPhase = Next(expected);
    }

    internal static void ApplyTransition(
        NativeStableAutonomousQValidationSession session,
        string label,
        string map,
        int processId,
        DateTimeOffset? processStartIdentity,
        NativeStableAutonomousHarnessTruth[] truth)
    {
        NativeStableAutonomousQPhase expected = (label, map) switch
        {
            ("C", "B") => NativeStableAutonomousQPhase.CaptureCB,
            ("C", "A2") => NativeStableAutonomousQPhase.CaptureCA2,
            ("D", "B") => NativeStableAutonomousQPhase.CaptureDB,
            ("D", "A2") => NativeStableAutonomousQPhase.CaptureDA2,
            ("E", "B") => NativeStableAutonomousQPhase.CaptureEB,
            ("E", "A2") => NativeStableAutonomousQPhase.CaptureEA2,
            _ => throw new ArgumentOutOfRangeException(nameof(map))
        };
        EnsurePhase(session, expected);
        NativeStableAutonomousGenerationValidation generation = session.Generations
            .Single(item => item.Label == label);
        NativeStableProcessGenerationGuard.EnsureSame(
            generation.ProcessId, generation.ProcessStartIdentity,
            processId, processStartIdentity);
        ulong[] allowed = map == "B"
            ? generation.TruthA.Where(item => item.ExpectedMapMatches)
                .Select(item => item.QAddress).ToArray()
            : generation.TruthB.Where(item => item.ExpectedMapMatches)
                .Select(item => item.QAddress).ToArray();
        if (truth.Select(item => item.QAddress).Except(allowed).Any())
        {
            throw new InvalidDataException("Transition added a candidate not surviving prior truth.");
        }
        if (map == "B") generation.TruthB = truth;
        else generation.TruthA2 = truth;
        session.NextPhase = Next(expected);
    }

    internal static NativeStableAutonomousGenerationSummary FinalizeGeneration(
        NativeStableAutonomousQValidationSession session,
        string label)
    {
        NativeStableAutonomousQPhase expected = label switch
        {
            "C" => NativeStableAutonomousQPhase.FinalizeC,
            "D" => NativeStableAutonomousQPhase.FinalizeD,
            "E" => NativeStableAutonomousQPhase.FinalizeE,
            _ => throw new ArgumentOutOfRangeException(nameof(label))
        };
        EnsurePhase(session, expected);
        NativeStableAutonomousGenerationValidation generation = session.Generations
            .Single(item => item.Label == label);
        generation.Finalized = true;
        session.NextPhase = Next(expected);
        return Summarize(generation);
    }

    internal static NativeStableAutonomousQComparison FinalizeAll(
        NativeStableAutonomousQValidationSession session)
    {
        EnsurePhase(session, NativeStableAutonomousQPhase.FinalizeAll);
        NativeStableAutonomousGenerationSummary[] summaries =
            session.Generations.Select(Summarize).ToArray();
        NativeStableInvariantComparison[] invariants = summaries.Length < 2
            ? [new("GenerationCount", NativeStableInvariantStatus.UnknownInsufficientEvidence,
                "Fewer than two autonomous generations.")]
            :
            [
                CompareSequence("RelativeAllocationOffsets",
                    session.Generations.Select(generation => SelectedCandidate(generation)?
                        .Metadata.OffsetFromAllocationBase)),
                CompareSequence("RelativeRegionOffsets",
                    session.Generations.Select(generation => SelectedCandidate(generation)?
                        .Metadata.OffsetFromRegionBase)),
                CompareSequence("Backing",
                    session.Generations.Select(generation => SelectedCandidate(generation)?
                        .Metadata.Backing)),
                CompareSequence("Protection",
                    session.Generations.Select(generation => SelectedCandidate(generation)?
                        .Metadata.Protection)),
                CompareSequence("StartupDiscovery",
                    summaries.Select(summary => summary.StartupDiscovery)),
                CompareSequence("TransitionValidation",
                    summaries.Select(summary => summary.StrongTransitionValidation))
            ];
        return new(summaries, invariants, MemoryReadPerformed: false);
    }

    internal static void EnsurePhase(
        NativeStableAutonomousQValidationSession session,
        NativeStableAutonomousQPhase phase)
    {
        Validate(session);
        if (session.NextPhase != phase)
        {
            throw new InvalidDataException(
                $"Phase order mismatch: expected {session.NextPhase}, requested {phase}.");
        }
    }

    internal static void Validate(NativeStableAutonomousQValidationSession session)
    {
        if (session.SchemaVersion != CurrentSchemaVersion || session.Profile is null ||
            session.HistoricalProcessStarts is null || session.Generations is null ||
            !Ordered.Contains(session.NextPhase))
        {
            throw new InvalidDataException("Autonomous Q validation session is malformed.");
        }
        session.Profile.Validate();
        if (session.Generations.Select(item => item.Label).Distinct().Count() !=
            session.Generations.Length || session.Generations.Any(item =>
                item.StartupResult is null || item.ProcessId <= 0 ||
                item.ProcessStartIdentity == default))
        {
            throw new InvalidDataException("Autonomous generation history is malformed.");
        }
    }

    private static NativeStableAutonomousGenerationSummary Summarize(
        NativeStableAutonomousGenerationValidation generation)
    {
        int afterB = generation.TruthB.Count(item => item.ExpectedMapMatches);
        int afterA2 = generation.TruthA2.Count(item => item.ExpectedMapMatches);
        bool strong = afterA2 > 0 && generation.TruthA2
            .Where(item => item.ExpectedMapMatches)
            .All(a2 => generation.TruthB.Any(b => b.QAddress == a2.QAddress &&
                    b.ExpectedMapMatches) &&
                generation.TruthA.Any(a => a.QAddress == a2.QAddress &&
                    a.ExpectedMapMatches));
        return new(
            generation.Label, generation.ProcessId, generation.ProcessStartIdentity,
            generation.StartupResult.Discovery,
            generation.StartupResult.Candidates.Length,
            generation.StartupResult.SelectedQAddress,
            afterB, afterA2, strong,
            generation.StartupResult.Timings.TotalTicks);
    }

    private static NativeStableAutonomousQCandidate? SelectedCandidate(
        NativeStableAutonomousGenerationValidation generation) =>
        generation.StartupResult.SelectedQAddress is ulong selected
            ? generation.StartupResult.Candidates.SingleOrDefault(item =>
                item.QAddress == selected)
            : null;

    private static NativeStableInvariantComparison CompareSequence<T>(
        string name, IEnumerable<T> values)
    {
        T[] sequence = values.ToArray();
        if (sequence.Length < 2 || sequence.Any(value => value is null))
        {
            return new(name, NativeStableInvariantStatus.UnknownInsufficientEvidence,
                "Insufficient comparable generations.");
        }
        bool same = sequence.Skip(1).All(value =>
            EqualityComparer<T>.Default.Equals(sequence[0], value));
        return new(name, same ? NativeStableInvariantStatus.ReproducedExactly :
            NativeStableInvariantStatus.Changed,
            same ? "Equal across generations." : "Varies across generations.");
    }

    private static NativeStableAutonomousQPhase Next(
        NativeStableAutonomousQPhase phase) => phase switch
    {
        NativeStableAutonomousQPhase.CaptureCA => NativeStableAutonomousQPhase.CaptureCB,
        NativeStableAutonomousQPhase.CaptureCB => NativeStableAutonomousQPhase.CaptureCA2,
        NativeStableAutonomousQPhase.CaptureCA2 => NativeStableAutonomousQPhase.FinalizeC,
        NativeStableAutonomousQPhase.FinalizeC => NativeStableAutonomousQPhase.CaptureDA,
        NativeStableAutonomousQPhase.CaptureDA => NativeStableAutonomousQPhase.CaptureDB,
        NativeStableAutonomousQPhase.CaptureDB => NativeStableAutonomousQPhase.CaptureDA2,
        NativeStableAutonomousQPhase.CaptureDA2 => NativeStableAutonomousQPhase.FinalizeD,
        NativeStableAutonomousQPhase.FinalizeD => NativeStableAutonomousQPhase.CaptureEA,
        NativeStableAutonomousQPhase.CaptureEA => NativeStableAutonomousQPhase.CaptureEB,
        NativeStableAutonomousQPhase.CaptureEB => NativeStableAutonomousQPhase.CaptureEA2,
        NativeStableAutonomousQPhase.CaptureEA2 => NativeStableAutonomousQPhase.FinalizeE,
        NativeStableAutonomousQPhase.FinalizeE => NativeStableAutonomousQPhase.FinalizeAll,
        _ => throw new InvalidDataException("FINALIZE_ALL has no next phase.")
    };
}

internal sealed class NativeStableAutonomousQStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    internal NativeStableAutonomousQValidationSession Load(string path) => LoadValue(
        path,
        json => JsonSerializer.Deserialize<NativeStableAutonomousQValidationSession>(json, Options),
        NativeStableAutonomousQHarness.Validate,
        "autonomous Q session");

    internal void Save(string path, NativeStableAutonomousQValidationSession session)
    {
        NativeStableAutonomousQHarness.Validate(session);
        SaveJson(path, JsonSerializer.Serialize(session, Options));
    }

    internal NativeStableAutonomousQProfile LoadProfile(string path) => LoadValue(
        path,
        json => JsonSerializer.Deserialize<NativeStableAutonomousQProfile>(json, Options),
        profile => profile.Validate(),
        "autonomous Q profile");

    internal void SaveProfile(string path, NativeStableAutonomousQProfile profile)
    {
        profile.Validate();
        SaveJson(path, JsonSerializer.Serialize(profile, Options));
    }

    internal void SaveComparison(string path, NativeStableAutonomousQComparison comparison) =>
        SaveJson(path, JsonSerializer.Serialize(comparison, Options));

    private static T LoadValue<T>(
        string path, Func<string, T?> deserialize, Action<T> validate, string description)
        where T : class
    {
        try
        {
            if (!File.Exists(path)) throw new InvalidDataException($"{description} missing: {path}");
            T value = deserialize(File.ReadAllText(path)) ??
                throw new InvalidDataException($"{description} empty.");
            validate(value);
            return value;
        }
        catch (InvalidDataException) { throw; }
        catch (Exception exception) when (exception is JsonException or IOException or
            UnauthorizedAccessException or NotSupportedException)
        {
            throw new InvalidDataException($"{description} could not be loaded.", exception);
        }
    }

    private static void SaveJson(string path, string json)
    {
        string full = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(full) ??
            throw new InvalidDataException("Diagnostic path has no directory.");
        Directory.CreateDirectory(directory);
        string temporary = full + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, full, overwrite: true);
    }
}
