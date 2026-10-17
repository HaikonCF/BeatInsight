using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BeatInsight.Diagnostics.NativeStable;

internal enum NativeStableQRestartPhase
{
    CaptureGenerationAProfile,
    RestartCalibrateA,
    RestartCaptureB,
    RestartCaptureA2,
    Finalize
}

internal enum NativeStableRestartDiscovery { None, Unique, Ambiguous }

internal enum NativeStableInvariantStatus
{
    ReproducedExactly,
    ReproducedStructurally,
    Changed,
    UnknownInsufficientEvidence
}

internal sealed class NativeStableQRestartExperimentSession
{
    public int SchemaVersion { get; set; }
    public NativeStableRestartGeneration GenerationA { get; set; } = null!;
    public NativeStableRestartGeneration? GenerationB { get; set; }
    public NativeStableRootStructuralProfile GenerationAProfile { get; set; } = null!;
    public NativeStableQRestartPhase NextPhase { get; set; }
}

internal sealed class NativeStableRestartGeneration
{
    public int ProcessId { get; set; }
    public DateTimeOffset ProcessStartIdentity { get; set; }
    public NativeStablePersistedMap MapA { get; set; } = null!;
    public NativeStablePersistedMap MapB { get; set; } = null!;
    public ulong XMapA { get; set; }
    public ulong? XMapB { get; set; }
    public ulong[] AllObservedQAddresses { get; set; } = [];
    public NativeStableRestartCandidate[] InitialCandidates { get; set; } = [];
    public int[] SurvivorIndexes { get; set; } = [];
    public NativeStableRestartTransitionPhase[] TransitionPhases { get; set; } = [];
    public NativeStableRestartTimings Timings { get; set; } = new();
    public int PresentationLimit { get; set; } = 32;

    internal NativeStableRestartDiscovery Discovery => SurvivorIndexes.Length switch
    {
        0 => NativeStableRestartDiscovery.None,
        1 => NativeStableRestartDiscovery.Unique,
        _ => NativeStableRestartDiscovery.Ambiguous
    };
}

internal sealed record NativeStableRestartCandidate(
    ulong InitialQAddress,
    NativeStableQCharacterizationCapture CalibrationCapture);

internal sealed record NativeStableRestartTransitionPhase(
    string Phase,
    NativeStableRestartCandidateTransition[] Transitions,
    NativeStableQCharacterizationCapture[] SurvivorCharacterizations,
    long TransitionValidationTicks,
    NativeStableRestartTimings CharacterizationTimings);

internal sealed record NativeStableRestartCandidateTransition(
    int CandidateIndex,
    ulong QAddress,
    ulong? XAddress,
    bool PointerReadable,
    bool FinalObjectReadable,
    int MatchedCoreFields,
    int MatchedOptionalIds,
    int AvailableOptionalIds,
    bool ExpectedMapMatches);

internal sealed class NativeStableRestartTimings
{
    public long GroundTruthObjectDiscoveryTicks { get; set; }
    public long QCandidateSearchTicks { get; set; }
    public long QMetadataTicks { get; set; }
    public long QNeighborhoodTicks { get; set; }
    public long RReferenceSearchTicks { get; set; }
    public long RMetadataTicks { get; set; }
    public long ThirdLevelReferenceSearchTicks { get; set; }
    public long TransitionValidationTicks { get; set; }
    public long StructuralComparisonTicks { get; set; }
    public long TotalTicks { get; set; }

    internal void Add(NativeStableRestartTimings other)
    {
        GroundTruthObjectDiscoveryTicks += other.GroundTruthObjectDiscoveryTicks;
        QCandidateSearchTicks += other.QCandidateSearchTicks;
        QMetadataTicks += other.QMetadataTicks;
        QNeighborhoodTicks += other.QNeighborhoodTicks;
        RReferenceSearchTicks += other.RReferenceSearchTicks;
        RMetadataTicks += other.RMetadataTicks;
        ThirdLevelReferenceSearchTicks += other.ThirdLevelReferenceSearchTicks;
        TransitionValidationTicks += other.TransitionValidationTicks;
        StructuralComparisonTicks += other.StructuralComparisonTicks;
        TotalTicks += other.TotalTicks;
    }
}

