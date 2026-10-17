using BeatInsight.Diagnostics.NativeStable;
using BeatInsight.Services.Library;
using System.IO;
using System.Security.Cryptography;

namespace BeatInsight.Tests.Diagnostics;

public sealed class NativeStablePocTests : IDisposable
{
    private readonly string root;
    private readonly string songs;
    private readonly string mapFolder;
    private readonly string mapPath;

    public NativeStablePocTests()
    {
        root = Path.Combine(
            Path.GetTempPath(),
            "beatinsight-native-stable-" + Guid.NewGuid().ToString("N"));
        songs = Path.Combine(root, "Songs");
        mapFolder = Path.Combine(songs, "123 Artist - Title");
        Directory.CreateDirectory(mapFolder);
        mapPath = Path.Combine(mapFolder, "Artist - Title (Mapper) [Hard].osu");
        File.WriteAllText(mapPath, "osu file format v14\n");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void SignatureScanner_ExactMatch()
    {
        BytePattern pattern = BytePattern.Parse("AA BB CC");

        Assert.Equal([1], pattern.FindAll([0x00, 0xAA, 0xBB, 0xCC, 0x00]));
    }

    [Fact]
    public void SignatureScanner_WildcardMatch()
    {
        BytePattern pattern = BytePattern.Parse("AA ?? CC");

        Assert.Equal([0], pattern.FindAll([0xAA, 0x10, 0xCC]));
    }

    [Fact]
    public void SignatureScanner_NoMatch()
    {
        BytePattern pattern = BytePattern.Parse("AA BB");

        Assert.Empty(pattern.FindAll([0xAA, 0xBC]));
    }

    [Fact]
    public void SignatureScanner_MultipleMatches()
    {
        BytePattern pattern = BytePattern.Parse("AA ??");

        Assert.Equal([0, 2], pattern.FindAll([0xAA, 0x01, 0xAA, 0x02]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("GG")]
    [InlineData("?")]
    [InlineData("AA BBB")]
    public void SignatureScanner_MalformedPattern_IsRejected(string value)
    {
        Assert.ThrowsAny<ArgumentException>(() => BytePattern.Parse(value));
    }

    [Fact]
    public void SignatureScanner_BoundaryMatch()
    {
        BytePattern pattern = BytePattern.Parse("BB CC");

        Assert.Equal([2], pattern.FindAll([0x00, 0xAA, 0xBB, 0xCC]));
    }

    [Fact]
    public void PathValidation_ValidMapUnderSongs_IsAccepted()
    {
        string checksum = Checksum(mapPath);

        NativeStableMapValidationResult result =
            NativeStableCurrentMapValidator.Validate(
                123,
                songs,
                Path.GetFileName(mapFolder),
                Path.GetFileName(mapPath),
                checksum,
                verifyChecksum: true,
                DateTimeOffset.UtcNow);

        Assert.True(result.IsValid);
        Assert.Equal(Path.GetFullPath(mapPath), result.Observation!.AbsoluteOsuPath);
    }

    [Fact]
    public void PathValidation_Traversal_IsRejected()
    {
        File.WriteAllText(Path.Combine(root, "outside.osu"), "outside");

        NativeStableMapValidationResult result =
            NativeStableCurrentMapValidator.Validate(
                123,
                songs,
                "..",
                "outside.osu",
                null,
                verifyChecksum: false,
                DateTimeOffset.UtcNow);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void PathValidation_WrongExtension_IsRejected()
    {
        NativeStableMapValidationResult result =
            NativeStableCurrentMapValidator.Validate(
                123,
                songs,
                Path.GetFileName(mapFolder),
                "map.txt",
                null,
                verifyChecksum: false,
                DateTimeOffset.UtcNow);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void PathValidation_NonexistentFile_IsRejected()
    {
        NativeStableMapValidationResult result =
            NativeStableCurrentMapValidator.Validate(
                123,
                songs,
                Path.GetFileName(mapFolder),
                "missing.osu",
                null,
                verifyChecksum: false,
                DateTimeOffset.UtcNow);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void SongsResolver_ManualPathHasPriority()
    {
        string install = CreateInstallation("Songs");
        string manual = Path.Combine(root, "ManualSongs");
        Directory.CreateDirectory(manual);
        string preference = Path.Combine(root, "songs-path.txt");
        var existing = new SongsPathResolver(new FileSongsPathPreferenceStore(preference));
        existing.SaveManualPath(manual);

        NativeStableSongsPathResult result =
            new NativeStableSongsPathResolver(existing)
                .Resolve(Path.Combine(install, "osu!.exe"));

        Assert.Equal(manual, result.Path);
        Assert.Equal(NativeStableSongsPathSource.ManualPreference, result.Source);
    }

    [Fact]
    public void SongsResolver_RelativeConfiguration_UsesInstallationRoot()
    {
        string install = CreateInstallation("CustomSongs");
        File.WriteAllText(
            Path.Combine(install, $"osu!.{Environment.UserName}.cfg"),
            "BeatmapDirectory = CustomSongs\n");

        NativeStableSongsPathResult result = CreateSongsResolver()
            .Resolve(Path.Combine(install, "osu!.exe"));

        Assert.Equal(Path.Combine(install, "CustomSongs"), result.Path);
        Assert.Equal(NativeStableSongsPathSource.StableConfiguration, result.Source);
    }

    [Fact]
    public void SongsResolver_AbsoluteConfiguration_IsSupported()
    {
        string install = CreateInstallation("Songs");
        string external = Path.Combine(root, "ExternalSongs");
        Directory.CreateDirectory(external);
        File.WriteAllText(
            Path.Combine(install, $"osu!.{Environment.UserName}.cfg"),
            $"BeatmapDirectory={external}\n");

        NativeStableSongsPathResult result = CreateSongsResolver()
            .Resolve(Path.Combine(install, "osu!.exe"));

        Assert.Equal(external, result.Path);
        Assert.Equal(NativeStableSongsPathSource.StableConfiguration, result.Source);
    }

    [Fact]
    public void SongsResolver_AbsentConfiguration_UsesInstallationDefault()
    {
        string install = CreateInstallation("Songs");

        NativeStableSongsPathResult result = CreateSongsResolver()
            .Resolve(Path.Combine(install, "osu!.exe"));

        Assert.Equal(Path.Combine(install, "Songs"), result.Path);
        Assert.Equal(NativeStableSongsPathSource.InstallationDefault, result.Source);
    }

    [Fact]
    public void ChangeTracker_SameObservationTwice_DoesNotIncrement()
    {
        NativeStableMapValidationResult map = ValidMap(123, mapPath);
        var tracker = new NativeStableObservationTracker();

        NativeStableObservationChange first = tracker.Apply(map);
        NativeStableObservationChange second = tracker.Apply(map);

        Assert.True(first.Changed);
        Assert.False(second.Changed);
        Assert.Equal(1, second.Generation);
    }

    [Fact]
    public void ChangeTracker_AtoBtoA_IncrementsGeneration()
    {
        string secondPath = Path.Combine(mapFolder, "Artist - Title (Mapper) [Insane].osu");
        File.WriteAllText(secondPath, "osu file format v14\n[Difficulty]\n");
        var tracker = new NativeStableObservationTracker();

        tracker.Apply(ValidMap(123, mapPath));
        NativeStableObservationChange changed = tracker.Apply(ValidMap(123, secondPath));
        NativeStableObservationChange changedBack = tracker.Apply(ValidMap(123, mapPath));

        Assert.True(changed.Changed);
        Assert.Equal(2, changed.Generation);
        Assert.True(changedBack.Changed);
        Assert.Equal(3, changedBack.Generation);
    }

    [Fact]
    public void ChangeTracker_InvalidObservation_BecomesUnavailable()
    {
        var tracker = new NativeStableObservationTracker();
        tracker.Apply(ValidMap(123, mapPath));

        NativeStableObservationChange result = tracker.Apply(
            NativeStableMapValidationResult.Invalid);

        Assert.False(result.IsAvailable);
        Assert.Null(tracker.Current);
    }

    [Fact]
    public void ChangeTracker_ProcessReset_ClearsStateButKeepsMonotonicGeneration()
    {
        var tracker = new NativeStableObservationTracker();
        tracker.Apply(ValidMap(123, mapPath));

        tracker.ResetProcess();

        Assert.Null(tracker.Current);
        Assert.Equal(1, tracker.Generation);

        NativeStableObservationChange afterRestart = tracker.Apply(ValidMap(456, mapPath));
        Assert.True(afterRestart.Changed);
        Assert.Equal(2, afterRestart.Generation);
    }

    [Fact]
    public void Correlation_ChecksumToFilenamePlus50_IsHighlighted()
    {
        IReadOnlyList<NativeStableCorrelationCandidate> result = Correlate(
            [Signal(NativeStableKnownSignalKind.Checksum, 0x1100),
             Signal(NativeStableKnownSignalKind.Filename, 0x1150)],
            [Region(0x1000, 0x1000)]);

        NativeStableCorrelationCandidate candidate = Assert.Single(result);
        Assert.Equal(0x50, candidate.Delta);
        Assert.True(candidate.IsPlus50);
    }

    [Fact]
    public void Correlation_ArbitraryPositiveDelta_IsRetainedWithoutHighlight()
    {
        NativeStableCorrelationCandidate candidate = Assert.Single(Correlate(
            [Signal(NativeStableKnownSignalKind.Checksum, 0x1100),
             Signal(NativeStableKnownSignalKind.Filename, 0x1220)],
            [Region(0x1000, 0x1000)]));

        Assert.Equal(0x120, candidate.Delta);
        Assert.False(candidate.IsPlus50);
    }

    [Fact]
    public void Correlation_NegativeDelta_IsSigned()
    {
        NativeStableCorrelationCandidate candidate = Assert.Single(Correlate(
            [Signal(NativeStableKnownSignalKind.Checksum, 0x1200),
             Signal(NativeStableKnownSignalKind.Filename, 0x1180)],
            [Region(0x1000, 0x1000)]));

        Assert.Equal(-0x80, candidate.Delta);
    }

    [Fact]
    public void Correlation_MultipleCandidatePairs_AreAllReported()
    {
        IReadOnlyList<NativeStableCorrelationCandidate> result = Correlate(
            [Signal(NativeStableKnownSignalKind.Checksum, 0x1100, 0x2100),
             Signal(NativeStableKnownSignalKind.Filename, 0x1150, 0x2150)],
            [Region(0x1000, 0x1000), Region(0x2000, 0x1000)],
            relationshipWindow: 0x100);

        Assert.Equal(2, result.Count);
        Assert.All(result, candidate => Assert.True(candidate.IsPlus50));
    }

    [Fact]
    public void Correlation_NoCandidateWithinWindow_ReturnsEmpty()
    {
        IReadOnlyList<NativeStableCorrelationCandidate> result = Correlate(
            [Signal(NativeStableKnownSignalKind.Checksum, 0x1000),
             Signal(NativeStableKnownSignalKind.Filename, 0x4000)],
            [Region(0x1000, 0x4000)]);

        Assert.Empty(result);
    }

    [Fact]
    public void Correlation_FolderProximity_IsRankedAndCarriesBothDeltas()
    {
        NativeStableCorrelationCandidate candidate = Assert.Single(Correlate(
            [Signal(NativeStableKnownSignalKind.Checksum, 0x1100),
             Signal(NativeStableKnownSignalKind.Filename, 0x1150),
             Signal(NativeStableKnownSignalKind.Folder, 0x1080, 0x4000)],
            [Region(0x1000, 0x4000)]));

        NativeStableFolderRelationship folder = Assert.Single(candidate.NearbyKnownFolders);
        Assert.Equal((nuint)0x1080, folder.Address);
        Assert.Equal(0x80, folder.ChecksumDelta);
        Assert.Equal(0xd0, folder.FilenameDelta);
    }

    [Fact]
    public void Correlation_ClassifiesSameAndDifferentRegions()
    {
        IReadOnlyList<NativeStableCorrelationCandidate> result = Correlate(
            [Signal(NativeStableKnownSignalKind.Checksum, 0x1100),
             Signal(NativeStableKnownSignalKind.Filename, 0x1150, 0x2100)],
            [Region(0x1000, 0x1000), Region(0x2000, 0x1000)],
            relationshipWindow: 0x2000);

        Assert.Contains(result, candidate =>
            candidate.FilenameAddress == (nuint)0x1150 && candidate.SameRegion);
        Assert.Contains(result, candidate =>
            candidate.FilenameAddress == (nuint)0x2100 && !candidate.SameRegion);
    }

    [Fact]
    public void Correlation_UsesOccurrencesBeyondBoundedDisplayPrefix()
    {
        uint[] filenames =
        [
            0x1000, 0x1100, 0x1200, 0x1300,
            0x1400, 0x1500, 0x1600, 0x1700,
            0x9050
        ];

        IReadOnlyList<NativeStableCorrelationCandidate> result = Correlate(
            [Signal(NativeStableKnownSignalKind.Checksum, 0x9000),
             Signal(NativeStableKnownSignalKind.Filename, filenames)],
            [Region(0x1000, 0x9000)],
            relationshipWindow: 0x100);

        NativeStableCorrelationCandidate candidate = Assert.Single(result);
        Assert.Equal((nuint)0x9050, candidate.FilenameAddress);
        Assert.True(candidate.IsPlus50);
    }

    [Fact]
    public void MultiMapSignalPlan_ContainsBothMapsInOneScanDefinitionSet()
    {
        IReadOnlyList<NativeStableProbeSignal> signals = NativeStablePoc.BuildSignalDefinitions(
        [
            PreparedMap("A"),
            PreparedMap("B")
        ]);

        Assert.Equal(12, signals.Count);
        Assert.Equal(6, signals.Count(signal => signal.MapLabel == "A"));
        Assert.Equal(6, signals.Count(signal => signal.MapLabel == "B"));
    }

    [Fact]
    public void MultiPatternScanner_FindsTwoKnownMapsInOneSyntheticBufferPass()
    {
        IReadOnlyDictionary<string, IReadOnlyList<int>> matches =
            NativeStableProcessPatternSearch.FindAllInBuffer(
                [0x41, 0x2d, 0x4d, 0x41, 0x50, 0x42, 0x2d, 0x4d, 0x41, 0x50],
                [
                    new("A", BytePattern.FromBytes("A-MAP"u8)),
                    new("B", BytePattern.FromBytes("B-MAP"u8))
                ]);

        Assert.Equal([0], matches["A"]);
        Assert.Equal([5], matches["B"]);
    }

    [Fact]
    public void MultiMapLifetime_AObserved_BNotObserved()
    {
        IReadOnlyList<NativeStableKnownMapCapture> maps = AnalyzeMapCaptures(
        [
            LabeledSignal("A", NativeStableKnownSignalKind.Checksum, 0x1100),
            LabeledSignal("A", NativeStableKnownSignalKind.Filename, 0x1150),
            LabeledSignal("B", NativeStableKnownSignalKind.Checksum),
            LabeledSignal("B", NativeStableKnownSignalKind.Filename)
        ],
        [Region(0x1000, 0x1000)]);

        Assert.True(maps.Single(map => map.MapLabel == "A").Observed);
        Assert.False(maps.Single(map => map.MapLabel == "B").Observed);
    }

    [Fact]
    public void MultiMapLifetime_AAbsent_BObserved()
    {
        IReadOnlyList<NativeStableKnownMapCapture> maps = AnalyzeMapCaptures(
        [
            LabeledSignal("A", NativeStableKnownSignalKind.Checksum),
            LabeledSignal("A", NativeStableKnownSignalKind.Filename),
            LabeledSignal("B", NativeStableKnownSignalKind.Checksum, 0x2100),
            LabeledSignal("B", NativeStableKnownSignalKind.Filename, 0x2150)
        ],
        [Region(0x2000, 0x1000)]);

        Assert.False(maps.Single(map => map.MapLabel == "A").Observed);
        Assert.True(maps.Single(map => map.MapLabel == "B").Observed);
    }

    [Fact]
    public void MultiMapLifetime_BothObserved_WithIndependentUniquePlus50Counts()
    {
        IReadOnlyList<NativeStableKnownMapCapture> maps = AnalyzeMapCaptures(
        [
            LabeledSignal("A", NativeStableKnownSignalKind.Checksum, 0x1100),
            LabeledSignal("A", NativeStableKnownSignalKind.Filename, 0x1150),
            LabeledSignal("B", NativeStableKnownSignalKind.Checksum, 0x2100),
            LabeledSignal("B", NativeStableKnownSignalKind.Filename, 0x2150)
        ],
        [Region(0x1000, 0x2000)]);

        Assert.All(maps, map => Assert.True(map.Observed));
        Assert.All(maps, map => Assert.Single(map.CorrelationCandidates));
        Assert.All(maps, map => Assert.Equal(1, map.Plus0x50CandidateCount));
    }

    [Fact]
    public void MultiMapLifetime_MultiplePlus50CandidatesRemainMapSpecific()
    {
        IReadOnlyList<NativeStableKnownMapCapture> maps = AnalyzeMapCaptures(
        [
            LabeledSignal("A", NativeStableKnownSignalKind.Checksum, 0x1100, 0x1200),
            LabeledSignal("A", NativeStableKnownSignalKind.Filename, 0x1150, 0x1250),
            LabeledSignal("B", NativeStableKnownSignalKind.Checksum, 0x2100),
            LabeledSignal("B", NativeStableKnownSignalKind.Filename, 0x2150)
        ],
        [Region(0x1000, 0x2000)],
        relationshipWindow: 0x60);

        NativeStableKnownMapCapture mapA = maps.Single(map => map.MapLabel == "A");
        NativeStableKnownMapCapture mapB = maps.Single(map => map.MapLabel == "B");
        Assert.Equal(NativeStableMapObservationStatus.MultipleCandidates, mapA.Status);
        Assert.Equal(2, mapA.Plus0x50CandidateCount);
        Assert.Equal(NativeStableMapObservationStatus.Observed, mapB.Status);
        Assert.Equal(1, mapB.Plus0x50CandidateCount);
    }

    [Fact]
    public void MultiMapCorrelation_DoesNotPairChecksumAWithFilenameB()
    {
        IReadOnlyList<NativeStableCorrelationCandidate> candidates =
            NativeStableCandidateCorrelationAnalyzer.Analyze(
            [
                LabeledSignal("A", NativeStableKnownSignalKind.Checksum, 0x1100),
                LabeledSignal("A", NativeStableKnownSignalKind.Filename),
                LabeledSignal("B", NativeStableKnownSignalKind.Checksum),
                LabeledSignal("B", NativeStableKnownSignalKind.Filename, 0x1150)
            ],
            [Region(0x1000, 0x1000)]);

        Assert.Empty(candidates);
    }

    [Fact]
    public void CrossMapComparison_ReportsSameRegionAndSignedDistances()
    {
        IReadOnlyList<NativeStableKnownMapCapture> maps = AnalyzeMapCaptures(
        [
            LabeledSignal("A", NativeStableKnownSignalKind.Checksum, 0x1100),
            LabeledSignal("A", NativeStableKnownSignalKind.Filename, 0x1150),
            LabeledSignal("B", NativeStableKnownSignalKind.Checksum, 0x1300),
            LabeledSignal("B", NativeStableKnownSignalKind.Filename, 0x1350)
        ],
        [Region(0x1000, 0x1000)]);

        NativeStableCrossMapComparison comparison = Assert.Single(
            NativeStablePoc.BuildCrossMapComparisons(maps));
        Assert.True(comparison.SameRegion);
        Assert.Equal(0x200, comparison.ChecksumAddressDistance);
        Assert.Equal(0x200, comparison.FilenameAddressDistance);
    }

    [Fact]
    public void CrossMapComparison_ReportsDifferentRegions()
    {
        IReadOnlyList<NativeStableKnownMapCapture> maps = AnalyzeMapCaptures(
        [
            LabeledSignal("A", NativeStableKnownSignalKind.Checksum, 0x1100),
            LabeledSignal("A", NativeStableKnownSignalKind.Filename, 0x1150),
            LabeledSignal("B", NativeStableKnownSignalKind.Checksum, 0x2100),
            LabeledSignal("B", NativeStableKnownSignalKind.Filename, 0x2150)
        ],
        [Region(0x1000, 0x1000), Region(0x2000, 0x1000)]);

        NativeStableCrossMapComparison comparison = Assert.Single(
            NativeStablePoc.BuildCrossMapComparisons(maps));
        Assert.False(comparison.SameRegion);
    }

    [Fact]
    public void PointerInspection_FiltersPointerLikeValueInsideKnownStringPayload()
    {
        NativeStableCorrelationCandidate candidate = Candidate("A", 0x1100, 0x1150);
        NativeStableProbeSignal checksum = SignalWithBytes(
            "A", NativeStableKnownSignalKind.Checksum, new byte[64], 0x1100);
        var memory = new SyntheticMemoryReader(
            pointers: new Dictionary<nuint, nuint> { [(nuint)0x1100] = 0x1800 });

        NativeStableCandidateInspectionResult result = NativeStableCandidateInspector.Inspect(
            memory,
            candidate,
            [checksum],
            [Region(0x1000, 0x1000)]);

        Assert.Empty(result.Relationships);
        Assert.Equal(1, result.FilteredStringPayloadPointerLikeValueCount);
    }

    [Fact]
    public void PointerInspection_RetainsReadablePointerOutsideKnownPayload()
    {
        NativeStableCorrelationCandidate candidate = Candidate("A", 0x1100, 0x1150);
        NativeStableProbeSignal checksum = SignalWithBytes(
            "A", NativeStableKnownSignalKind.Checksum, new byte[64], 0x1100);
        var memory = new SyntheticMemoryReader(
            pointers: new Dictionary<nuint, nuint> { [(nuint)0x10f8] = 0x1800 });

        NativeStableCandidateInspectionResult result = NativeStableCandidateInspector.Inspect(
            memory,
            candidate,
            [checksum],
            [Region(0x1000, 0x1000)]);

        NativeStablePointerRelationship relationship = Assert.Single(result.Relationships);
        Assert.Equal(NativeStablePointerClassification.ReadablePointerCandidate,
            relationship.Classification);
        Assert.Equal(-8, relationship.SourceDeltaFromChecksum);
        Assert.Equal(0, result.FilteredStringPayloadPointerLikeValueCount);
    }

    [Fact]
    public void PointerInspection_KnownStringTargetIsRereadAndVerified()
    {
        NativeStableCorrelationCandidate candidate = Candidate("A", 0x1100, 0x1150);
        byte[] knownBytes = [0x41, 0x00, 0x42, 0x00];
        NativeStableProbeSignal target = SignalWithBytes(
            "B", NativeStableKnownSignalKind.Filename, knownBytes, 0x1800);
        var memory = new SyntheticMemoryReader(
            pointers: new Dictionary<nuint, nuint> { [(nuint)0x10f8] = 0x1800 },
            bytes: new Dictionary<nuint, byte[]> { [(nuint)0x1800] = knownBytes });

        NativeStableCandidateInspectionResult result = NativeStableCandidateInspector.Inspect(
            memory,
            candidate,
            [target],
            [Region(0x1000, 0x1000)]);

        NativeStablePointerRelationship relationship = Assert.Single(result.Relationships);
        Assert.Equal(NativeStablePointerClassification.KnownStringTarget,
            relationship.Classification);
        Assert.Equal("B", relationship.TargetMapLabel);
        Assert.Equal(NativeStableKnownSignalKind.Filename, relationship.TargetKind);
    }

    [Fact]
    public void ReferenceTargets_ContainChecksumFilenameAndObservedRelativeAddresses()
    {
        NativeStableCandidateTargetSet targets = NativeStableReferenceDiscovery.BuildTargetSet(
            [Candidate("A", 0x1100, 0x1150)],
            [Region(0x1000, 0x1000)]);

        Assert.Contains(targets.Targets, target =>
            target.Kind == NativeStableCandidateTargetKind.ChecksumAddress &&
            target.Address == (nuint)0x1100);
        Assert.Contains(targets.Targets, target =>
            target.Kind == NativeStableCandidateTargetKind.FilenameAddress &&
            target.Address == (nuint)0x1150);
        Assert.Contains(targets.Targets, target =>
            target.Kind == NativeStableCandidateTargetKind.CandidateAddressMinus0x08 &&
            target.Address == (nuint)0x10f8);
        Assert.Contains(targets.Targets, target =>
            target.Kind == NativeStableCandidateTargetKind.CandidateAddressPlus0x48 &&
            target.Address == (nuint)0x1148);
    }

    [Theory]
    [InlineData("A", 0x1100u)]
    [InlineData("B", 0x2100u)]
    public void ReverseReference_FindsDirectChecksumReference(string label, uint target)
    {
        NativeStableCandidateTargetSet targets = TargetSet(
            ("A", 0x1100u, 0x1150u),
            ("B", 0x2100u, 0x2150u));

        NativeStableBufferReferenceResult result = ScanReferences(
            0x5000,
            targets,
            (0, target));

        NativeStableReferenceObservation reference = Assert.Single(result.Observations);
        Assert.Equal(label, reference.TargetMapLabel);
        Assert.Equal(NativeStableCandidateTargetKind.ChecksumAddress, reference.TargetKind);
    }

    [Fact]
    public void ReverseReference_FindsFilenameReference()
    {
        NativeStableBufferReferenceResult result = ScanReferences(
            0x5000,
            TargetSet(("A", 0x1100u, 0x1150u)),
            (4, 0x1150u));

        NativeStableReferenceObservation reference = Assert.Single(result.Observations);
        Assert.Equal(NativeStableCandidateTargetKind.FilenameAddress, reference.TargetKind);
        Assert.Equal((nuint)0x5004, reference.SourceAddress);
    }

    [Fact]
    public void ReverseReference_FindsCandidateNeighborhoodReference()
    {
        NativeStableBufferReferenceResult result = ScanReferences(
            0x5000,
            TargetSet(("A", 0x1100u, 0x1150u)),
            (0, 0x1120u));

        NativeStableReferenceObservation reference = Assert.Single(result.Observations);
        Assert.Equal(NativeStableReferenceKind.CandidateNeighborhoodReference, reference.Kind);
        Assert.Equal(0x20, reference.TargetDeltaFromChecksum);
        Assert.Equal(-0x30, reference.TargetDeltaFromFilename);
    }

    [Fact]
    public void ReverseReference_FiltersSourceBytesInsideKnownStringPayload()
    {
        byte[] buffer = new byte[64];
        BitConverter.GetBytes(0x1100u).CopyTo(buffer, 0);
        NativeStableProbeSignal payload = SignalWithBytes(
            "A", NativeStableKnownSignalKind.Checksum, new byte[64], 0x5000);

        NativeStableBufferReferenceResult result = NativeStableReferenceDiscovery.ScanBuffer(
            buffer,
            0x5000,
            Region(0x5000, 0x1000),
            sizeof(uint),
            TargetSet(("A", 0x1100u, 0x1150u)),
            [payload]);

        Assert.Empty(result.Observations);
        Assert.Equal(1, result.FilteredStringPayloadPointerLikeValues);
    }

    [Fact]
    public void ReverseReference_ReportsMultipleSourcesAndNoReferenceCase()
    {
        NativeStableCandidateTargetSet targets = TargetSet(("A", 0x1100u, 0x1150u));
        NativeStableBufferReferenceResult multiple = ScanReferences(
            0x5000,
            targets,
            (0, 0x1100u),
            (8, 0x1100u));
        NativeStableBufferReferenceResult none = ScanReferences(
            0x6000,
            targets,
            (0, 0x9000u));

        Assert.Equal(2, multiple.Observations.Count);
        Assert.Empty(none.Observations);
    }

    [Fact]
    public void ReverseReference_NormalizesSourceRegionAndOffset()
    {
        NativeStableBufferReferenceResult first = ScanReferences(
            0x5000,
            TargetSet(("A", 0x1100u, 0x1150u)),
            (12, 0x1100u));
        NativeStableBufferReferenceResult second = ScanReferences(
            0x7000,
            TargetSet(("A", 0x1100u, 0x1150u)),
            (12, 0x1100u));

        NativeStableReferenceObservation firstReference = Assert.Single(first.Observations);
        NativeStableReferenceObservation secondReference = Assert.Single(second.Observations);
        Assert.Equal((nuint)0x5000, firstReference.SourceRegion.BaseAddress);
        Assert.Equal((nuint)0x7000, secondReference.SourceRegion.BaseAddress);
        Assert.Equal((nuint)12, firstReference.SourceOffsetWithinRegion);
        Assert.Equal(firstReference.SourceOffsetWithinRegion,
            secondReference.SourceOffsetWithinRegion);
    }

    [Fact]
    public void ReverseReference_UsesEightBytePointerWidthWhenRequested()
    {
        nuint targetAddress = nuint.Parse(
            "100001100",
            System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture);
        var target = new NativeStableCandidateTarget(
            "A", 0, NativeStableCandidateTargetKind.ChecksumAddress,
            targetAddress, targetAddress, targetAddress + 0x50);
        var targets = new NativeStableCandidateTargetSet([target], []);
        byte[] buffer = new byte[16];
        BitConverter.GetBytes(checked((ulong)targetAddress)).CopyTo(buffer, 8);
        NativeStableMemoryRegion sourceRegion = Region(0x5000, 0x1000);

        NativeStableBufferReferenceResult result = NativeStableReferenceDiscovery.ScanBuffer(
            buffer, 0x5000, sourceRegion, sizeof(ulong), targets, []);

        NativeStableReferenceObservation reference = Assert.Single(result.Observations);
        Assert.Equal((nuint)0x5008, reference.SourceAddress);
        Assert.Equal(targetAddress, reference.TargetAddress);
    }

    [Fact]
    public void OneLevelIndirection_FindsIntermediateButDoesNotRecurse()
    {
        NativeStableMemoryRegion region = Region(0x4000, 0x3000);
        NativeStableReferenceObservation firstLevel = Reference(0x6000, "A");
        byte[] memoryBytes = new byte[0x3000];
        BitConverter.GetBytes(0x6000u).CopyTo(memoryBytes, 0x1000);
        BitConverter.GetBytes(0x5000u).CopyTo(memoryBytes, 0);
        var memory = new SyntheticMemoryReader(
            bytes: new Dictionary<nuint, byte[]> { [(nuint)0x4000] = memoryBytes });
        var direct = new NativeStableReferenceDiscoveryResult(
            [firstLevel], [], [], 0);

        NativeStableReferenceDiscoveryResult result =
            NativeStableReferenceDiscovery.SearchOneIntermediateLevel(
                memory, [region], direct, []);

        NativeStableIntermediateReference reference = Assert.Single(
            result.IntermediateReferences);
        Assert.Equal((nuint)0x5000, reference.SourceAddress);
        Assert.Equal((nuint)0x6000, reference.IntermediateAddress);
        Assert.Equal(2, reference.Depth);
        Assert.DoesNotContain(result.IntermediateReferences,
            item => item.SourceAddress == (nuint)0x4000);
    }

    [Fact]
    public void SnapshotComparison_ClassifiesSwitchingAndStaticReferencesTogether()
    {
        NativeStableReferenceSnapshot a1 = Snapshot(
            (0x1000u, "A"), (0x2000u, "A"), (0x3000u, "B"));
        NativeStableReferenceSnapshot b = Snapshot(
            (0x1000u, "B"), (0x2000u, "A"), (0x3000u, "B"));
        NativeStableReferenceSnapshot a2 = Snapshot(
            (0x1000u, "A"), (0x2000u, "A"), (0x3000u, "B"));

        IReadOnlyList<NativeStableReferenceTransition> transitions =
            NativeStableReferenceSnapshotComparer.Compare(a1, b, a2);

        Assert.Contains(transitions, transition =>
            transition.SourceAddress == (nuint)0x1000 &&
            transition.Kind == NativeStableReferenceTransitionKind.SwitchingReference);
        Assert.Contains(transitions, transition =>
            transition.SourceAddress == (nuint)0x2000 &&
            transition.Kind == NativeStableReferenceTransitionKind.StaticAReference);
        Assert.Contains(transitions, transition =>
            transition.SourceAddress == (nuint)0x3000 &&
            transition.Kind == NativeStableReferenceTransitionKind.StaticBReference);
    }

    [Fact]
    public void SnapshotComparison_ClassifiesDisappearingAndMultipleTargets()
    {
        NativeStableReferenceSnapshot a1 = Snapshot(
            (0x1000u, "A"), (0x2000u, "A"), (0x2000u, "B"));
        NativeStableReferenceSnapshot b = Snapshot((0x2000u, "B"));
        NativeStableReferenceSnapshot a2 = Snapshot(
            (0x1000u, "A"), (0x2000u, "A"));

        IReadOnlyList<NativeStableReferenceTransition> transitions =
            NativeStableReferenceSnapshotComparer.Compare(a1, b, a2);

        Assert.Contains(transitions, transition =>
            transition.SourceAddress == (nuint)0x1000 &&
            transition.Kind == NativeStableReferenceTransitionKind.DisappearingReference);
        Assert.Contains(transitions, transition =>
            transition.SourceAddress == (nuint)0x2000 &&
            transition.Kind == NativeStableReferenceTransitionKind.MultipleTargetReference);
    }

    private NativeStableSongsPathResolver CreateSongsResolver()
    {
        string preference = Path.Combine(root, Guid.NewGuid().ToString("N"), "songs-path.txt");
        return new NativeStableSongsPathResolver(
            new SongsPathResolver(new FileSongsPathPreferenceStore(preference)));
    }

    private string CreateInstallation(string songsDirectoryName)
    {
        string install = Path.Combine(root, "osu-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(install);
        Directory.CreateDirectory(Path.Combine(install, songsDirectoryName));
        File.WriteAllBytes(Path.Combine(install, "osu!.exe"), []);
        return install;
    }

    private NativeStableMapValidationResult ValidMap(int processId, string path)
    {
        return NativeStableCurrentMapValidator.Validate(
            processId,
            songs,
            Path.GetFileName(mapFolder),
            Path.GetFileName(path),
            Checksum(path),
            verifyChecksum: true,
            DateTimeOffset.UtcNow);
    }

    private static string Checksum(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(MD5.HashData(stream)).ToLowerInvariant();
    }

    private static IReadOnlyList<NativeStableCorrelationCandidate> Correlate(
        IReadOnlyList<NativeStableProbeSignal> signals,
        IReadOnlyList<NativeStableMemoryRegion> regions,
        long relationshipWindow = NativeStableCandidateCorrelationAnalyzer.DefaultRelationshipWindow) =>
        NativeStableCandidateCorrelationAnalyzer.Analyze(
            signals,
            regions,
            relationshipWindow);

    private static NativeStableProbeSignal Signal(
        NativeStableKnownSignalKind kind,
        params uint[] addresses) =>
        LabeledSignal("SYNTHETIC", kind, addresses);

    private static NativeStableProbeSignal LabeledSignal(
        string mapLabel,
        NativeStableKnownSignalKind kind,
        params uint[] addresses) =>
        SignalWithBytes(mapLabel, kind, [0x41], addresses);

    private static NativeStableProbeSignal SignalWithBytes(
        string mapLabel,
        NativeStableKnownSignalKind kind,
        byte[] knownBytes,
        params uint[] addresses) =>
        new(
            mapLabel,
            $"{kind}-synthetic",
            kind,
            NativeStableTextEncoding.Utf16,
            knownBytes,
            addresses.Select(address => checked((nuint)address)).ToArray());

    private static NativeStablePreparedKnownMap PreparedMap(string label) =>
        new(
            label,
            "0123456789abcdef0123456789abcdef",
            $"{label}.osu",
            $"Folder-{label}",
            FullPath: null);

    private static IReadOnlyList<NativeStableKnownMapCapture> AnalyzeMapCaptures(
        IReadOnlyList<NativeStableProbeSignal> signals,
        IReadOnlyList<NativeStableMemoryRegion> regions,
        long relationshipWindow = NativeStableCandidateCorrelationAnalyzer.DefaultRelationshipWindow)
    {
        IReadOnlyList<NativeStableCorrelationCandidate> candidates =
            NativeStableCandidateCorrelationAnalyzer.Analyze(
                signals,
                regions,
                relationshipWindow);
        return NativeStablePoc.BuildKnownMapCaptures(
            [PreparedMap("A"), PreparedMap("B")],
            signals,
            candidates);
    }

    private static NativeStableCorrelationCandidate Candidate(
        string mapLabel,
        uint checksumAddress,
        uint filenameAddress)
    {
        NativeStableMemoryRegion region = Region(0x1000, 0x1000);
        return new(
            mapLabel,
            checksumAddress,
            filenameAddress,
            checked((long)filenameAddress - checksumAddress),
            NativeStableTextEncoding.Utf16,
            NativeStableTextEncoding.Utf16,
            region,
            region,
            []);
    }

    private static NativeStableCandidateTargetSet TargetSet(
        params (string Label, uint Checksum, uint Filename)[] candidates) =>
        NativeStableReferenceDiscovery.BuildTargetSet(
            candidates.Select(candidate => Candidate(
                candidate.Label,
                candidate.Checksum,
                candidate.Filename)).ToArray(),
            [Region(0x1000, 0x3000)]);

    private static NativeStableBufferReferenceResult ScanReferences(
        uint bufferAddress,
        NativeStableCandidateTargetSet targets,
        params (int Offset, uint Value)[] pointers)
    {
        var buffer = new byte[64];
        foreach ((int offset, uint value) in pointers)
        {
            BitConverter.GetBytes(value).CopyTo(buffer, offset);
        }

        NativeStableMemoryRegion region = Region(bufferAddress, 0x1000);
        return NativeStableReferenceDiscovery.ScanBuffer(
            buffer,
            bufferAddress,
            region,
            sizeof(uint),
            targets,
            []);
    }

    private static NativeStableReferenceObservation Reference(
        uint sourceAddress,
        string targetMapLabel)
    {
        uint regionBase = sourceAddress & 0xfffff000;
        return new(
            NativeStableReferenceKind.DirectReference,
            sourceAddress,
            0x1100,
            targetMapLabel,
            NativeStableCandidateTargetKind.ChecksumAddress,
            CandidateIndex: 0,
            Region(regionBase, 0x1000),
            sourceAddress - regionBase,
            SourceAlignment: 0,
            TargetDeltaFromChecksum: 0,
            TargetDeltaFromFilename: -0x50,
            OccurrenceCount: 1);
    }

    private static NativeStableReferenceSnapshot Snapshot(
        params (uint SourceAddress, string TargetMapLabel)[] references) =>
        new(
            Guid.NewGuid().ToString("N"),
            ExpectedSelectedMapLabel: null,
            references.Select(reference => Reference(
                reference.SourceAddress,
                reference.TargetMapLabel)).ToArray());

    private static NativeStableMemoryRegion Region(uint address, uint size) =>
        new(
            address,
            size,
            NativeStableInterop.MemoryCommit,
            Protect: 0x04,
            Type: 0x20000);

    private sealed class SyntheticMemoryReader : INativeStableMemoryReader
    {
        private readonly IReadOnlyDictionary<nuint, nuint> pointers;
        private readonly IReadOnlyDictionary<nuint, byte[]> bytes;

        internal SyntheticMemoryReader(
            IReadOnlyDictionary<nuint, nuint>? pointers = null,
            IReadOnlyDictionary<nuint, byte[]>? bytes = null)
        {
            this.pointers = pointers ?? new Dictionary<nuint, nuint>();
            this.bytes = bytes ?? new Dictionary<nuint, byte[]>();
        }

        public bool Is32Bit => true;

        public NativeStableMemoryReadResult ReadBytes(nuint address, int count)
        {
            if (!bytes.TryGetValue(address, out byte[]? value) || value.Length < count)
            {
                return NativeStableMemoryReadResult.Failed;
            }

            byte[] result = value[..count];
            return new(result, Complete: true, result.Length);
        }

        public bool TryReadPointer(nuint address, out nuint value) =>
            pointers.TryGetValue(address, out value);
    }
}
