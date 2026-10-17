using BeatInsight.Diagnostics.NativeStable;
using System.IO;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Diagnostics;

/// <summary>
/// Explicit opt-in live diagnostic. Normal automated runs do nothing unless
/// both multi-map paths or the legacy single-map path are supplied.
/// </summary>
public sealed class NativeStablePocManualLiveTests
{
    private readonly ITestOutputHelper output;

    public NativeStablePocManualLiveTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact]
    [Trait("Category", "ManualLive")]
    public void CaptureKnownSelectedMap_WhenExplicitlyEnabled()
    {
        string? mapA = Environment.GetEnvironmentVariable(
            "BEATINSIGHT_NATIVE_STABLE_POC_MAP_A");
        string? mapB = Environment.GetEnvironmentVariable(
            "BEATINSIGHT_NATIVE_STABLE_POC_MAP_B");
        string? expected = Environment.GetEnvironmentVariable(
            "BEATINSIGHT_NATIVE_STABLE_POC_EXPECTED");
        string? legacy = Environment.GetEnvironmentVariable(
            "BEATINSIGHT_NATIVE_STABLE_POC_MAP");

        IReadOnlyList<NativeStableKnownMapInput> inputs;
        if (!string.IsNullOrWhiteSpace(mapA) && !string.IsNullOrWhiteSpace(mapB))
        {
            inputs =
            [
                NativeStableKnownMapInput.FromPath("A", mapA),
                NativeStableKnownMapInput.FromPath("B", mapB)
            ];
        }
        else if (!string.IsNullOrWhiteSpace(legacy))
        {
            inputs = [NativeStableKnownMapInput.FromPath("MAP", legacy)];
            expected = "MAP";
        }
        else
        {
            output.WriteLine(
                "Manual native-stable POC skipped: supply MAP_A and MAP_B, or legacy MAP.");
            return;
        }

        NativeStableProbeResult result = NativeStablePoc.CaptureKnownMaps(inputs, expected);

        output.WriteLine("=== NATIVE STABLE MULTI-MAP LIFETIME CAPTURE ===");
        output.WriteLine($"CaptureId={result.CaptureId}");
        output.WriteLine($"ProcessId={result.ProcessId?.ToString() ?? "unavailable"}");
        output.WriteLine($"ObservedAtUtc={result.ObservedAtUtc:O}");
        output.WriteLine($"AttachmentStatus={result.AttachmentStatus}");
        output.WriteLine($"ExpectedSelectedMap={result.ExpectedSelectedMapLabel ?? "unspecified"}");

        foreach (NativeStableKnownMapCapture map in result.KnownMaps)
        {
            output.WriteLine(string.Empty);
            output.WriteLine($"Map {map.MapLabel}");
            output.WriteLine(new string('-', map.MapLabel.Length + 4));
            output.WriteLine($"KnownMapPath={map.KnownMapPath ?? "not supplied"}");
            output.WriteLine($"KnownChecksum={map.KnownChecksum}");
            output.WriteLine($"Observed={map.Observed}");
            output.WriteLine($"Status={map.Status}");

            WriteOccurrenceGroup(map, NativeStableKnownSignalKind.Checksum, "Checksum");
            WriteOccurrenceGroup(map, NativeStableKnownSignalKind.Filename, "Filename");
            WriteOccurrenceGroup(map, NativeStableKnownSignalKind.Folder, "Folder");

            output.WriteLine($"CorrelationCandidateCount={map.CorrelationCandidates.Count}");
            output.WriteLine($"Plus0x50CandidateCount={map.Plus0x50CandidateCount}");
            for (int index = 0; index < map.CorrelationCandidates.Count; index++)
            {
                WriteCandidate(index, map.CorrelationCandidates[index]);
            }
        }

        output.WriteLine(string.Empty);
        output.WriteLine("CrossMap:");
        bool bothObserved = result.KnownMaps.Count >= 2 &&
            result.KnownMaps.All(map => map.Observed);
        output.WriteLine($"BothObserved={bothObserved}");
        if (result.CrossMapComparisons.Count == 0)
        {
            output.WriteLine("  comparisons=none");
        }

        foreach (NativeStableCrossMapComparison comparison in result.CrossMapComparisons)
        {
            output.WriteLine(
                $"  {comparison.FirstMapLabel}#{comparison.FirstCandidateIndex + 1} -> {comparison.SecondMapLabel}#{comparison.SecondCandidateIndex + 1}; SameRegion={comparison.SameRegion}; ChecksumAddressDistance={SignedHex(comparison.ChecksumAddressDistance)}; FilenameAddressDistance={SignedHex(comparison.FilenameAddressDistance)}");
        }

        output.WriteLine(string.Empty);
        output.WriteLine("=== NATIVE STABLE OWNER / REFERENCE DISCOVERY ===");
        output.WriteLine($"CaptureId={result.CaptureId}");
        output.WriteLine($"ProcessId={result.ProcessId?.ToString() ?? "unavailable"}");
        output.WriteLine($"ObservedAtUtc={result.ObservedAtUtc:O}");
        output.WriteLine($"ExpectedSelectedMap={result.ExpectedSelectedMapLabel ?? "unspecified"}");
        WriteReferences("DirectReferences", result.ReferenceDiscovery.DirectReferences);
        WriteReferences(
            "CandidateNeighborhoodReferences",
            result.ReferenceDiscovery.NeighborhoodReferences);
        WriteIntermediateReferences(result.ReferenceDiscovery.IntermediateReferences);
        output.WriteLine(
            $"FilteredStringPayloadPointerLikeValues={result.ReferenceDiscovery.FilteredStringPayloadPointerLikeValues}");
        output.WriteLine("SwitchingReferenceCandidates=external A1/B/A2 comparison required");
        output.WriteLine("StaticReferences=external A1/B/A2 comparison required");

        output.WriteLine(string.Empty);
        output.WriteLine("TimingsMilliseconds:");
        output.WriteLine($"  ProcessDiscovery={result.Timings.ProcessDiscovery.TotalMilliseconds:F1}");
        output.WriteLine($"  Attachment={result.Timings.Attachment.TotalMilliseconds:F1}");
        output.WriteLine($"  RegionEnumeration={result.Timings.RegionEnumeration.TotalMilliseconds:F1}");
        output.WriteLine($"  KnownStringScan={result.Timings.KnownStringScan.TotalMilliseconds:F1}");
        output.WriteLine($"  Correlation={result.Timings.Correlation.TotalMilliseconds:F1}");
        output.WriteLine($"  TargetedInspection={result.Timings.TargetedInspection.TotalMilliseconds:F1}");
        output.WriteLine($"  ReverseReferenceSearch={result.Timings.ReverseReferenceSearch.TotalMilliseconds:F1}");
        output.WriteLine($"  NeighborhoodAnalysis={result.Timings.NeighborhoodAnalysis.TotalMilliseconds:F1}");
        output.WriteLine($"  Total={result.Timings.Total.TotalMilliseconds:F1}");

        Assert.Equal(NativeStableAttachmentStatus.AttachedReadOnly, result.AttachmentStatus);
    }

    [Fact]
    [Trait("Category", "ManualLive")]
    public void ValidateSelectionSlot_A1_B1_A2_B2_A3_WhenExplicitlyEnabled()
    {
        string? enabled = Environment.GetEnvironmentVariable(
            "BEATINSIGHT_NATIVE_STABLE_SLOT_VALIDATION");
        string? mapA = Environment.GetEnvironmentVariable(
            "BEATINSIGHT_NATIVE_STABLE_POC_MAP_A");
        string? mapB = Environment.GetEnvironmentVariable(
            "BEATINSIGHT_NATIVE_STABLE_POC_MAP_B");
        if (!string.Equals(enabled, "1", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(mapA) || string.IsNullOrWhiteSpace(mapB))
        {
            output.WriteLine(
                "Manual selection-slot validation skipped: set SLOT_VALIDATION=1, MAP_A, and MAP_B.");
            return;
        }

        output.WriteLine("Acquiring candidates and the C.3 0x5388 region hypothesis...");
        NativeStableSelectionSlotSession? session =
            NativeStableSelectionSlotSession.TryAcquire(
            [
                NativeStableKnownMapInput.FromPath("A", mapA),
                NativeStableKnownMapInput.FromPath("B", mapB)
            ],
            expectedSelectedMap: "A",
            out NativeStableSelectionSlotAcquisition acquisition);

        output.WriteLine("=== NATIVE STABLE SELECTION SLOT VALIDATION ===");
        output.WriteLine($"ProcessId={acquisition.Discovery.ProcessId?.ToString() ?? "unavailable"}");
        output.WriteLine($"AcquisitionCandidateCount={acquisition.Candidates.Count}");
        for (int index = 0; index < acquisition.Candidates.Count; index++)
        {
            WriteSelectionSlotCandidate(index + 1, acquisition.Candidates[index]);
        }
        output.WriteLine($"InitialAcquisitionMilliseconds={acquisition.InitialAcquisition.TotalMilliseconds:F1}");
        output.WriteLine($"AcquisitionFailure={acquisition.FailureReason ?? "None"}");
        if (session is null)
        {
            return;
        }

        using (session)
        {
            (string State, string Expected)[] states =
            [
                ("A1", "A"),
                ("B1", "B"),
                ("A2", "A"),
                ("B2", "B"),
                ("A3", "A")
            ];
            var snapshotsByCandidate = acquisition.Candidates
                .Select((_, index) => new
                {
                    CandidateNumber = index + 1,
                    Snapshots = new List<NativeStableSelectionSlotSnapshot>()
                })
                .ToArray();

            foreach ((string state, string expected) in states)
            {
                Console.WriteLine(
                    $"READY FOR {state}: select map {expected} in osu!stable, then press Enter.");
                if (Console.ReadLine() is null)
                {
                    throw new InvalidOperationException(
                        "Interactive input ended before the five-state sequence completed.");
                }

                output.WriteLine(string.Empty);
                output.WriteLine($"State={state}");
                IReadOnlyList<NativeStableSelectionSlotCandidateCapture> captures =
                    session.Capture(state, expected);
                foreach (NativeStableSelectionSlotCandidateCapture capture in captures)
                {
                    snapshotsByCandidate[capture.CandidateNumber - 1]
                        .Snapshots.Add(capture.Snapshot);
                    WriteSelectionSlotSnapshot(capture.CandidateNumber, capture.Snapshot);
                }
            }

            output.WriteLine(string.Empty);
            output.WriteLine("Summary:");
            foreach (var candidateSnapshots in snapshotsByCandidate)
            {
                NativeStableSelectionSlotSequenceSummary summary =
                    NativeStableSelectionSlotSequenceAnalyzer.Analyze(
                        candidateSnapshots.Snapshots);
                output.WriteLine($"Candidate #{candidateSnapshots.CandidateNumber}:");
                output.WriteLine(
                    $"ObservedSequence={string.Join("->", summary.ObservedSequence)}");
                output.WriteLine(
                    $"SameRegionBaseAcrossSequence={summary.SameRegionBaseAcrossSequence}");
                output.WriteLine(
                    $"SameSlotAddressAcrossSequence={summary.SameSlotAddressAcrossSequence}");
                output.WriteLine(
                    $"ExactSelectionTrackingPattern={summary.ExactSelectionTrackingPattern}");
            }
        }
    }

    [Fact]
    [Trait("Category", "ManualLive")]
    public void DiscoverAndValidateTwoLevelRoot_A1_B1_A2_B2_A3_WhenExplicitlyEnabled()
    {
        string? enabled = Environment.GetEnvironmentVariable(
            "BEATINSIGHT_NATIVE_STABLE_ROOT_VALIDATION");
        string? mapA = Environment.GetEnvironmentVariable(
            "BEATINSIGHT_NATIVE_STABLE_ROOT_MAP_A");
        string? mapB = Environment.GetEnvironmentVariable(
            "BEATINSIGHT_NATIVE_STABLE_ROOT_MAP_B");
        string? requestedPhase = Environment.GetEnvironmentVariable(
            "BEATINSIGHT_NATIVE_STABLE_ROOT_PHASE");
        if (!string.Equals(enabled, "1", StringComparison.Ordinal))
        {
            output.WriteLine(
                "Manual root reverse-discovery skipped: set ROOT_VALIDATION=1.");
            return;
        }
        if (string.IsNullOrWhiteSpace(mapA) || string.IsNullOrWhiteSpace(mapB) ||
            string.IsNullOrWhiteSpace(requestedPhase) ||
            !new[] { "CALIBRATE", "A1", "B1", "A2", "B2", "A3", "FINALIZE" }
                .Contains(requestedPhase.Trim(), StringComparer.OrdinalIgnoreCase) ||
            !Enum.TryParse(requestedPhase, ignoreCase: true, out NativeStableRootPhase phase))
        {
            throw new InvalidOperationException(
                "Set ROOT_MAP_A, ROOT_MAP_B, and ROOT_PHASE=CALIBRATE|A1|B1|A2|B2|A3|FINALIZE.");
        }

        string sessionPath = Environment.GetEnvironmentVariable(
            NativeStableRootPhaseHarness.SessionEnvironmentVariable) ??
            NativeStableRootPhaseHarness.DefaultSessionPath;
        var store = new NativeStableRootDiagnosticSessionStore();

        if (phase == NativeStableRootPhase.Calibrate)
        {
            using NativeStableRootReverseDiscoverySession? calibration =
                NativeStableRootReverseDiscoverySession.TryCalibrate(
                    mapA, mapB, out string? failure);
            if (calibration is null)
            {
                throw new InvalidOperationException(
                    $"Calibration failed: {failure ?? "Unknown"}");
            }
            if (calibration.ProcessStartIdentity is null)
            {
                throw new InvalidOperationException(
                    "Calibration did not capture a process-start identity.");
            }
            NativeStableRootDiagnosticSession created = NativeStableRootPhaseHarness.Create(
                calibration.ProcessId,
                calibration.ProcessStartIdentity.Value,
                calibration.MapA,
                calibration.MapB,
                calibration.Calibration,
                calibration.GroundTruthObjectDiscovery,
                calibration.TotalCalibration);
            store.Save(sessionPath, created);
            WriteRootCalibration(created, sessionPath);
            return;
        }

        NativeStableRootDiagnosticSession session = store.Load(sessionPath);
        EnsureSameMapPath(mapA, session.MapA.KnownMapPath, "A");
        EnsureSameMapPath(mapB, session.MapB.KnownMapPath, "B");
        if (phase == NativeStableRootPhase.Finalize)
        {
            WriteRootFinal(session, NativeStableRootPhaseHarness.Finalize(session), sessionPath);
            return;
        }

        int entering = session.SurvivingCandidateIndexes.Length;
        NativeStableRootTransitionCapture capture = NativeStableRootLivePhaseCapture.Capture(
            session, phase, out int processId, out DateTimeOffset? processStartIdentity);
        NativeStableRootPhaseHarness.ApplyCapture(
            session, phase, processId, processStartIdentity, capture);
        store.Save(sessionPath, session);
        output.WriteLine($"PHASE={phase.ToString().ToUpperInvariant()}");
        output.WriteLine($"EXPECTED_MAP={ExpectedMapLabel(phase)}");
        output.WriteLine($"ProcessId={session.ProcessId}");
        output.WriteLine($"ProcessStartIdentity={session.ProcessStartIdentity:O}");
        output.WriteLine($"InitialRootSlotCount={session.InitialCandidates.Select(candidate => candidate.RootSlotAddress).Distinct().Count()}");
        output.WriteLine($"CandidatesEnteringPhase={entering}");
        output.WriteLine($"CandidatesSurvivingPhase={session.SurvivingCandidateIndexes.Length}");
        output.WriteLine($"SessionPath={sessionPath}");
        output.WriteLine(
            $"TransitionValidationMilliseconds={capture.ValidationTime.TotalMilliseconds:F3}");
        foreach (NativeStableRootCandidateTransition transition in capture.Candidates)
        {
            WriteRootTransition(transition);
        }
    }

    [Fact]
    [Trait("Category", "ManualLive")]
    public void CharacterizeDynamicRootQ_A_B_A2_WhenExplicitlyEnabled()
    {
        string? enabled = Environment.GetEnvironmentVariable(
            "BEATINSIGHT_NATIVE_STABLE_Q_CHARACTERIZATION");
        if (!string.Equals(enabled, "1", StringComparison.Ordinal))
        {
            output.WriteLine("Manual Q characterization skipped: set Q_CHARACTERIZATION=1.");
            return;
        }
        string? requested = Environment.GetEnvironmentVariable(
            "BEATINSIGHT_NATIVE_STABLE_Q_PHASE");
        if (string.IsNullOrWhiteSpace(requested) ||
            !new[] { "CAPTURE_A", "CAPTURE_B", "CAPTURE_A2", "FINALIZE" }
                .Contains(requested.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Set Q_PHASE=CAPTURE_A|CAPTURE_B|CAPTURE_A2|FINALIZE.");
        }
        NativeStableQPhase phase = requested.Trim().ToUpperInvariant() switch
        {
            "CAPTURE_A" => NativeStableQPhase.CaptureA,
            "CAPTURE_B" => NativeStableQPhase.CaptureB,
            "CAPTURE_A2" => NativeStableQPhase.CaptureA2,
            _ => NativeStableQPhase.Finalize
        };
        string rootSessionPath = Environment.GetEnvironmentVariable(
            NativeStableRootPhaseHarness.SessionEnvironmentVariable) ??
            NativeStableRootPhaseHarness.DefaultSessionPath;
        string sessionPath = Environment.GetEnvironmentVariable(
            NativeStableQCharacterizationHarness.SessionEnvironmentVariable) ??
            NativeStableQCharacterizationHarness.DefaultSessionPath;
        string profilePath = Environment.GetEnvironmentVariable(
            NativeStableQCharacterizationHarness.ProfileEnvironmentVariable) ??
            NativeStableQCharacterizationHarness.DefaultProfilePath;
        var store = new NativeStableQCharacterizationStore();

        NativeStableQCharacterizationSession session;
        if (phase == NativeStableQPhase.CaptureA)
        {
            NativeStableRootDiagnosticSession c3 =
                new NativeStableRootDiagnosticSessionStore().Load(rootSessionPath);
            session = NativeStableQCharacterizationHarness.CreateFromCompletedC3(c3);
        }
        else
        {
            session = store.Load(sessionPath);
        }

        if (phase == NativeStableQPhase.Finalize)
        {
            NativeStableQCharacterizationSummary summary =
                NativeStableQCharacterizationHarness.Finalize(session);
            store.SaveProfile(profilePath, summary.RestartProfile);
            WriteQFinal(session, summary, sessionPath, profilePath);
            return;
        }

        NativeStableQCharacterizationCapture capture = NativeStableQLiveCapture.Capture(
            session, phase, out int processId, out DateTimeOffset? processStartIdentity);
        NativeStableQCharacterizationHarness.ApplyCapture(
            session, phase, processId, processStartIdentity, capture);
        store.Save(sessionPath, session);
        WriteQCapture(session, capture, sessionPath);
    }

    [Fact]
    [Trait("Category", "ManualLive")]
    public void ReacquireDynamicQAcrossRestart_WhenExplicitlyEnabled()
    {
        string? enabled = Environment.GetEnvironmentVariable(
            "BEATINSIGHT_NATIVE_STABLE_Q_RESTART_VALIDATION");
        if (!string.Equals(enabled, "1", StringComparison.Ordinal))
        {
            output.WriteLine("Manual Q restart experiment skipped: set Q_RESTART_VALIDATION=1.");
            return;
        }
        string? requested = Environment.GetEnvironmentVariable(
            "BEATINSIGHT_NATIVE_STABLE_Q_RESTART_PHASE");
        string[] accepted =
        [
            "CAPTURE_GENERATION_A_PROFILE", "RESTART_CALIBRATE_A",
            "RESTART_CAPTURE_B", "RESTART_CAPTURE_A2", "FINALIZE"
        ];
        if (string.IsNullOrWhiteSpace(requested) ||
            !accepted.Contains(requested.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Set Q_RESTART_PHASE=CAPTURE_GENERATION_A_PROFILE|RESTART_CALIBRATE_A|RESTART_CAPTURE_B|RESTART_CAPTURE_A2|FINALIZE.");
        }
        NativeStableQRestartPhase phase = requested.Trim().ToUpperInvariant() switch
        {
            "CAPTURE_GENERATION_A_PROFILE" =>
                NativeStableQRestartPhase.CaptureGenerationAProfile,
            "RESTART_CALIBRATE_A" => NativeStableQRestartPhase.RestartCalibrateA,
            "RESTART_CAPTURE_B" => NativeStableQRestartPhase.RestartCaptureB,
            "RESTART_CAPTURE_A2" => NativeStableQRestartPhase.RestartCaptureA2,
            _ => NativeStableQRestartPhase.Finalize
        };
        string historicalSessionPath = Environment.GetEnvironmentVariable(
            NativeStableQCharacterizationHarness.SessionEnvironmentVariable) ??
            NativeStableQCharacterizationHarness.DefaultSessionPath;
        string historicalProfilePath = Environment.GetEnvironmentVariable(
            NativeStableQCharacterizationHarness.ProfileEnvironmentVariable) ??
            NativeStableQCharacterizationHarness.DefaultProfilePath;
        string restartSessionPath = Environment.GetEnvironmentVariable(
            NativeStableQRestartExperiment.SessionEnvironmentVariable) ??
            NativeStableQRestartExperiment.DefaultSessionPath;
        string comparisonPath = Environment.GetEnvironmentVariable(
            NativeStableQRestartExperiment.ComparisonEnvironmentVariable) ??
            NativeStableQRestartExperiment.DefaultComparisonPath;
        var restartStore = new NativeStableQRestartStore();

        if (phase == NativeStableQRestartPhase.CaptureGenerationAProfile)
        {
            var historicalStore = new NativeStableQCharacterizationStore();
            string c3SessionPath = Environment.GetEnvironmentVariable(
                NativeStableRootPhaseHarness.SessionEnvironmentVariable) ??
                NativeStableRootPhaseHarness.DefaultSessionPath;
            NativeStableQRestartExperimentSession created =
                NativeStableQRestartExperiment.ImportGenerationA(
                    new NativeStableRootDiagnosticSessionStore().Load(c3SessionPath),
                    historicalStore.Load(historicalSessionPath),
                    historicalStore.LoadProfile(historicalProfilePath));
            restartStore.Save(restartSessionPath, created);
            output.WriteLine("PHASE=CAPTURE_GENERATION_A_PROFILE");
            output.WriteLine($"GenerationAProcessId={created.GenerationA.ProcessId}");
            output.WriteLine($"GenerationAProcessStartIdentity={created.GenerationA.ProcessStartIdentity:O}");
            output.WriteLine($"GenerationAQ=0x{created.GenerationA.InitialCandidates[0].InitialQAddress:x}");
            output.WriteLine($"GenerationAObservedQCandidateCount={created.GenerationA.AllObservedQAddresses.Length}");
            output.WriteLine("MemoryScanPerformed=False");
            output.WriteLine("NextPhase=RESTART_CALIBRATE_A");
            output.WriteLine($"SessionPath={restartSessionPath}");
            return;
        }

        NativeStableQRestartExperimentSession session = restartStore.Load(restartSessionPath);
        if (phase == NativeStableQRestartPhase.RestartCalibrateA)
        {
            string mapA = RequireEnvironment("BEATINSIGHT_NATIVE_STABLE_Q_RESTART_MAP_A");
            string mapB = RequireEnvironment("BEATINSIGHT_NATIVE_STABLE_Q_RESTART_MAP_B");
            NativeStableRestartGeneration generation =
                NativeStableQRestartLiveHarness.CalibrateGenerationB(session, mapA, mapB);
            NativeStableQRestartExperiment.ApplyGenerationBCalibration(session, generation);
            restartStore.Save(restartSessionPath, session);
            WriteRestartCalibration(generation, restartSessionPath);
            return;
        }
        if (phase is NativeStableQRestartPhase.RestartCaptureB or
            NativeStableQRestartPhase.RestartCaptureA2)
        {
            NativeStableRestartTransitionPhase transition =
                NativeStableQRestartLiveHarness.CaptureTransition(
                    session, phase, out int processId,
                    out DateTimeOffset? processStartIdentity);
            int entering = session.GenerationB!.SurvivorIndexes.Length;
            NativeStableQRestartExperiment.ApplyTransition(
                session, phase, processId, processStartIdentity, transition);
            restartStore.Save(restartSessionPath, session);
            WriteRestartTransition(
                session.GenerationB, transition, entering, restartSessionPath);
            return;
        }

        NativeStableQRestartComparison comparison =
            NativeStableQRestartExperiment.Finalize(session);
        restartStore.SaveComparison(comparisonPath, comparison);
        WriteRestartFinal(session, comparison, restartSessionPath, comparisonPath);
    }

    [Fact]
    [Trait("Category", "ManualLive")]
    public void ValidateAutonomousQAcrossGenerationsCDE_WhenExplicitlyEnabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(
                "BEATINSIGHT_NATIVE_STABLE_AUTONOMOUS_Q_VALIDATION"),
                "1", StringComparison.Ordinal))
        {
            output.WriteLine("Manual autonomous-Q validation skipped: set AUTONOMOUS_Q_VALIDATION=1.");
            return;
        }
        string phaseText = RequireEnvironment(
            "BEATINSIGHT_NATIVE_STABLE_AUTONOMOUS_Q_PHASE").Trim().ToUpperInvariant();
        NativeStableAutonomousQPhase phase = phaseText switch
        {
            "CAPTURE_C_A" => NativeStableAutonomousQPhase.CaptureCA,
            "CAPTURE_C_B" => NativeStableAutonomousQPhase.CaptureCB,
            "CAPTURE_C_A2" => NativeStableAutonomousQPhase.CaptureCA2,
            "FINALIZE_C" => NativeStableAutonomousQPhase.FinalizeC,
            "CAPTURE_D_A" => NativeStableAutonomousQPhase.CaptureDA,
            "CAPTURE_D_B" => NativeStableAutonomousQPhase.CaptureDB,
            "CAPTURE_D_A2" => NativeStableAutonomousQPhase.CaptureDA2,
            "FINALIZE_D" => NativeStableAutonomousQPhase.FinalizeD,
            "CAPTURE_E_A" => NativeStableAutonomousQPhase.CaptureEA,
            "CAPTURE_E_B" => NativeStableAutonomousQPhase.CaptureEB,
            "CAPTURE_E_A2" => NativeStableAutonomousQPhase.CaptureEA2,
            "FINALIZE_E" => NativeStableAutonomousQPhase.FinalizeE,
            "FINALIZE_ALL" => NativeStableAutonomousQPhase.FinalizeAll,
            _ => throw new InvalidOperationException("Unsupported AUTONOMOUS_Q_PHASE.")
        };
        string sessionPath = Environment.GetEnvironmentVariable(
            NativeStableAutonomousQHarness.SessionEnvironmentVariable) ??
            NativeStableAutonomousQHarness.DefaultSessionPath;
        string profilePath = Environment.GetEnvironmentVariable(
            NativeStableAutonomousQHarness.ProfileEnvironmentVariable) ??
            NativeStableAutonomousQHarness.DefaultProfilePath;
        string comparisonPath = Environment.GetEnvironmentVariable(
            NativeStableAutonomousQHarness.ComparisonEnvironmentVariable) ??
            NativeStableAutonomousQHarness.DefaultComparisonPath;
        var store = new NativeStableAutonomousQStore();

        NativeStableAutonomousQValidationSession session;
        if (phase == NativeStableAutonomousQPhase.CaptureCA)
        {
            string restartSessionPath = Environment.GetEnvironmentVariable(
                NativeStableQRestartExperiment.SessionEnvironmentVariable) ??
                NativeStableQRestartExperiment.DefaultSessionPath;
            string restartComparisonPath = Environment.GetEnvironmentVariable(
                NativeStableQRestartExperiment.ComparisonEnvironmentVariable) ??
                NativeStableQRestartExperiment.DefaultComparisonPath;
            string structuralProfilePath = Environment.GetEnvironmentVariable(
                NativeStableQCharacterizationHarness.ProfileEnvironmentVariable) ??
                NativeStableQCharacterizationHarness.DefaultProfilePath;
            var restartStore = new NativeStableQRestartStore();
            NativeStableQRestartExperimentSession restart =
                restartStore.Load(restartSessionPath);
            NativeStableQRestartComparison comparison =
                restartStore.LoadComparison(restartComparisonPath);
            NativeStableRootStructuralProfile structural =
                new NativeStableQCharacterizationStore().LoadProfile(structuralProfilePath);
            NativeStableAutonomousQProfile profile =
                NativeStableAutonomousQProfile.FromCrossProcessEvidence(
                    structural, restart, comparison);
            store.SaveProfile(profilePath, profile);
            session = NativeStableAutonomousQHarness.Create(
                profile,
                restart.GenerationA.ProcessStartIdentity,
                restart.GenerationB!.ProcessStartIdentity);
        }
        else
        {
            session = store.Load(sessionPath);
        }

        if (phase == NativeStableAutonomousQPhase.FinalizeAll)
        {
            NativeStableAutonomousQComparison comparison =
                NativeStableAutonomousQHarness.FinalizeAll(session);
            store.SaveComparison(comparisonPath, comparison);
            WriteAutonomousFinal(comparison, sessionPath, profilePath, comparisonPath);
            return;
        }

        string label = phase switch
{
    NativeStableAutonomousQPhase.CaptureCA or
    NativeStableAutonomousQPhase.CaptureCB or
    NativeStableAutonomousQPhase.CaptureCA2 or
    NativeStableAutonomousQPhase.FinalizeC => "C",

    NativeStableAutonomousQPhase.CaptureDA or
    NativeStableAutonomousQPhase.CaptureDB or
    NativeStableAutonomousQPhase.CaptureDA2 or
    NativeStableAutonomousQPhase.FinalizeD => "D",

    NativeStableAutonomousQPhase.CaptureEA or
    NativeStableAutonomousQPhase.CaptureEB or
    NativeStableAutonomousQPhase.CaptureEA2 or
    NativeStableAutonomousQPhase.FinalizeE => "E",

    _ => throw new ArgumentOutOfRangeException(nameof(phase))
};
        if (phase is NativeStableAutonomousQPhase.FinalizeC or
            NativeStableAutonomousQPhase.FinalizeD or
            NativeStableAutonomousQPhase.FinalizeE)
        {
            NativeStableAutonomousGenerationSummary summary =
                NativeStableAutonomousQHarness.FinalizeGeneration(session, label);
            store.Save(sessionPath, session);
            WriteAutonomousGenerationSummary(summary, sessionPath);
            return;
        }

        string mapA = RequireEnvironment("BEATINSIGHT_NATIVE_STABLE_AUTONOMOUS_Q_MAP_A");
        string mapB = RequireEnvironment("BEATINSIGHT_NATIVE_STABLE_AUTONOMOUS_Q_MAP_B");
        bool startup = phase is NativeStableAutonomousQPhase.CaptureCA or
            NativeStableAutonomousQPhase.CaptureDA or
            NativeStableAutonomousQPhase.CaptureEA;
        if (startup)
        {
            (NativeStableAutonomousQLocatorResult result,
             NativeStableAutonomousHarnessTruth[] truth) =
                NativeStableAutonomousQLiveHarness.CaptureStartup(session.Profile, mapA);
            NativeStableAutonomousQHarness.ApplyStartup(session, label, result, truth);
            store.Save(sessionPath, session);
            WriteAutonomousStartup(label, result, truth, sessionPath);
            return;
        }

        NativeStableAutonomousGenerationValidation generation =
            session.Generations.Single(item => item.Label == label);
        bool a2 = phase is NativeStableAutonomousQPhase.CaptureCA2 or
            NativeStableAutonomousQPhase.CaptureDA2 or
            NativeStableAutonomousQPhase.CaptureEA2;
        (int processId, DateTimeOffset? processStart, NativeStableAutonomousHarnessTruth[] transitionTruth,
            long validationTicks) = NativeStableAutonomousQLiveHarness.CaptureTransition(
                generation, a2 ? mapA : mapB, fromB: a2);
        NativeStableAutonomousQHarness.ApplyTransition(
            session, label, a2 ? "A2" : "B", processId, processStart, transitionTruth);
        generation.StartupResult = generation.StartupResult with
        {
            Timings = generation.StartupResult.Timings with
            {
                TransitionValidationTicks =
                    generation.StartupResult.Timings.TransitionValidationTicks + validationTicks
            }
        };
        store.Save(sessionPath, session);
        WriteAutonomousTransition(label, a2 ? "A2" : "B", transitionTruth, validationTicks, sessionPath);
    }

    [Fact]
    [Trait("Category", "ManualLive")]
    public void DiscoverBeatmapObject_WhenExplicitlyEnabled()
    {
        string? knownMap = Environment.GetEnvironmentVariable(
            "BEATINSIGHT_NATIVE_STABLE_POC_MAP");
        if (string.IsNullOrWhiteSpace(knownMap))
        {
            output.WriteLine(
                "Manual beatmap-object discovery skipped: supply BEATINSIGHT_NATIVE_STABLE_POC_MAP.");
            return;
        }

        NativeStableBeatmapObjectCapture capture =
            NativeStableBeatmapObjectDiscovery.Capture(knownMap);
        output.WriteLine("============================================================");
        output.WriteLine("NATIVE STABLE BEATMAP OBJECT DISCOVERY");
        output.WriteLine("============================================================");
        output.WriteLine($"CaptureId={capture.CaptureId}");
        output.WriteLine($"ObservedAtUtc={capture.ObservedAtUtc:O}");
        output.WriteLine($"ProcessId={capture.ProcessId?.ToString() ?? "unavailable"}");
        output.WriteLine($"ProcessStartIdentity={capture.ProcessStartedAtUtc?.ToString("O") ?? "unavailable"}");
        output.WriteLine($"KnownMapPath={capture.Metadata?.KnownMapPath ?? "unavailable"}");
        output.WriteLine($"KnownChecksum={capture.Metadata?.Checksum ?? "unavailable"}");
        output.WriteLine($"ExpectedBeatmapId={capture.Metadata?.BeatmapId?.ToString() ?? "unavailable"}");
        output.WriteLine($"ExpectedBeatmapSetId={capture.Metadata?.BeatmapSetId?.ToString() ?? "unavailable"}");
        output.WriteLine($"ArchitectureSupported={capture.Discovery.ArchitectureSupported}");
        output.WriteLine($"CandidateDiscovery={capture.Discovery.Status}");
        output.WriteLine($"Failure={capture.FailureReason ?? "None"}");

        NativeStableBeatmapObjectStatistics statistics = capture.Discovery.Statistics;
        output.WriteLine($"ChecksumReferences={statistics.ChecksumReferenceCount}");
        output.WriteLine($"FolderReferences={statistics.FolderReferenceCount}");
        output.WriteLine($"FilenameReferences={statistics.FilenameReferenceCount}");
        output.WriteLine($"ChecksumImpliedBases={statistics.ChecksumImpliedBaseCount}");
        output.WriteLine($"FolderImpliedBases={statistics.FolderImpliedBaseCount}");
        output.WriteLine($"FilenameImpliedBases={statistics.FilenameImpliedBaseCount}");
        output.WriteLine($"SurvivingTwoFields={statistics.SurvivingTwoFieldCount}");
        output.WriteLine($"SurvivingThreeFields={statistics.SurvivingThreeFieldCount}");
        output.WriteLine($"SurvivingAvailableIds={statistics.SurvivingAvailableIdCount}");
        output.WriteLine($"RawChecksumUtf16Address={Hex(capture.Discovery.ChecksumRawUtf16Address)}");
        output.WriteLine($"RawFilenameUtf16Address={Hex(capture.Discovery.FilenameRawUtf16Address)}");
        output.WriteLine($"RawChecksumToFilenameDelta={SignedHex(capture.Discovery.RawChecksumToFilenameDelta)}");
        output.WriteLine(
            $"RawDeltaMatchesHypothesizedFieldSpacing={capture.Discovery.RawDeltaMatchesHypothesizedFieldSpacing?.ToString() ?? "unavailable"}");

        int candidateNumber = 0;
        foreach (NativeStableBeatmapObjectCandidate candidate in
                 capture.Discovery.StrongCandidates)
        {
            candidateNumber++;
            output.WriteLine($"Candidate #{candidateNumber}");
            output.WriteLine($"Address=0x{candidate.CandidateAddress:x}");
            output.WriteLine($"Level={candidate.Level}");
            output.WriteLine($"MatchedCoreFields={candidate.MatchedCoreFieldCount}/3");
            output.WriteLine(
                $"MatchedOptionalIds={candidate.MatchedOptionalIdCount}/{candidate.AvailableOptionalIdCount}");
            WriteObjectField("Checksum", candidate.Checksum);
            WriteObjectField("Folder", candidate.Folder);
            WriteObjectField("Filename", candidate.Filename);
            output.WriteLine("IDs:");
            output.WriteLine($"  BeatmapId={candidate.ObservedBeatmapId?.ToString() ?? "unreadable"}");
            output.WriteLine($"  Matches={candidate.BeatmapIdMatches?.ToString() ?? "unavailable"}");
            output.WriteLine($"  BeatmapSetId={candidate.ObservedBeatmapSetId?.ToString() ?? "unreadable"}");
            output.WriteLine($"  Matches={candidate.BeatmapSetIdMatches?.ToString() ?? "unavailable"}");
            WriteRegion("Region", candidate.Region);
        }

        NativeStableBeatmapObjectTimings timings = capture.Discovery.Timings;
        output.WriteLine("TimingsMilliseconds:");
        output.WriteLine($"  ProcessDiscovery={timings.ProcessDiscovery.TotalMilliseconds:F1}");
        output.WriteLine($"  MemoryRegionEnumeration={timings.MemoryRegionEnumeration.TotalMilliseconds:F1}");
        output.WriteLine($"  KnownSignalSearch={timings.KnownSignalSearch.TotalMilliseconds:F1}");
        output.WriteLine($"  ReverseReferenceSearch={timings.ReverseReferenceSearch.TotalMilliseconds:F1}");
        output.WriteLine($"  CandidateValidation={timings.CandidateValidation.TotalMilliseconds:F1}");
        output.WriteLine($"  Total={timings.Total.TotalMilliseconds:F1}");
        output.WriteLine("============================================================");
    }

    private void WriteAutonomousStartup(
        string label,
        NativeStableAutonomousQLocatorResult result,
        NativeStableAutonomousHarnessTruth[] truth,
        string sessionPath)
    {
        output.WriteLine("============================================================");
        output.WriteLine($"AUTONOMOUS Q — GENERATION {label} STARTUP");
        output.WriteLine($"ProcessId={result.ProcessId}");
        output.WriteLine($"ProcessStartIdentity={result.ProcessStartIdentity:O}");
        output.WriteLine($"Discovery={result.Discovery}");
        output.WriteLine($"SelectedQ={HexUlong(result.SelectedQAddress)}");
        output.WriteLine($"CandidateCount={result.Candidates.Length}");
        output.WriteLine($"UsedCache={result.UsedCache}");
        output.WriteLine($"CacheRevalidated={result.CacheRevalidated}");
        output.WriteLine($"UsedFallback={result.UsedFallback}");
        output.WriteLine($"HeavyTopologyScanPerformed={result.HeavyTopologyScanPerformed}");
        foreach (NativeStableAutonomousQCandidate candidate in result.Candidates)
        {
            NativeStableAutonomousHarnessTruth? oracle = truth.SingleOrDefault(item =>
                item.QAddress == candidate.QAddress);
            output.WriteLine(
                $"Q=0x{candidate.QAddress:x}; X={HexUlong(candidate.XAddress)}; Path={candidate.DiscoveryPath}; StructuralScore={candidate.StructuralScore:F2}; FinalScore={candidate.FinalScore:F2}; Similarity={candidate.NeighborhoodSimilarity:P1}; ExactAllocationOffset={candidate.ExactObservedAllocationOffset}; ExactRegionOffset={candidate.ExactObservedRegionOffset}; ObjectValid={candidate.ObjectEvidence.IsValid}; HarnessExpectedMapMatches={oracle?.ExpectedMapMatches.ToString() ?? "Unavailable"}");
        }
        WriteAutonomousTimings(result.Timings);
        output.WriteLine($"SessionPath={sessionPath}");
        output.WriteLine("============================================================");
    }

    private void WriteAutonomousTransition(
        string label,
        string map,
        NativeStableAutonomousHarnessTruth[] truth,
        long ticks,
        string sessionPath)
    {
        output.WriteLine($"PHASE=CAPTURE_{label}_{map}");
        output.WriteLine($"CandidatesEntering={truth.Length}");
        output.WriteLine($"CandidatesSurviving={truth.Count(item => item.ExpectedMapMatches)}");
        foreach (NativeStableAutonomousHarnessTruth item in truth)
        {
            output.WriteLine(
                $"Q=0x{item.QAddress:x}; X={HexUlong(item.XAddress)}; PointerReadable={item.PointerReadable}; FinalObjectReadable={item.FinalObjectReadable}; Core={item.MatchedCoreFields}/3; Ids={item.MatchedOptionalIds}/{item.AvailableOptionalIds}; ExpectedMapMatches={item.ExpectedMapMatches}");
        }
        output.WriteLine($"TransitionValidationMilliseconds={TimeSpan.FromTicks(ticks).TotalMilliseconds:F3}");
        output.WriteLine($"SessionPath={sessionPath}");
    }

    private void WriteAutonomousGenerationSummary(
        NativeStableAutonomousGenerationSummary summary,
        string sessionPath)
    {
        output.WriteLine($"PHASE=FINALIZE_{summary.Label}");
        output.WriteLine($"ProcessId={summary.ProcessId}");
        output.WriteLine($"ProcessStartIdentity={summary.ProcessStartIdentity:O}");
        output.WriteLine($"StartupDiscovery={summary.StartupDiscovery}");
        output.WriteLine($"StartupCandidateCount={summary.StartupCandidateCount}");
        output.WriteLine($"SelectedQ={HexUlong(summary.SelectedQ)}");
        output.WriteLine($"SurvivingAfterB={summary.SurvivingAfterB}");
        output.WriteLine($"SurvivingAfterA2={summary.SurvivingAfterA2}");
        output.WriteLine($"StrongTransitionValidation={summary.StrongTransitionValidation}");
        output.WriteLine($"StartupLocatorMilliseconds={TimeSpan.FromTicks(summary.StartupLocatorTicks).TotalMilliseconds:F3}");
        output.WriteLine("FINALIZE_MEMORY_READ=False");
        output.WriteLine($"SessionPath={sessionPath}");
    }

    private void WriteAutonomousFinal(
        NativeStableAutonomousQComparison comparison,
        string sessionPath,
        string profilePath,
        string comparisonPath)
    {
        output.WriteLine("============================================================");
        output.WriteLine("AUTONOMOUS Q MULTI-GENERATION FINAL");
        foreach (NativeStableAutonomousGenerationSummary generation in comparison.Generations)
        {
            output.WriteLine(
                $"Generation={generation.Label}; Process={generation.ProcessId}/{generation.ProcessStartIdentity:O}; Discovery={generation.StartupDiscovery}; Candidates={generation.StartupCandidateCount}; Q={HexUlong(generation.SelectedQ)}; AfterB={generation.SurvivingAfterB}; AfterA2={generation.SurvivingAfterA2}; StrongTransition={generation.StrongTransitionValidation}; LocatorMs={TimeSpan.FromTicks(generation.StartupLocatorTicks).TotalMilliseconds:F3}");
        }
        foreach (NativeStableInvariantComparison invariant in comparison.CrossGenerationInvariants)
        {
            output.WriteLine($"{invariant.Name}={invariant.Status}; {invariant.Detail}");
        }
        output.WriteLine($"FINALIZE_MEMORY_READ={comparison.MemoryReadPerformed}");
        output.WriteLine($"SessionPath={sessionPath}");
        output.WriteLine($"ProfilePath={profilePath}");
        output.WriteLine($"ComparisonPath={comparisonPath}");
        output.WriteLine("============================================================");
    }

    private void WriteAutonomousTimings(NativeStableAutonomousQLocatorTimings timings)
    {
        output.WriteLine("TimingsMilliseconds:");
        output.WriteLine($"  ProcessAttach={TimeSpan.FromTicks(timings.ProcessAttachTicks).TotalMilliseconds:F3}");
        output.WriteLine($"  CandidateRegionEnumeration={TimeSpan.FromTicks(timings.CandidateRegionEnumerationTicks).TotalMilliseconds:F3}");
        output.WriteLine($"  CandidateGeneration={TimeSpan.FromTicks(timings.CandidateGenerationTicks).TotalMilliseconds:F3}");
        output.WriteLine($"  NeighborhoodValidation={TimeSpan.FromTicks(timings.NeighborhoodValidationTicks).TotalMilliseconds:F3}");
        output.WriteLine($"  ObjectValidation={TimeSpan.FromTicks(timings.ObjectValidationTicks).TotalMilliseconds:F3}");
        output.WriteLine($"  Decision={TimeSpan.FromTicks(timings.DecisionTicks).TotalMilliseconds:F3}");
        output.WriteLine($"  TransitionValidation={TimeSpan.FromTicks(timings.TransitionValidationTicks).TotalMilliseconds:F3}");
        output.WriteLine($"  Total={TimeSpan.FromTicks(timings.TotalTicks).TotalMilliseconds:F3}");
    }

    private void WriteRestartCalibration(
        NativeStableRestartGeneration generation,
        string sessionPath)
    {
        output.WriteLine("============================================================");
        output.WriteLine("NATIVE STABLE Q RESTART — GENERATION B CALIBRATION");
        output.WriteLine("============================================================");
        output.WriteLine("PHASE=RESTART_CALIBRATE_A");
        output.WriteLine($"ProcessId={generation.ProcessId}");
        output.WriteLine($"ProcessStartIdentity={generation.ProcessStartIdentity:O}");
        output.WriteLine($"X_A=0x{generation.XMapA:x}");
        output.WriteLine($"QCandidateCount={generation.InitialCandidates.Length}");
        output.WriteLine($"PersistedQCandidateCount={generation.InitialCandidates.Length}");
        int shown = Math.Min(generation.PresentationLimit, generation.InitialCandidates.Length);
        output.WriteLine($"PresentedQCandidateCount={shown}");
        output.WriteLine($"PresentationLimit={generation.PresentationLimit}");
        for (int index = 0; index < shown; index++)
        {
            NativeStableRestartCandidate candidate = generation.InitialCandidates[index];
            output.WriteLine($"CandidateIndex={index}; Q=0x{candidate.InitialQAddress:x}");
            WriteObservedAddress("  QMetadata", candidate.CalibrationCapture.QMetadata);
            output.WriteLine($"  RCount={candidate.CalibrationCapture.ReferencesToQ.Length}");
            output.WriteLine($"  ClusterCount={candidate.CalibrationCapture.ReferenceClusters.Length}");
            output.WriteLine($"  TCount={candidate.CalibrationCapture.ThirdLevelReferences.Length}");
        }
        WriteRestartTimings(generation.Timings);
        output.WriteLine("NextPhase=RESTART_CAPTURE_B");
        output.WriteLine($"SessionPath={sessionPath}");
        output.WriteLine("============================================================");
    }

    private void WriteRestartTransition(
        NativeStableRestartGeneration generation,
        NativeStableRestartTransitionPhase phase,
        int entering,
        string sessionPath)
    {
        output.WriteLine("============================================================");
        output.WriteLine($"PHASE={phase.Phase.ToUpperInvariant()}");
        output.WriteLine($"ProcessId={generation.ProcessId}");
        output.WriteLine($"ProcessStartIdentity={generation.ProcessStartIdentity:O}");
        output.WriteLine($"CandidatesEntering={entering}");
        output.WriteLine($"CandidatesSurviving={generation.SurvivorIndexes.Length}");
        foreach (NativeStableRestartCandidateTransition candidate in phase.Transitions)
        {
            output.WriteLine($"CandidateIndex={candidate.CandidateIndex}");
            output.WriteLine($"  Q=0x{candidate.QAddress:x}");
            output.WriteLine($"  X={HexUlong(candidate.XAddress)}");
            output.WriteLine($"  PointerReadable={candidate.PointerReadable}");
            output.WriteLine($"  FinalObjectReadable={candidate.FinalObjectReadable}");
            output.WriteLine($"  Core={candidate.MatchedCoreFields}/3");
            output.WriteLine($"  Ids={candidate.MatchedOptionalIds}/{candidate.AvailableOptionalIds}");
            output.WriteLine($"  ExpectedMapMatches={candidate.ExpectedMapMatches}");
        }
        foreach (NativeStableQCharacterizationCapture capture in
                 phase.SurvivorCharacterizations)
        {
            output.WriteLine(
                $"Survivor Q=0x{capture.QAddress:x}; R={capture.ReferencesToQ.Length}; Clusters={capture.ReferenceClusters.Length}; T={capture.ThirdLevelReferences.Length}");
        }
        output.WriteLine($"Discovery={generation.Discovery}");
        WriteRestartTimings(generation.Timings);
        output.WriteLine($"NextPhase={(phase.Phase == "RestartCaptureB" ? "RESTART_CAPTURE_A2" : "FINALIZE")}");
        output.WriteLine($"SessionPath={sessionPath}");
        output.WriteLine("============================================================");
    }

    private void WriteRestartFinal(
        NativeStableQRestartExperimentSession session,
        NativeStableQRestartComparison comparison,
        string sessionPath,
        string comparisonPath)
    {
        output.WriteLine("============================================================");
        output.WriteLine("NATIVE STABLE Q CROSS-PROCESS COMPARISON");
        output.WriteLine("============================================================");
        output.WriteLine("PHASE=FINALIZE");
        output.WriteLine($"GenerationA={comparison.GenerationAProcessId}/{comparison.GenerationAStart:O}");
        output.WriteLine($"GenerationB={comparison.GenerationBProcessId}/{comparison.GenerationBStart:O}");
        output.WriteLine($"InitialQCandidateCount={comparison.InitialQCandidateCount}");
        output.WriteLine($"SurvivingQCandidateCount={comparison.SurvivingQCandidateCount}");
        output.WriteLine($"Discovery={comparison.Discovery}");
        output.WriteLine("EXACT INVARIANTS:");
        foreach (NativeStableInvariantComparison invariant in comparison.ExactInvariants)
        {
            output.WriteLine($"  {invariant.Name}={invariant.Status}; {invariant.Detail}");
        }
        output.WriteLine("STRUCTURAL INVARIANTS:");
        foreach (NativeStableRestartCandidateComparison candidate in
                 comparison.CandidateComparisons)
        {
            output.WriteLine($"CandidateIndex={candidate.CandidateIndex}; Q=0x{candidate.QAddress:x}");
            foreach (NativeStableInvariantComparison invariant in candidate.StructuralInvariants)
            {
                output.WriteLine($"  {invariant.Name}={invariant.Status}; {invariant.Detail}");
            }
        }
        output.WriteLine($"StructuralComparisonMilliseconds={TimeSpan.FromTicks(comparison.StructuralComparisonTicks).TotalMilliseconds:F3}");
        output.WriteLine($"FINALIZE_MEMORY_SCAN={comparison.MemoryScanPerformed}");
        output.WriteLine($"SessionPath={sessionPath}");
        output.WriteLine($"ComparisonPath={comparisonPath}");
        output.WriteLine($"ReferenceProfileReplaced=False");
        WriteRestartTimings(session.GenerationB!.Timings);
        output.WriteLine("============================================================");
    }

    private void WriteRestartTimings(NativeStableRestartTimings timings)
    {
        output.WriteLine("TimingsMilliseconds:");
        output.WriteLine($"  GroundTruthObjectDiscovery={TimeSpan.FromTicks(timings.GroundTruthObjectDiscoveryTicks).TotalMilliseconds:F1}");
        output.WriteLine($"  QCandidateSearch={TimeSpan.FromTicks(timings.QCandidateSearchTicks).TotalMilliseconds:F1}");
        output.WriteLine($"  QMetadata={TimeSpan.FromTicks(timings.QMetadataTicks).TotalMilliseconds:F1}");
        output.WriteLine($"  QNeighborhood={TimeSpan.FromTicks(timings.QNeighborhoodTicks).TotalMilliseconds:F1}");
        output.WriteLine($"  RReferenceSearch={TimeSpan.FromTicks(timings.RReferenceSearchTicks).TotalMilliseconds:F1}");
        output.WriteLine($"  RMetadata={TimeSpan.FromTicks(timings.RMetadataTicks).TotalMilliseconds:F1}");
        output.WriteLine($"  ThirdLevelReferenceSearch={TimeSpan.FromTicks(timings.ThirdLevelReferenceSearchTicks).TotalMilliseconds:F1}");
        output.WriteLine($"  TransitionValidation={TimeSpan.FromTicks(timings.TransitionValidationTicks).TotalMilliseconds:F1}");
        output.WriteLine($"  StructuralComparison={TimeSpan.FromTicks(timings.StructuralComparisonTicks).TotalMilliseconds:F1}");
        output.WriteLine($"  Total={TimeSpan.FromTicks(timings.TotalTicks).TotalMilliseconds:F1}");
    }

    private static string RequireEnvironment(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Set {name}.");

    private void WriteQCapture(
        NativeStableQCharacterizationSession session,
        NativeStableQCharacterizationCapture capture,
        string sessionPath)
    {
        output.WriteLine("============================================================");
        output.WriteLine("NATIVE STABLE DYNAMIC Q CHARACTERIZATION");
        output.WriteLine("============================================================");
        output.WriteLine($"PHASE={capture.Phase.ToUpperInvariant()}");
        output.WriteLine($"EXPECTED_MAP={(capture.Phase == "CaptureB" ? "B" : "A")}");
        output.WriteLine($"ProcessId={session.ProcessId}");
        output.WriteLine($"ProcessStartIdentity={session.ProcessStartIdentity:O}");
        output.WriteLine($"Q=0x{capture.QAddress:x}");
        output.WriteLine($"X=0x{capture.XAddress:x}");
        output.WriteLine($"Core={capture.MatchedCoreFields}/3");
        output.WriteLine($"Ids={capture.MatchedOptionalIds}/{capture.AvailableOptionalIds}");
        output.WriteLine($"ExpectedMapMatches={capture.ExpectedMapMatches}");
        WriteObservedAddress("QMetadata", capture.QMetadata);
        WriteStructuralFingerprint("QNeighborhood", capture.QNeighborhood);
        output.WriteLine($"ReferenceToQCount={capture.ReferencesToQ.Length}");
        output.WriteLine($"ExpectedC3ReferenceCount={capture.ExpectedReferenceCount}");
        output.WriteLine($"MissingC3ReferenceCount={capture.MissingExpectedReferences.Length}");
        foreach (NativeStableQReference reference in capture.ReferencesToQ)
        {
            output.WriteLine($"R=0x{reference.AbsoluteAddress:x} -> Q=0x{capture.QAddress:x}");
            WriteObservedAddress("  RMetadata", reference.Address);
            WriteStructuralFingerprint("  RNeighborhood", reference.Neighborhood);
        }
        output.WriteLine($"ReferenceClusterCount={capture.ReferenceClusters.Length}");
        foreach (NativeStableReferenceCluster cluster in capture.ReferenceClusters)
        {
            output.WriteLine(
                $"Cluster FirstR=0x{cluster.FirstAbsoluteAddress:x}; LastR=0x{cluster.LastAbsoluteAddress:x}; Count={cluster.Count}; FirstAllocationOffset={HexUlong(cluster.FirstOffsetFromAllocation)}; LastAllocationOffset={HexUlong(cluster.LastOffsetFromAllocation)}; Protection=0x{cluster.Protection:x}; Type=0x{cluster.Type:x}; Backing={cluster.Backing}; Module={cluster.Module?.Name ?? "None"}");
        }
        output.WriteLine($"ThirdLevelReferenceCount={capture.ThirdLevelReferences.Length}");
        foreach (NativeStableThirdLevelReference third in capture.ThirdLevelReferences)
        {
            output.WriteLine(
                $"T=0x{third.AbsoluteAddress:x} -> R=0x{third.ReferencedRAddress:x} -> Q=0x{capture.QAddress:x} -> X=0x{capture.XAddress:x}");
            WriteObservedAddress("  TMetadata", third.Address);
        }
        output.WriteLine($"TotalMilliseconds={TimeSpan.FromTicks(capture.TotalTicks).TotalMilliseconds:F1}");
        output.WriteLine($"NextPhase={session.NextPhase.ToString().ToUpperInvariant()}");
        output.WriteLine($"SessionPath={sessionPath}");
        output.WriteLine("============================================================");
    }

    private void WriteQFinal(
        NativeStableQCharacterizationSession session,
        NativeStableQCharacterizationSummary summary,
        string sessionPath,
        string profilePath)
    {
        output.WriteLine("============================================================");
        output.WriteLine("NATIVE STABLE DYNAMIC Q CHARACTERIZATION — FINAL");
        output.WriteLine("============================================================");
        output.WriteLine("PHASE=FINALIZE");
        output.WriteLine($"ProcessId={session.ProcessId}");
        output.WriteLine($"ProcessStartIdentity={session.ProcessStartIdentity:O}");
        output.WriteLine($"Q=0x{session.QAddress:x}");
        output.WriteLine($"CaptureCount={session.Captures.Length}");
        output.WriteLine("ExactInvariants:");
        output.WriteLine($"  QAddress={summary.ExactQAddressInvariant}");
        output.WriteLine($"  QRegionAllocationIdentity={summary.ExactQRegionIdentityInvariant}");
        output.WriteLine($"  CompleteReferenceSet={summary.ExactReferenceSetInvariant}");
        output.WriteLine("StructuralInvariants:");
        output.WriteLine($"  RestartProfile={summary.StructuralProfileInvariant}");
        output.WriteLine("ChangingValues:");
        foreach (NativeStableQCharacterizationCapture capture in summary.Captures)
        {
            output.WriteLine(
                $"  {capture.Phase}: X=0x{capture.XAddress:x}; References={capture.ReferencesToQ.Length}; Clusters={capture.ReferenceClusters.Length}; ThirdLevel={capture.ThirdLevelReferences.Length}");
        }
        output.WriteLine($"SessionPath={sessionPath}");
        output.WriteLine($"RestartProfilePath={profilePath}");
        output.WriteLine("FINALIZE_MEMORY_SCAN=False");
        output.WriteLine("============================================================");
    }

    private void WriteObservedAddress(string label, NativeStableObservedAddress address)
    {
        output.WriteLine($"{label}:");
        output.WriteLine($"  Address=0x{address.AbsoluteAddress:x}");
        output.WriteLine($"  RegionBase={HexUlong(address.RegionBase)}");
        output.WriteLine($"  AllocationBase={HexUlong(address.AllocationBase)}");
        output.WriteLine($"  OffsetFromRegionBase=0x{address.OffsetFromRegionBase:x}");
        output.WriteLine($"  OffsetFromAllocationBase={HexUlong(address.OffsetFromAllocationBase)}");
        output.WriteLine($"  RegionSize=0x{address.RegionSize:x}");
        output.WriteLine($"  State=0x{address.State:x}");
        output.WriteLine($"  Protection=0x{address.Protection:x}");
        output.WriteLine($"  Type=0x{address.Type:x}");
        output.WriteLine($"  Backing={address.Backing}");
        output.WriteLine($"  Alignment={address.Alignment}");
        output.WriteLine($"  Module={address.Module?.Name ?? "None"}");
        output.WriteLine($"  ModulePath={address.Module?.Path ?? "None"}");
        output.WriteLine($"  ModuleOffset={HexUlong(address.Module?.OffsetFromModuleBase)}");
    }

    private void WriteStructuralFingerprint(
        string label,
        NativeStableStructuralFingerprint fingerprint)
    {
        output.WriteLine($"{label}: Radius=0x{fingerprint.Radius:x}; Cells={fingerprint.Cells.Length}");
        foreach (NativeStableStructuralCell cell in fingerprint.Cells)
        {
            output.WriteLine(
                $"  Offset={SignedHex(cell.RelativeOffset)}; Kind={cell.Kind}; TargetBacking={cell.PointerTarget?.Backing.ToString() ?? "None"}; TargetProtection={(cell.PointerTarget is null ? "None" : $"0x{cell.PointerTarget.Protection:x}")}; TargetType={(cell.PointerTarget is null ? "None" : $"0x{cell.PointerTarget.Type:x}")}; TargetModule={cell.PointerTarget?.ModuleName ?? "None"}; TargetModuleOffset={HexUlong(cell.PointerTarget?.OffsetFromModuleBase)}");
        }
    }

    private static string HexUlong(ulong? value) =>
        value is null ? "None" : $"0x{value.Value:x}";

    private void WriteRootCalibration(
        NativeStableRootDiagnosticSession session,
        string sessionPath)
    {
        output.WriteLine("============================================================");
        output.WriteLine("NATIVE STABLE ROOT REVERSE-DISCOVERY — CALIBRATE");
        output.WriteLine("============================================================");
        output.WriteLine("PHASE=CALIBRATE");
        output.WriteLine("EXPECTED_MAP=A");
        output.WriteLine($"ProcessId={session.ProcessId}");
        output.WriteLine($"ProcessStartIdentity={session.ProcessStartIdentity:O}");
        output.WriteLine($"GroundTruthX=0x{session.GroundTruthX:x}");
        output.WriteLine($"FirstLevelReferenceCount={session.QCandidateAddresses.Length}");
        output.WriteLine($"SecondLevelRootSlotCount={session.InitialCandidates.Select(candidate => candidate.RootSlotAddress).Distinct().Count()}");
        output.WriteLine($"InitialChainCount={session.InitialCandidates.Length}");
        output.WriteLine($"InitialRootSlotCount={session.InitialCandidates.Select(candidate => candidate.RootSlotAddress).Distinct().Count()}");
        output.WriteLine($"CandidatesEnteringPhase={session.InitialCandidates.Length}");
        output.WriteLine($"CandidatesSurvivingPhase={session.InitialCandidates.Length}");
        output.WriteLine($"SessionPath={sessionPath}");
        output.WriteLine($"NextPhase={session.NextPhase.ToString().ToUpperInvariant()}");
        int shown = Math.Min(session.PresentedCandidateLimit, session.InitialCandidates.Length);
        output.WriteLine($"PresentedCandidateCount={shown}");
        output.WriteLine($"PresentationLimit={session.PresentedCandidateLimit}");
        for (int index = 0; index < shown; index++)
        {
            WriteRootCandidate(index + 1, session.InitialCandidates[index].ToRuntime());
        }
        output.WriteLine("NO A1 CAPTURE WAS PERFORMED");
        output.WriteLine("============================================================");
    }

    private void WriteRootFinal(
        NativeStableRootDiagnosticSession session,
        NativeStableRootTransitionSummary summary,
        string sessionPath)
    {
        output.WriteLine("============================================================");
        output.WriteLine("NATIVE STABLE ROOT REVERSE-DISCOVERY");
        output.WriteLine("============================================================");
        output.WriteLine("PHASE=FINALIZE");
        output.WriteLine("EXPECTED_MAP=NONE");
        output.WriteLine($"ProcessId={session.ProcessId}");
        output.WriteLine($"ProcessStartIdentity={session.ProcessStartIdentity:O}");
        output.WriteLine($"GroundTruthX=0x{session.GroundTruthX:x}");
        output.WriteLine($"FirstLevelReferenceCount={session.QCandidateAddresses.Length}");
        output.WriteLine($"SecondLevelRootSlotCount={session.InitialCandidates.Select(candidate => candidate.RootSlotAddress).Distinct().Count()}");
        output.WriteLine($"InitialChainCount={session.InitialCandidates.Length}");
        output.WriteLine($"InitialRootSlotCount={session.InitialCandidates.Length}");
        output.WriteLine($"CandidatesEnteringPhase={session.SurvivingCandidateIndexes.Length}");
        output.WriteLine($"CandidatesSurvivingPhase={session.SurvivingCandidateIndexes.Length}");
        output.WriteLine($"SessionPath={sessionPath}");
        foreach (NativeStableRemainingCount remaining in summary.RemainingAfterEachPhase)
        {
            output.WriteLine($"RemainingAfter{remaining.Phase}={remaining.Remaining}");
        }
        for (int index = 0; index < session.InitialCandidates.Length; index++)
        {
            NativeStableRootCandidateSurvival survival = summary.Candidates[index];
            WriteRootCandidate(index + 1, session.InitialCandidates[index].ToRuntime());
            foreach (NativeStableRootPhase phase in new[]
                     {
                         NativeStableRootPhase.A1, NativeStableRootPhase.B1,
                         NativeStableRootPhase.A2, NativeStableRootPhase.B2,
                         NativeStableRootPhase.A3
                     })
            {
                NativeStableRootCandidateTransition? transition = survival.Transitions
                    .SingleOrDefault(item => item.Phase == phase.ToString());
                output.WriteLine($"{phase}Result={(transition is null ? "NOT_EVALUATED_AFTER_REMOVAL" : transition.ExpectedMapMatches ? "PASS" : "FAIL")}");
                if (transition is not null)
                {
                    WriteRootTransition(transition);
                }
            }
            output.WriteLine($"FinalSurvival={survival.Classification}");
        }
        output.WriteLine($"SurvivingRootSlots={summary.SurvivingRootSlots}");
        output.WriteLine($"RootSlotDiscovery={summary.RootSlotDiscovery}");
        output.WriteLine("TimingsMilliseconds:");
        output.WriteLine($"  GroundTruthObjectDiscovery={TimeSpan.FromTicks(session.GroundTruthObjectDiscoveryTicks).TotalMilliseconds:F1}");
        output.WriteLine($"  FirstLevelReferenceSearch={TimeSpan.FromTicks(session.FirstLevelReferenceSearchTicks).TotalMilliseconds:F1}");
        output.WriteLine($"  SecondLevelReferenceSearch={TimeSpan.FromTicks(session.SecondLevelReferenceSearchTicks).TotalMilliseconds:F1}");
        output.WriteLine($"  CandidateMetadata={TimeSpan.FromTicks(session.CandidateMetadataTicks).TotalMilliseconds:F1}");
        output.WriteLine($"  ReverseDiscoveryTotal={TimeSpan.FromTicks(session.ReverseDiscoveryTotalTicks).TotalMilliseconds:F1}");
        output.WriteLine($"  TotalCalibration={TimeSpan.FromTicks(session.TotalCalibrationTicks).TotalMilliseconds:F1}");
        foreach (NativeStablePersistedRootCapture capture in session.PhaseCaptures)
        {
            output.WriteLine($"  {capture.Phase}Validation={TimeSpan.FromTicks(capture.ValidationTimeTicks).TotalMilliseconds:F3}");
        }
        output.WriteLine("============================================================");
    }

    private static void EnsureSameMapPath(string supplied, string calibrated, string label)
    {
        if (!string.Equals(Path.GetFullPath(supplied), Path.GetFullPath(calibrated),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Map {label} path differs from the calibrated diagnostic session.");
        }
    }

    private static string ExpectedMapLabel(NativeStableRootPhase phase) => phase switch
    {
        NativeStableRootPhase.A1 or NativeStableRootPhase.A2 or NativeStableRootPhase.A3 => "A",
        NativeStableRootPhase.B1 or NativeStableRootPhase.B2 => "B",
        _ => "NONE"
    };

    private void WriteObjectField(
        string name,
        NativeStableBeatmapObjectFieldEvidence evidence)
    {
        output.WriteLine($"{name}:");
        output.WriteLine($"  FieldAddress=0x{evidence.FieldAddress:x}");
        output.WriteLine($"  Reference={Hex(evidence.Reference)}");
        output.WriteLine($"  Matches={evidence.Matches}");
    }

    private void WriteRootCandidate(
        int candidateNumber,
        NativeStableRootChainCandidate candidate)
    {
        output.WriteLine($"Candidate #{candidateNumber}");
        output.WriteLine($"S=0x{candidate.RootSlotAddress:x}");
        output.WriteLine($"Q=0x{candidate.QCandidateAddress:x}");
        output.WriteLine($"X=0x{candidate.GroundTruthX:x}");
        WriteRootAddressMetadata("SRegion", candidate.RootSlotMetadata);
        WriteRootAddressMetadata("QRegion", candidate.QCandidateMetadata);
        WriteRootAddressMetadata("XRegion", candidate.XMetadata);
        output.WriteLine($"SAndQShareRegion={candidate.RootSlotAndQShareRegion}");
        output.WriteLine($"QAndXShareRegion={candidate.QAndXShareRegion}");
        output.WriteLine("NeighborhoodFingerprint:");
        output.WriteLine(
            $"  DistanceFromRegionStart=0x{candidate.NeighborhoodFingerprint.DistanceFromRegionStart:x}");
        output.WriteLine(
            $"  DistanceToRegionEnd=0x{candidate.NeighborhoodFingerprint.DistanceToRegionEnd:x}");
        foreach (IGrouping<NativeStableNeighborhoodValueKind, NativeStableNeighborhoodObservation> group in
                 candidate.NeighborhoodFingerprint.Observations.GroupBy(item => item.Kind))
        {
            output.WriteLine($"  {group.Key}={group.Count()}");
        }
    }

    private void WriteRootAddressMetadata(
        string name,
        NativeStableAddressMetadata metadata)
    {
        output.WriteLine($"{name}:");
        output.WriteLine($"  Base={Hex(metadata.RegionBase)}");
        output.WriteLine($"  AllocationBase={Hex(metadata.AllocationBase)}");
        output.WriteLine($"  OffsetFromBase=0x{metadata.OffsetFromRegionBase:x}");
        output.WriteLine($"  Size=0x{metadata.RegionSize:x}");
        output.WriteLine($"  State=0x{metadata.State:x}");
        output.WriteLine($"  Protection=0x{metadata.Protection:x}");
        output.WriteLine($"  Type=0x{metadata.Type:x}");
        output.WriteLine($"  Backing={metadata.Backing}");
        output.WriteLine($"  Alignment={metadata.Alignment}");
    }

    private void WriteRootTransition(NativeStableRootCandidateTransition transition)
    {
        output.WriteLine($"S=0x{transition.RootSlotAddress:x}");
        output.WriteLine($"Q={Hex(transition.CurrentQAddress)}");
        output.WriteLine($"X={Hex(transition.CurrentXAddress)}");
        output.WriteLine($"PointerReadable={transition.PointerReadable}");
        output.WriteLine($"FinalObjectReadable={transition.FinalObjectReadable}");
        output.WriteLine($"Core={transition.MatchedCoreFields}/3");
        output.WriteLine(
            $"Ids={transition.MatchedOptionalIds}/{transition.AvailableOptionalIds}");
        output.WriteLine($"ExpectedMapMatches={transition.ExpectedMapMatches}");
    }

    private void WriteSelectionSlotCandidate(
        int candidateNumber,
        NativeStableSelectionSlotCandidate candidate)
    {
        output.WriteLine($"Candidate #{candidateNumber}");
        output.WriteLine($"RegionBase=0x{candidate.SourceRegion.BaseAddress:x}");
        output.WriteLine($"RegionSize=0x{candidate.SourceRegion.Size:x}");
        output.WriteLine($"RegionState=0x{candidate.SourceRegion.State:x}");
        output.WriteLine($"RegionProtection=0x{candidate.SourceRegion.Protect:x}");
        output.WriteLine($"RegionType=0x{candidate.SourceRegion.Type:x}");
        output.WriteLine($"SlotOffset=0x{candidate.SlotOffset:x}");
        output.WriteLine($"SlotAddress=0x{candidate.SlotAddress:x}");
        output.WriteLine($"AcquisitionReferenceKind={candidate.AcquisitionReferenceKind}");
        output.WriteLine($"InitialResolvedTargetMap={candidate.InitialResolvedTargetMap}");
        output.WriteLine($"InitialResolvedTargetKind={candidate.InitialResolvedTargetKind}");
    }

    private void WriteSelectionSlotSnapshot(
        int candidateNumber,
        NativeStableSelectionSlotSnapshot snapshot)
    {
        output.WriteLine($"Candidate #{candidateNumber}:");
        output.WriteLine($"CaptureId={snapshot.CaptureId}");
        output.WriteLine($"ProcessId={snapshot.ProcessId}");
        output.WriteLine($"ObservedAtUtc={snapshot.ObservedAtUtc:O}");
        output.WriteLine($"ExpectedSelectedMap={snapshot.ExpectedSelectedMap}");
        output.WriteLine($"SourceRegionBase=0x{snapshot.SourceRegion.BaseAddress:x}");
        output.WriteLine($"SourceRegionSize=0x{snapshot.SourceRegion.Size:x}");
        output.WriteLine($"SourceRegionState=0x{snapshot.SourceRegion.State:x}");
        output.WriteLine($"SourceRegionProtection=0x{snapshot.SourceRegion.Protect:x}");
        output.WriteLine($"SourceRegionType=0x{snapshot.SourceRegion.Type:x}");
        output.WriteLine($"SourceOffsetWithinRegion=0x{snapshot.SourceOffsetWithinRegion:x}");
        output.WriteLine($"SlotAddress=0x{snapshot.SlotAddress:x}");
        output.WriteLine($"SlotReadable={snapshot.SlotReadable}");
        output.WriteLine($"RawSlotValue={Hex(snapshot.RawSlotValue)}");
        output.WriteLine($"Level1={Hex(snapshot.DereferencedValueLevel1)}");
        output.WriteLine($"Level2={Hex(snapshot.DereferencedValueLevel2)}");
        output.WriteLine($"ResolvedTargetMap={snapshot.ResolvedTargetMap}");
        output.WriteLine($"ResolvedTargetKind={snapshot.ResolvedTargetKind}");
        output.WriteLine($"RegionInvalidated={snapshot.RegionInvalidated}");
        output.WriteLine($"ReacquisitionAttempted={snapshot.ReacquisitionAttempted}");
        output.WriteLine($"RegionReacquired={snapshot.RegionReacquired}");
        output.WriteLine($"OldRegionBase={Hex(snapshot.OldRegionBase)}");
        output.WriteLine($"NewRegionBase={Hex(snapshot.NewRegionBase)}");
        output.WriteLine($"RapidReadMilliseconds={snapshot.RapidRead.TotalMilliseconds:F3}");
        output.WriteLine($"ResolutionMilliseconds={snapshot.Resolution.TotalMilliseconds:F3}");
        output.WriteLine($"TotalPerSnapshotMilliseconds={snapshot.Total.TotalMilliseconds:F3}");
    }

    private static string Hex(nuint? value) =>
        value is null ? "unavailable" : $"0x{value.Value:x}";

    private static string SignedHex(long? value) => value switch
    {
        null => "unavailable",
        < 0 => $"-0x{-value.Value:x}",
        _ => $"+0x{value.Value:x}"
    };

    private void WriteOccurrenceGroup(
        NativeStableKnownMapCapture map,
        NativeStableKnownSignalKind kind,
        string heading)
    {
        output.WriteLine($"{heading}:");
        NativeStableProbeSignal[] signals = map.Signals
            .Where(signal => signal.Kind == kind)
            .ToArray();
        if (signals.Length == 0)
        {
            output.WriteLine("  not supplied");
        }

        foreach (NativeStableProbeSignal signal in signals)
        {
            output.WriteLine(
                $"  {signal.Encoding}: count={signal.Addresses.Count}; addresses={string.Join(',', signal.Addresses.Take(8).Select(address => $"0x{address:x}"))}");
        }
    }

    private void WriteCandidate(int index, NativeStableCorrelationCandidate candidate)
    {
        output.WriteLine($"Candidate #{index + 1}");
        output.WriteLine($"  MapLabel={candidate.MapLabel}");
        output.WriteLine($"  ChecksumAddress=0x{candidate.ChecksumAddress:x} ({candidate.ChecksumEncoding})");
        output.WriteLine($"  FilenameAddress=0x{candidate.FilenameAddress:x} ({candidate.FilenameEncoding})");
        output.WriteLine($"  Delta={SignedHex(candidate.Delta)}");
        output.WriteLine($"  HighlightedPlus0x50={candidate.IsPlus50}");
        output.WriteLine($"  SameRegion={candidate.SameRegion}");
        WriteRegion("  ChecksumRegion", candidate.ChecksumRegion);
        WriteRegion("  FilenameRegion", candidate.FilenameRegion);

        output.WriteLine("  NearbyKnownFolders:");
        if (candidate.NearbyKnownFolders.Count == 0)
        {
            output.WriteLine("    none");
        }

        foreach (NativeStableFolderRelationship folder in candidate.NearbyKnownFolders)
        {
            output.WriteLine(
                $"    address=0x{folder.Address:x}; checksumDelta={SignedHex(folder.ChecksumDelta)}; filenameDelta={SignedHex(folder.FilenameDelta)}; encoding={folder.Encoding}");
        }

        output.WriteLine("  StructuralPointers:");
        if (candidate.PointerRelationships.Count == 0)
        {
            output.WriteLine("    none");
        }

        foreach (NativeStablePointerRelationship pointer in candidate.PointerRelationships)
        {
            output.WriteLine(
                $"    source=0x{pointer.SourceAddress:x}; target=0x{pointer.TargetAddress:x}; sourceDelta={SignedHex(pointer.SourceDeltaFromChecksum)}; classification={pointer.Classification}; targetMap={pointer.TargetMapLabel ?? "unknown"}; targetKind={pointer.TargetKind?.ToString() ?? "unknown"}; targetEncoding={pointer.TargetEncoding?.ToString() ?? "unknown"}; references={pointer.ReferenceCount}; alignment={pointer.Alignment}");
        }

        output.WriteLine(
            $"  FilteredStringPayloadPointerLikeValues={candidate.FilteredStringPayloadPointerLikeValueCount}");
    }

    private void WriteReferences(
        string heading,
        IReadOnlyList<NativeStableReferenceObservation> references)
    {
        output.WriteLine($"{heading}:");
        if (references.Count == 0)
        {
            output.WriteLine("  none");
        }

        foreach (NativeStableReferenceObservation reference in references)
        {
            output.WriteLine(
                $"  source=0x{reference.SourceAddress:x}; sourceRegion=0x{reference.SourceRegion.BaseAddress:x}; sourceRegionSize=0x{reference.SourceRegion.Size:x}; sourceRegionState=0x{reference.SourceRegion.State:x}; sourceRegionProtection=0x{reference.SourceRegion.Protect:x}; sourceRegionType=0x{reference.SourceRegion.Type:x}; sourceOffsetWithinRegion=0x{reference.SourceOffsetWithinRegion:x}; sourceAlignment={reference.SourceAlignment}; targetMap={reference.TargetMapLabel}; targetKind={reference.TargetKind}; target=0x{reference.TargetAddress:x}; targetDeltaFromChecksum={SignedHex(reference.TargetDeltaFromChecksum)}; targetDeltaFromFilename={SignedHex(reference.TargetDeltaFromFilename)}; occurrences={reference.OccurrenceCount}");
        }
    }

    private void WriteIntermediateReferences(
        IReadOnlyList<NativeStableIntermediateReference> references)
    {
        output.WriteLine("IntermediateReadableReferences:");
        if (references.Count == 0)
        {
            output.WriteLine("  none");
        }

        foreach (NativeStableIntermediateReference reference in references)
        {
            output.WriteLine(
                $"  source=0x{reference.SourceAddress:x}; sourceRegion=0x{reference.SourceRegion.BaseAddress:x}; sourceOffsetWithinRegion=0x{reference.SourceOffsetWithinRegion:x}; intermediate=0x{reference.IntermediateAddress:x}; candidateTarget=0x{reference.CandidateTargetAddress:x}; targetMap={reference.TargetMapLabel}; targetKind={reference.TargetKind}; depth={reference.Depth}; alignment={reference.SourceAlignment}");
        }
    }

    private void WriteRegion(string label, NativeStableMemoryRegion? region)
    {
        if (region is null)
        {
            output.WriteLine($"{label}=unavailable");
            return;
        }

        output.WriteLine(
            $"{label}: base=0x{region.Value.BaseAddress:x}; size=0x{region.Value.Size:x}; state=0x{region.Value.State:x}; protection=0x{region.Value.Protect:x}; type=0x{region.Value.Type:x}");
    }

    private static string SignedHex(long value) =>
        value < 0 ? $"-0x{-value:x}" : $"+0x{value:x}";
}