internal static class NativeStableQRestartExperiment
{
    internal const int CurrentSchemaVersion = 1;
    internal const int DefaultPresentationLimit = 32;
    internal const string SessionEnvironmentVariable =
        "BEATINSIGHT_NATIVE_STABLE_Q_RESTART_SESSION";
    internal const string ComparisonEnvironmentVariable =
        "BEATINSIGHT_NATIVE_STABLE_Q_RESTART_COMPARISON";
    internal static string DefaultSessionPath => Path.Combine(
        Path.GetTempPath(), "BeatInsight", "NativeStable",
        "native-stable-q-restart-session.json");
    internal static string DefaultComparisonPath => Path.Combine(
        Path.GetTempPath(), "BeatInsight", "NativeStable",
        "native-stable-q-restart-comparison.json");

    private static readonly NativeStableQRestartPhase[] OrderedPhases =
    [
        NativeStableQRestartPhase.CaptureGenerationAProfile,
        NativeStableQRestartPhase.RestartCalibrateA,
        NativeStableQRestartPhase.RestartCaptureB,
        NativeStableQRestartPhase.RestartCaptureA2,
        NativeStableQRestartPhase.Finalize
    ];

    internal static NativeStableQRestartExperimentSession ImportGenerationA(
        NativeStableRootDiagnosticSession c3,
        NativeStableQCharacterizationSession historical,
        NativeStableRootStructuralProfile profile)
    {
        NativeStableRootPhaseHarness.Validate(c3);
        NativeStableQCharacterizationHarness.Validate(historical);
        profile.Validate();
        if (c3.NextPhase != NativeStableRootPhase.Finalize ||
            c3.ProcessId != historical.ProcessId ||
            c3.ProcessStartIdentity != historical.ProcessStartIdentity ||
            historical.NextPhase != NativeStableQPhase.Finalize || historical.Captures.Length != 3)
        {
            throw new InvalidDataException("Historical C.4 characterization is incomplete.");
        }
        NativeStableQCharacterizationCapture a = historical.Captures[0];
        NativeStableQCharacterizationCapture b = historical.Captures[1];
        var generation = new NativeStableRestartGeneration
        {
            ProcessId = historical.ProcessId,
            ProcessStartIdentity = historical.ProcessStartIdentity,
            MapA = historical.MapA,
            MapB = historical.MapB,
            XMapA = a.XAddress,
            XMapB = b.XAddress,
            AllObservedQAddresses = c3.QCandidateAddresses.ToArray(),
            InitialCandidates = [new(historical.QAddress, a)],
            SurvivorIndexes = [0],
            TransitionPhases =
            [
                new("CaptureB", [TransitionFromCapture(0, b)], [b], 0, new()),
                new("CaptureA2", [TransitionFromCapture(0, historical.Captures[2])],
                    [historical.Captures[2]], 0, new())
            ],
            Timings = new(),
            PresentationLimit = DefaultPresentationLimit
        };
        return new()
        {
            SchemaVersion = CurrentSchemaVersion,
            GenerationA = generation,
            GenerationAProfile = profile,
            GenerationB = null,
            NextPhase = NativeStableQRestartPhase.RestartCalibrateA
        };
    }

    /// <summary>
    /// Complete Q search for a newly discovered X. Historical generation data is
    /// deliberately absent from this API, making address reuse impossible here.
    /// </summary>
    internal static IReadOnlyList<nuint> DiscoverGenerationBQCandidates(
        INativeStableMemoryReader memory,
        IReadOnlyList<NativeStableMemoryRegion> regions,
        nuint independentlyDiscoveredX) =>
        NativeStableRootReverseDiscovery.FindAlignedReferences(
            memory, regions, [independentlyDiscoveredX]);

    internal static void ApplyGenerationBCalibration(
        NativeStableQRestartExperimentSession session,
        NativeStableRestartGeneration generationB)
    {
        EnsurePhase(session, NativeStableQRestartPhase.RestartCalibrateA);
        EnsureNewGeneration(session.GenerationA.ProcessStartIdentity,
            generationB.ProcessStartIdentity);
        ValidateGeneration(generationB);
        session.GenerationB = generationB;
        session.NextPhase = NativeStableQRestartPhase.RestartCaptureB;
    }

    internal static void ApplyTransition(
        NativeStableQRestartExperimentSession session,
        NativeStableQRestartPhase phase,
        int processId,
        DateTimeOffset? processStartIdentity,
        NativeStableRestartTransitionPhase result)
    {
        EnsurePhase(session, phase);
        NativeStableRestartGeneration generation = session.GenerationB ??
            throw new InvalidDataException("GenerationB has not been calibrated.");
        NativeStableProcessGenerationGuard.EnsureSame(
            generation.ProcessId, generation.ProcessStartIdentity,
            processId, processStartIdentity);
        string expectedName = phase == NativeStableQRestartPhase.RestartCaptureB
            ? "RestartCaptureB"
            : "RestartCaptureA2";
        if (result.Phase != expectedName ||
            result.Transitions.Length != generation.SurvivorIndexes.Length)
        {
            throw new InvalidDataException("Restart transition candidate set is malformed.");
        }
        var survivors = new List<int>();
        for (int position = 0; position < generation.SurvivorIndexes.Length; position++)
        {
            int index = generation.SurvivorIndexes[position];
            NativeStableRestartCandidateTransition transition = result.Transitions[position];
            if (transition.CandidateIndex != index ||
                transition.QAddress != generation.InitialCandidates[index].InitialQAddress)
            {
                throw new InvalidDataException(
                    "Restart transition replaced, reordered, or added a Q candidate.");
            }
            if (transition.ExpectedMapMatches)
            {
                survivors.Add(index);
            }
        }
        if (result.SurvivorCharacterizations.Length != survivors.Count)
        {
            throw new InvalidDataException("Survivor characterization count is inconsistent.");
        }
        generation.SurvivorIndexes = survivors.ToArray();
        generation.TransitionPhases = generation.TransitionPhases.Append(result).ToArray();
        generation.Timings.TransitionValidationTicks += result.TransitionValidationTicks;
        generation.Timings.Add(result.CharacterizationTimings);
        if (phase == NativeStableQRestartPhase.RestartCaptureB)
        {
            generation.XMapB = result.Transitions
                .Where(item => item.ExpectedMapMatches && item.XAddress is not null)
                .Select(item => item.XAddress!.Value).FirstOrDefault();
            session.NextPhase = NativeStableQRestartPhase.RestartCaptureA2;
        }
        else
        {
            session.NextPhase = NativeStableQRestartPhase.Finalize;
        }
    }

    internal static NativeStableQRestartComparison Finalize(
        NativeStableQRestartExperimentSession session)
    {
        EnsurePhase(session, NativeStableQRestartPhase.Finalize);
        var timer = Stopwatch.StartNew();
        NativeStableRestartGeneration generationB = session.GenerationB!;
        NativeStableRestartCandidateComparison[] candidates = generationB.SurvivorIndexes
            .Select(index => CompareCandidate(
                index,
                generationB.InitialCandidates[index].InitialQAddress,
                session.GenerationAProfile,
                LatestCharacterization(generationB, index)))
            .ToArray();
        NativeStableRestartCandidateComparison? single = candidates.Length == 1
            ? candidates[0]
            : null;
        NativeStableQCharacterizationCapture historical =
            session.GenerationA.InitialCandidates[0].CalibrationCapture;
        NativeStableQCharacterizationCapture? current = single?.CurrentCapture;
        NativeStableInvariantComparison[] exact =
        [
            Exact("QAbsoluteAddressSame", historical.QAddress,
                current?.QAddress),
            Exact("XMapAAddressSame", session.GenerationA.XMapA,
                generationB.XMapA),
            Exact("XMapBAddressSame", session.GenerationA.XMapB,
                generationB.XMapB),
            ExactSet("RAbsoluteSetSame",
                historical.ReferencesToQ.Select(item => item.AbsoluteAddress),
                current?.ReferencesToQ.Select(item => item.AbsoluteAddress)),
            ExactSet("TAbsoluteSetSame",
                historical.ThirdLevelReferences.Select(item => item.AbsoluteAddress),
                current?.ThirdLevelReferences.Select(item => item.AbsoluteAddress)),
            Exact("QAllocationBaseSame", historical.QMetadata.AllocationBase,
                current?.QMetadata.AllocationBase)
        ];
        timer.Stop();
        generationB.Timings.StructuralComparisonTicks = timer.Elapsed.Ticks;
        return new(
            session.GenerationA.ProcessId,
            generationB.ProcessId,
            session.GenerationA.ProcessStartIdentity,
            generationB.ProcessStartIdentity,
            generationB.Discovery,
            generationB.InitialCandidates.Length,
            generationB.SurvivorIndexes.Length,
            exact,
            candidates,
            timer.Elapsed.Ticks,
            MemoryScanPerformed: false);
    }

    internal static void EnsureNewGeneration(
        DateTimeOffset generationAStart,
        DateTimeOffset generationBStart)
    {
        if (generationAStart == generationBStart)
        {
            throw new InvalidOperationException(
                "restart attendu mais même génération de processus");
        }
    }

    internal static void EnsurePhase(
        NativeStableQRestartExperimentSession session,
        NativeStableQRestartPhase phase)
    {
        Validate(session);
        if (session.NextPhase != phase)
        {
            throw new InvalidDataException(
                $"Phase order mismatch: expected {session.NextPhase}, requested {phase}.");
        }
    }

    internal static void Validate(NativeStableQRestartExperimentSession session)
    {
        if (session.SchemaVersion != CurrentSchemaVersion || session.GenerationA is null ||
            session.GenerationAProfile is null)
        {
            throw new InvalidDataException("Restart experiment session is incomplete.");
        }
        ValidateGeneration(session.GenerationA);
        session.GenerationAProfile.Validate();
        int phaseIndex = Array.IndexOf(OrderedPhases, session.NextPhase);
        if (phaseIndex < 1)
        {
            throw new InvalidDataException("Restart experiment phase history is malformed.");
        }
        if (phaseIndex >= 2 && session.GenerationB is null)
        {
            throw new InvalidDataException("GenerationB is missing.");
        }
        if (session.GenerationB is not null)
        {
            ValidateGeneration(session.GenerationB);
            EnsureNewGeneration(session.GenerationA.ProcessStartIdentity,
                session.GenerationB.ProcessStartIdentity);
            int expectedTransitions = phaseIndex switch
            {
                2 => 0,
                3 => 1,
                4 => 2,
                _ => 0
            };
            if (session.GenerationB.TransitionPhases.Length != expectedTransitions)
            {
                throw new InvalidDataException("GenerationB transition history is malformed.");
            }
        }
    }

    internal static void ValidateGeneration(NativeStableRestartGeneration generation)
    {
        if (generation.ProcessId <= 0 || generation.ProcessStartIdentity == default ||
            generation.MapA is null || generation.MapB is null || generation.XMapA == 0 ||
            generation.AllObservedQAddresses is null ||
            generation.InitialCandidates is null || generation.SurvivorIndexes is null ||
            generation.TransitionPhases is null || generation.Timings is null ||
            generation.SurvivorIndexes.Distinct().Count() != generation.SurvivorIndexes.Length ||
            generation.SurvivorIndexes.Any(index =>
                index < 0 || index >= generation.InitialCandidates.Length))
        {
            throw new InvalidDataException("Restart generation data is malformed.");
        }
    }

    private static NativeStableRestartCandidateComparison CompareCandidate(
        int candidateIndex,
        ulong qAddress,
        NativeStableRootStructuralProfile historical,
        NativeStableQCharacterizationCapture current)
    {
        NativeStableRootStructuralProfile profile =
            NativeStableRootStructuralProfile.FromCapture(current);
        NativeStableInvariantComparison[] comparisons = NativeStableStructuralComparator.Compare(
            historical, profile);
        return new(candidateIndex, qAddress, current, comparisons);
    }

    private static NativeStableQCharacterizationCapture LatestCharacterization(
        NativeStableRestartGeneration generation,
        int candidateIndex)
    {
        foreach (NativeStableRestartTransitionPhase phase in generation.TransitionPhases.Reverse())
        {
            NativeStableRestartCandidateTransition? transition = phase.Transitions
                .SingleOrDefault(item => item.CandidateIndex == candidateIndex &&
                    item.ExpectedMapMatches);
            if (transition is not null)
            {
                return phase.SurvivorCharacterizations.Single(capture =>
                    capture.QAddress == transition.QAddress);
            }
        }
        return generation.InitialCandidates[candidateIndex].CalibrationCapture;
    }

    private static NativeStableRestartCandidateTransition TransitionFromCapture(
        int index,
        NativeStableQCharacterizationCapture capture) => new(
            index, capture.QAddress, capture.XAddress, true, true,
            capture.MatchedCoreFields, capture.MatchedOptionalIds,
            capture.AvailableOptionalIds, capture.ExpectedMapMatches);

    private static NativeStableInvariantComparison Exact<T>(
        string name, T expected, T? actual) => actual is null
        ? new(name, NativeStableInvariantStatus.UnknownInsufficientEvidence,
            "GenerationB evidence unavailable.")
        : EqualityComparer<T>.Default.Equals(expected, actual)
            ? new(name, NativeStableInvariantStatus.ReproducedExactly, "Equal.")
            : new(name, NativeStableInvariantStatus.Changed, "Different.");

    private static NativeStableInvariantComparison ExactSet(
        string name, IEnumerable<ulong> expected, IEnumerable<ulong>? actual)
    {
        if (actual is null)
        {
            return new(name, NativeStableInvariantStatus.UnknownInsufficientEvidence,
                "GenerationB evidence unavailable.");
        }
        bool equal = expected.OrderBy(value => value)
            .SequenceEqual(actual.OrderBy(value => value));
        return new(name, equal ? NativeStableInvariantStatus.ReproducedExactly :
            NativeStableInvariantStatus.Changed, equal ? "Equal set." : "Different set.");
    }
}

internal static class NativeStableStructuralComparator
{
    internal static NativeStableInvariantComparison[] Compare(
        NativeStableRootStructuralProfile historical,
        NativeStableRootStructuralProfile current)
    {
        historical.Validate();
        current.Validate();
        var results = new List<NativeStableInvariantComparison>
        {
            CompareValue("Q.Backing", historical.Q.Backing, current.Q.Backing),
            CompareValue("Q.Protection", historical.Q.Protection, current.Q.Protection),
            CompareValue("Q.Type", historical.Q.Type, current.Q.Type),
            CompareValue("Q.Alignment", historical.Q.Alignment, current.Q.Alignment),
            CompareValue("Q.RegionSize", historical.Q.RegionSize, current.Q.RegionSize),
            CompareValue("Q.OffsetFromRegion", historical.Q.OffsetFromRegionBase,
                current.Q.OffsetFromRegionBase),
            CompareValue("Q.OffsetFromAllocation", historical.Q.OffsetFromAllocationBase,
                current.Q.OffsetFromAllocationBase),
            CompareValue("R.Count", historical.References.Length, current.References.Length),
            CompareValue("R.ClusterCount", historical.Clusters.Length, current.Clusters.Length),
            CompareSequence("R.ClusterCardinality",
                historical.Clusters.Select(item => item.Count),
                current.Clusters.Select(item => item.Count)),
            CompareValue("T.Count", historical.ThirdLevel.Length, current.ThirdLevel.Length)
        };
        results.Add(CompareNeighborhood(historical.QNeighborhood, current.QNeighborhood));
        results.Add(CompareSequence("R.BackingDistribution",
            historical.References.GroupBy(item => item.Address.Backing)
                .OrderBy(group => group.Key).Select(group => $"{group.Key}:{group.Count()}"),
            current.References.GroupBy(item => item.Address.Backing)
                .OrderBy(group => group.Key).Select(group => $"{group.Key}:{group.Count()}")));
        results.Add(CompareSequence("T.ModuleMotifs",
            historical.ThirdLevel.Select(item =>
                $"{item.T.Backing}:{item.T.ModuleName}:{item.T.OffsetFromModuleBase}"),
            current.ThirdLevel.Select(item =>
                $"{item.T.Backing}:{item.T.ModuleName}:{item.T.OffsetFromModuleBase}")));
        return results.ToArray();
    }

    internal static NativeStableInvariantComparison CompareNeighborhood(
        NativeStableStructuralFingerprint historical,
        NativeStableStructuralFingerprint current)
    {
        if (historical.Cells.Length == 0 || current.Cells.Length == 0)
        {
            return new("Q.Neighborhood", NativeStableInvariantStatus.UnknownInsufficientEvidence,
                "No comparable cells.");
        }
        Dictionary<int, NativeStableStructuralCell> currentByOffset =
            current.Cells.ToDictionary(cell => cell.RelativeOffset);
        NativeStableStructuralCell[] comparable = historical.Cells
            .Where(cell => currentByOffset.ContainsKey(cell.RelativeOffset)).ToArray();
        if (comparable.Length == 0)
        {
            return new("Q.Neighborhood", NativeStableInvariantStatus.UnknownInsufficientEvidence,
                "No shared offsets.");
        }
        int matching = comparable.Count(cell =>
            currentByOffset[cell.RelativeOffset].Kind == cell.Kind);
        double similarity = matching / (double)comparable.Length;
        bool moduleMotifs = comparable
            .Where(cell => cell.Kind == NativeStableStructuralValueKind.ModulePointerCandidate)
            .All(cell =>
            {
                NativeStableStructuralCell other = currentByOffset[cell.RelativeOffset];
                return other.Kind == cell.Kind &&
                    other.PointerTarget?.ModuleName == cell.PointerTarget?.ModuleName &&
                    other.PointerTarget?.OffsetFromModuleBase ==
                        cell.PointerTarget?.OffsetFromModuleBase;
            });
        NativeStableInvariantStatus status = similarity == 1 && moduleMotifs
            ? NativeStableInvariantStatus.ReproducedExactly
            : similarity >= 0.80
                ? NativeStableInvariantStatus.ReproducedStructurally
                : NativeStableInvariantStatus.Changed;
        return new("Q.Neighborhood", status,
            $"CategoricalSimilarity={similarity:P1}; StableModuleMotifs={moduleMotifs}");
    }

    private static NativeStableInvariantComparison CompareValue<T>(
        string name, T first, T second) => EqualityComparer<T>.Default.Equals(first, second)
        ? new(name, NativeStableInvariantStatus.ReproducedExactly, "Equal.")
        : new(name, NativeStableInvariantStatus.Changed, "Different.");

    private static NativeStableInvariantComparison CompareSequence<T>(
        string name, IEnumerable<T> first, IEnumerable<T> second) => first.SequenceEqual(second)
        ? new(name, NativeStableInvariantStatus.ReproducedExactly, "Equal sequence.")
        : new(name, NativeStableInvariantStatus.Changed, "Different sequence.");
}

internal sealed record NativeStableInvariantComparison(
    string Name,
    NativeStableInvariantStatus Status,
    string Detail);

internal sealed record NativeStableRestartCandidateComparison(
    int CandidateIndex,
    ulong QAddress,
    NativeStableQCharacterizationCapture CurrentCapture,
    NativeStableInvariantComparison[] StructuralInvariants);

internal sealed record NativeStableQRestartComparison(
    int GenerationAProcessId,
    int GenerationBProcessId,
    DateTimeOffset GenerationAStart,
    DateTimeOffset GenerationBStart,
    NativeStableRestartDiscovery Discovery,
    int InitialQCandidateCount,
    int SurvivingQCandidateCount,
    NativeStableInvariantComparison[] ExactInvariants,
    NativeStableRestartCandidateComparison[] CandidateComparisons,
    long StructuralComparisonTicks,
    bool MemoryScanPerformed);

internal sealed class NativeStableQRestartStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    internal NativeStableQRestartExperimentSession Load(string path) => LoadValue(
        path,
        json => JsonSerializer.Deserialize<NativeStableQRestartExperimentSession>(json, Options),
        NativeStableQRestartExperiment.Validate,
        "restart experiment session");

    internal void Save(string path, NativeStableQRestartExperimentSession session)
    {
        NativeStableQRestartExperiment.Validate(session);
        SaveJson(path, JsonSerializer.Serialize(session, Options));
    }

    internal NativeStableQRestartComparison LoadComparison(string path) => LoadValue(
        path,
        json => JsonSerializer.Deserialize<NativeStableQRestartComparison>(json, Options),
        comparison =>
        {
            if (comparison.ExactInvariants is null || comparison.CandidateComparisons is null)
            {
                throw new InvalidDataException("restart comparison is malformed.");
            }
        },
        "restart comparison");

    internal void SaveComparison(string path, NativeStableQRestartComparison comparison) =>
        SaveJson(path, JsonSerializer.Serialize(comparison, Options));

    private static T LoadValue<T>(
        string path, Func<string, T?> deserialize, Action<T> validate, string description)
        where T : class
    {
        try
        {
            if (!File.Exists(path))
            {
                throw new InvalidDataException($"{description} is missing: {path}");
            }
            T value = deserialize(File.ReadAllText(path)) ??
                throw new InvalidDataException($"{description} is empty.");
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
