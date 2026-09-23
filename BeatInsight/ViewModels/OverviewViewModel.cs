using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using BeatInsight.Models.AnalysisCache;
using BeatInsight.Models.Identity;
using BeatInsight.Models.Sections;
using BeatInsight.Models.SkillPressure;

namespace BeatInsight.ViewModels;

public sealed class OverviewViewModel : ViewModelBase
{
    private bool hasBeatmap;
    private bool isLoading;
    private string loadingMessage = "Ready";
    private string? errorMessage;
    private bool hasError;

    // Metadata
    private string title = "—";
    private string artist = "—";
    private string version = "—";
    private string creator = "—";
    private int bpm;
    private string bpmDisplay = "—";
    private string lengthDisplay = "--:--";
    private int maxCombo;
    private string maxComboDisplay = "—";
    private double ar;
    private double od;
    private double cs;
    private double hp;
    private int circleCount;
    private int sliderCount;
    private int spinnerCount;
    private string? backgroundPath;
    private bool hasBackground;
    private int? beatmapId;
    private int? beatmapSetId;
    private string? osuBeatmapUrl;

    // Ratings
    private double osuStarRating;
    private string osuStarRatingDisplay = "-- ★";
    private double beatInsightRatingScore;
    private string beatInsightRatingDisplay = "--";
    private double beatInsightRatingConfidence;
    private string beatInsightRatingConfidenceDisplay = "--";
    private double historicalRating;

    // Analysis Status
    private string analysisSourceDisplay = "No active map";
    private bool wasCacheHit;
    private string formattedAnalysisSummary = string.Empty;
    private string spikesSummaryText = "No significant difficulty spikes detected";
    private bool hasSpikes;
    private bool hasCommunityTags;

    public bool HasBeatmap
    {
        get => hasBeatmap;
        set => SetProperty(ref hasBeatmap, value);
    }

    public bool IsLoading
    {
        get => isLoading;
        set => SetProperty(ref isLoading, value);
    }

    public string LoadingMessage
    {
        get => loadingMessage;
        set => SetProperty(ref loadingMessage, value);
    }

    public string? ErrorMessage
    {
        get => errorMessage;
        set
        {
            if (SetProperty(ref errorMessage, value))
            {
                HasError = !string.IsNullOrWhiteSpace(value);
            }
        }
    }

    public bool HasError
    {
        get => hasError;
        set => SetProperty(ref hasError, value);
    }

    public string Title
    {
        get => title;
        set => SetProperty(ref title, value);
    }

    public string Artist
    {
        get => artist;
        set => SetProperty(ref artist, value);
    }

    public string Version
    {
        get => version;
        set => SetProperty(ref version, value);
    }

    public string Creator
    {
        get => creator;
        set => SetProperty(ref creator, value);
    }

    public int Bpm
    {
        get => bpm;
        set => SetProperty(ref bpm, value);
    }

    public string BpmDisplay
    {
        get => bpmDisplay;
        set => SetProperty(ref bpmDisplay, value);
    }

    public string LengthDisplay
    {
        get => lengthDisplay;
        set => SetProperty(ref lengthDisplay, value);
    }

    public int MaxCombo
    {
        get => maxCombo;
        set => SetProperty(ref maxCombo, value);
    }

    public string MaxComboDisplay
    {
        get => maxComboDisplay;
        set => SetProperty(ref maxComboDisplay, value);
    }

    public double Ar
    {
        get => ar;
        set => SetProperty(ref ar, value);
    }

    public double Od
    {
        get => od;
        set => SetProperty(ref od, value);
    }

    public double Cs
    {
        get => cs;
        set => SetProperty(ref cs, value);
    }

    public double Hp
    {
        get => hp;
        set => SetProperty(ref hp, value);
    }

    public int CircleCount
    {
        get => circleCount;
        set => SetProperty(ref circleCount, value);
    }

    public int SliderCount
    {
        get => sliderCount;
        set => SetProperty(ref sliderCount, value);
    }

    public int SpinnerCount
    {
        get => spinnerCount;
        set => SetProperty(ref spinnerCount, value);
    }

    public string? BackgroundPath
    {
        get => backgroundPath;
        set
        {
            if (SetProperty(ref backgroundPath, value))
            {
                HasBackground = !string.IsNullOrWhiteSpace(value);
            }
        }
    }

    public bool HasBackground
    {
        get => hasBackground;
        set => SetProperty(ref hasBackground, value);
    }

    public int? BeatmapId
    {
        get => beatmapId;
        set => SetProperty(ref beatmapId, value);
    }

    public int? BeatmapSetId
    {
        get => beatmapSetId;
        set => SetProperty(ref beatmapSetId, value);
    }

    public string? OsuBeatmapUrl
    {
        get => osuBeatmapUrl;
        set => SetProperty(ref osuBeatmapUrl, value);
    }

    public double OsuStarRating
    {
        get => osuStarRating;
        set => SetProperty(ref osuStarRating, value);
    }

    public string OsuStarRatingDisplay
    {
        get => osuStarRatingDisplay;
        set => SetProperty(ref osuStarRatingDisplay, value);
    }

    public double BeatInsightRatingScore
    {
        get => beatInsightRatingScore;
        set => SetProperty(ref beatInsightRatingScore, value);
    }

    public string BeatInsightRatingDisplay
    {
        get => beatInsightRatingDisplay;
        set => SetProperty(ref beatInsightRatingDisplay, value);
    }

    public double BeatInsightRatingConfidence
    {
        get => beatInsightRatingConfidence;
        set => SetProperty(ref beatInsightRatingConfidence, value);
    }

    public string BeatInsightRatingConfidenceDisplay
    {
        get => beatInsightRatingConfidenceDisplay;
        set => SetProperty(ref beatInsightRatingConfidenceDisplay, value);
    }

    public double HistoricalRating
    {
        get => historicalRating;
        set => SetProperty(ref historicalRating, value);
    }

    public string AnalysisSourceDisplay
    {
        get => analysisSourceDisplay;
        set => SetProperty(ref analysisSourceDisplay, value);
    }

    public bool WasCacheHit
    {
        get => wasCacheHit;
        set => SetProperty(ref wasCacheHit, value);
    }

    public string FormattedAnalysisSummary
    {
        get => formattedAnalysisSummary;
        set => SetProperty(ref formattedAnalysisSummary, value);
    }

    public string SpikesSummaryText
    {
        get => spikesSummaryText;
        set => SetProperty(ref spikesSummaryText, value);
    }

    public bool HasSpikes
    {
        get => hasSpikes;
        set => SetProperty(ref hasSpikes, value);
    }

    public bool HasCommunityTags
    {
        get => hasCommunityTags;
        set => SetProperty(ref hasCommunityTags, value);
    }

    public bool HasSections => Sections.Count > 0;
    public bool HasDifficultyData => DifficultyPoints.Count > 0;

    public OverviewViewModel()
    {
        Sections.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasSections));
        DifficultyPoints.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasDifficultyData));
    }

    public ObservableCollection<SkillMetricViewModel> SkillMetrics { get; } = [];
    public ObservableCollection<SkillMetricViewModel> SkillPressureMetrics { get; } = [];
    public ObservableCollection<CommunityTag> CommunityTags { get; } = [];
    public IdentityViewModel Identity { get; } = new();
    public PatternsViewModel Patterns { get; } = new();
    public DifficultyAnalysisViewModel Difficulty { get; } = new();
    public ObservableCollection<SectionTimelineItemViewModel> Sections { get; } = [];
    public ObservableCollection<DifficultyPointViewModel> DifficultyPoints { get; } = [];
    public BiaAssistantViewModel Assistant { get; } = new();

    public void SetLoading(string message)
    {
        HasBeatmap = false;
        IsLoading = true;
        LoadingMessage = message;
        ErrorMessage = null;
        Assistant.SetState(
            BiaVisualState.Analyzing,
            "Analyzing",
            message);
    }

    public void SetError(string error)
    {
        HasBeatmap = false;
        IsLoading = false;
        ErrorMessage = error;
        Assistant.SetState(
            BiaVisualState.Error,
            "Analysis Error",
            error);
    }

    public void Clear()
    {
        HasBeatmap = false;
        IsLoading = false;
        ErrorMessage = null;
        Title = "—";
        Artist = "—";
        Version = "—";
        Creator = "—";
        Bpm = 0;
        BpmDisplay = "—";
        LengthDisplay = "--:--";
        MaxCombo = 0;
        MaxComboDisplay = "—";
        Ar = 0;
        Od = 0;
        Cs = 0;
        Hp = 0;
        CircleCount = 0;
        SliderCount = 0;
        SpinnerCount = 0;
        BackgroundPath = null;
        BeatmapId = null;
        BeatmapSetId = null;
        OsuBeatmapUrl = null;
        OsuStarRating = 0;
        OsuStarRatingDisplay = "-- ★";
        BeatInsightRatingScore = 0;
        BeatInsightRatingDisplay = "--";
        BeatInsightRatingConfidence = 0;
        BeatInsightRatingConfidenceDisplay = "--";
        HistoricalRating = 0;
        AnalysisSourceDisplay = "No active map";
        WasCacheHit = false;
        FormattedAnalysisSummary = string.Empty;
        SpikesSummaryText = "No significant difficulty spikes detected";
        HasSpikes = false;
        CommunityTags.Clear();
        HasCommunityTags = false;

        SkillMetrics.Clear();
        SkillPressureMetrics.Clear();
        Identity.PrimaryStructure = "—";
        Identity.SecondaryStructure = "—";
        Identity.HasSecondary = false;
        Identity.GlobalConfidence = 0;
        Identity.GlobalConfidenceDisplay = "0%";
        Identity.MainSkills.Clear();
        Identity.MainPatterns.Clear();
        Patterns.Clear();
        Difficulty.Clear();
        Sections.Clear();
        DifficultyPoints.Clear();
        Assistant.Message = "Select or play a beatmap to see my detailed analytical breakdown!";
        Assistant.SetState(BiaVisualState.Idle, "Ready");
    }

    public void UpdateCommunityTags(IEnumerable<CommunityTag>? communityTags)
    {
        CommunityTags.Clear();

        if (communityTags is not null)
        {
            foreach (CommunityTag tag in communityTags)
            {
                if (!string.IsNullOrWhiteSpace(tag.Name))
                {
                    CommunityTags.Add(tag);
                }
            }
        }

        HasCommunityTags = CommunityTags.Count > 0;
    }

    public void LoadSnapshot(
        BeatmapAnalysisSnapshot? snapshot,
        bool isCacheHit = false,
        TimeSpan? loadTime = null,
        string? backgroundPath = null,
        bool includeSecondaryDetails = true,
        bool setReady = true)
    {
        if (snapshot is null)
        {
            Clear();
            return;
        }

        ErrorMessage = null;
        HasBeatmap = true;
        WasCacheHit = isCacheHit;
        BackgroundPath = backgroundPath;
        CommunityTags.Clear();
        HasCommunityTags = false;

        // 1. Metadata
        var meta = snapshot.Metadata;
        if (meta is not null)
        {
            Title = string.IsNullOrWhiteSpace(meta.Title) ? "Unknown Title" : meta.Title;
            Artist = string.IsNullOrWhiteSpace(meta.Artist) ? "Unknown Artist" : meta.Artist;
            Version = string.IsNullOrWhiteSpace(meta.Version) ? "Normal" : meta.Version;
            Creator = string.IsNullOrWhiteSpace(meta.Creator) ? "Unknown" : meta.Creator;
            Bpm = meta.Bpm;
            BpmDisplay = FormatBpm(meta);
            LengthDisplay = TimeSpan.FromTicks(meta.LengthTicks).ToString(@"mm\:ss");
            MaxCombo = meta.MaxCombo;
            MaxComboDisplay = meta.OsuMaxCombo is > 0
                ? meta.OsuMaxCombo.Value.ToString(CultureInfo.InvariantCulture)
                : "—";
            Ar = meta.ApproachRate;
            Od = meta.OverallDifficulty;
            Cs = meta.CircleSize;
            Hp = meta.HpDrain;
            CircleCount = meta.CircleCount;
            SliderCount = meta.SliderCount;
            SpinnerCount = meta.SpinnerCount;
            BeatmapId = meta.BeatmapId;
            BeatmapSetId = meta.BeatmapSetId;
            OsuStarRating = meta.OsuStarRating;
            OsuStarRatingDisplay = meta.OsuStarRating > 0
                ? $"{meta.OsuStarRating.ToString("F2", CultureInfo.InvariantCulture)} ★"
                : "-- ★";
            HistoricalRating = meta.HistoricalBeatInsightRating;

            if (meta.BeatmapId.HasValue && meta.BeatmapId.Value > 0)
            {
                OsuBeatmapUrl = $"https://osu.ppy.sh/b/{meta.BeatmapId.Value}";
            }
            else
            {
                OsuBeatmapUrl = null;
            }
        }

        // 2. Rating V2
        if (snapshot.RatingV2 is not null)
        {
            BeatInsightRatingScore = snapshot.RatingV2.Score;
            BeatInsightRatingDisplay = snapshot.RatingV2.Score.ToString("F1", CultureInfo.InvariantCulture);
            BeatInsightRatingConfidence = snapshot.RatingV2.Confidence;
            BeatInsightRatingConfidenceDisplay = snapshot.RatingV2.Confidence.ToString("P0", CultureInfo.InvariantCulture);
        }
        else
        {
            BeatInsightRatingScore = 0;
            BeatInsightRatingDisplay = "--";
            BeatInsightRatingConfidence = 0;
            BeatInsightRatingConfidenceDisplay = "--";
        }
        Difficulty.LoadRating(snapshot.RatingV2);

        // 3. Analysis Source
        if (isCacheHit)
        {
            string timeStr = loadTime.HasValue ? $" ({loadTime.Value.TotalMilliseconds:F1} ms)" : "";
            AnalysisSourceDisplay = $"Persistent Cache HIT{timeStr}";
        }
        else
        {
            AnalysisSourceDisplay = "Fresh Analysis (Deterministic)";
        }

        // 4. Skills Bar Mapping
        SkillMetrics.Clear();
        double aimVal = snapshot.SkillPressureV2?.AimControl.Score ?? snapshot.GameplayProfile?.AimScore ?? 0.0;
        double streamVal = (snapshot.GameplayProfile?.StreamRatio ?? 0.0) * 100.0;
        double readVal = snapshot.SkillPressureV2?.ReadingPressure.Score ?? snapshot.GameplayProfile?.ReadingScore ?? 0.0;
        double jumpVal = (snapshot.GameplayProfile?.JumpRatio ?? 0.0) * 100.0;
        double techVal = snapshot.GameplayProfile?.TechScore ?? 0.0;
        double burstVal = (snapshot.GameplayProfile?.BurstRatio ?? 0.0) * 100.0;
        // SPEED IS STRICTLY BOUND TO GlobalTripletSpeed.SpeedPercent
        double speedVal = snapshot.GlobalTripletSpeed?.SpeedPercent ?? 0.0;
        double staminaVal = snapshot.SkillPressureV2?.Stamina.Score ?? 0.0;

        SkillMetrics.Add(new SkillMetricViewModel("Aim", aimVal, $"{aimVal:F0}%", "Cursor trajectory & spatial displacement", true, "StructureJump"));
        SkillMetrics.Add(new SkillMetricViewModel("Stream", streamVal, $"{streamVal:F0}%", "Continuous high-density note sequences", true, "StructureStream"));
        SkillMetrics.Add(new SkillMetricViewModel("Reading", readVal, $"{readVal:F0}%", "Visual density and overlap complexity", true, "StructureClassicMixed"));
        SkillMetrics.Add(new SkillMetricViewModel("Jump", jumpVal, $"{jumpVal:F0}%", "Discrete wide/sharp directional jumps", true, "StructureJump"));
        SkillMetrics.Add(new SkillMetricViewModel("Tech", techVal, $"{techVal:F0}%", "Complex slider movement & rhythm awkwardness", true, "StructureTech"));
        SkillMetrics.Add(new SkillMetricViewModel("Burst", burstVal, $"{burstVal:F0}%", "Short burst acceleration bursts", true, "StructureStream"));
        SkillMetrics.Add(new SkillMetricViewModel("Speed", speedVal, $"{speedVal:F0}%", "Global Triplet & High-BPM speed pressure", true, "AccentMagenta"));
        SkillMetrics.Add(new SkillMetricViewModel("Stamina", staminaVal, $"{staminaVal:F0}%", "Sustained physical tapping endurance", true, "AccentMagenta"));

        LoadSkillPressureMetrics(snapshot.SkillPressureV2);

        // 5. Identity V3
        Identity.MainSkills.Clear();
        Identity.MainPatterns.Clear();
        Patterns.Load(snapshot.PatternSummaries);
        if (snapshot.IdentityV3 is not null)
        {
            Identity.PrimaryStructure = snapshot.IdentityV3.PrimaryStructure.ToString().ToUpperInvariant();
            Identity.PrimaryStructureColorKey = GetStructureColorKey(snapshot.IdentityV3.PrimaryStructure.ToString());

            if (snapshot.IdentityV3.SecondaryStructure.HasValue)
            {
                Identity.SecondaryStructure = snapshot.IdentityV3.SecondaryStructure.Value.ToString().ToUpperInvariant();
                Identity.SecondaryStructureColorKey = GetStructureColorKey(snapshot.IdentityV3.SecondaryStructure.Value.ToString());
                Identity.HasSecondary = true;
            }
            else
            {
                Identity.SecondaryStructure = "—";
                Identity.SecondaryStructureColorKey = "StructureClassicMixed";
                Identity.HasSecondary = false;
            }

            Identity.GlobalConfidence = snapshot.IdentityV3.GlobalIdentityConfidence;
            Identity.GlobalConfidenceDisplay = snapshot.IdentityV3.GlobalIdentityConfidence.ToString("P0", CultureInfo.InvariantCulture);

            if (snapshot.IdentityV3.MainSkills is not null)
            {
                foreach (var skill in snapshot.IdentityV3.MainSkills)
                {
                    Identity.MainSkills.Add(new MainSkillChipViewModel(
                        skill.Name,
                        skill.Score,
                        $"{skill.Score:F0}",
                        skill.Status.ToString()));
                }
            }

            var patternSource = snapshot.IdentityV3.MainPatterns ?? snapshot.PatternSummaries;
            if (patternSource is not null)
            {
                foreach (var pat in patternSource)
                {
                    int occurrenceEstimate = (int)Math.Round(pat.Coverage * 100.0);
                    Identity.MainPatterns.Add(new MainPatternChipViewModel(
                        pat.Name,
                        occurrenceEstimate,
                        pat.Coverage,
                        $"{(pat.Coverage * 100.0):F0}% coverage"));
                }
            }
        }
        else
        {
            Identity.PrimaryStructure = "—";
            Identity.SecondaryStructure = "—";
            Identity.HasSecondary = false;
            Identity.GlobalConfidence = 0;
            Identity.GlobalConfidenceDisplay = "0%";
        }

        Sections.Clear();
        DifficultyPoints.Clear();
        Difficulty.LoadSections(Array.Empty<SectionTimelineItemViewModel>());
        HasSpikes = false;
        SpikesSummaryText = "No section data available";
        Assistant.Name = "Bia-chan";
        Assistant.Title = "Your Analysis Assistant";
        Assistant.StatusBadge = isCacheHit ? "Cached Analysis" : "Fresh Analysis";
        Assistant.Message = "Analysis ready.";
        Assistant.State = BiaVisualState.Idle;
        FormattedAnalysisSummary = string.Empty;

        if (includeSecondaryDetails)
            LoadSecondarySnapshotDetails(snapshot, isCacheHit);

        if (setReady)
            SetReady();
    }

    public void SetReady() => IsLoading = false;

    public void LoadSecondarySnapshotDetails(
        BeatmapAnalysisSnapshot snapshot,
        bool isCacheHit)
    {
        // 6. Section Analysis V2 & Spikes
        Sections.Clear();
        DifficultyPoints.Clear();
        var secAnalysis = snapshot.SectionAnalysisV2;
        if (secAnalysis?.Sections is { Count: > 0 })
        {
            foreach (var sec in secAnalysis.Sections)
            {
                string structName = sec.DominantStructure.ToString();
                string colorKey = GetStructureColorKey(structName);
                string startDisplay = TimeSpan.FromMilliseconds(sec.StartTime).ToString(@"mm\:ss");
                string endDisplay = TimeSpan.FromMilliseconds(sec.EndTime).ToString(@"mm\:ss");
                string dominantSignal = GetDominantSignalName(sec);
                string tooltip = $"Section #{sec.SectionIndex + 1}\nTime: {startDisplay} - {endDisplay}\nStructure: {structName}\nLocal Difficulty: {sec.LocalDifficultyScore:F1}/100\nDominant Signal: {dominantSignal}";


                Sections.Add(new SectionTimelineItemViewModel
                {
                    Index = sec.SectionIndex,
                    StartTimeMs = sec.StartTime,
                    EndTimeMs = sec.EndTime,

                    TimeRangeDisplay =
        $"{TimeSpan.FromMilliseconds(sec.StartTime):mm\\:ss} - {TimeSpan.FromMilliseconds(sec.EndTime):mm\\:ss}",

                    Structure = structName,
                    StructureColorKey = colorKey,

                    LocalDifficulty = sec.LocalDifficultyScore,
                    LocalDifficultyDisplay = sec.LocalDifficultyScore.ToString(
                        "F1", CultureInfo.InvariantCulture),

                    DominantSignal = dominantSignal,

                    JumpScore = sec.JumpScore,
                    StreamScore = sec.StreamScore,
                    TechScore = sec.TechScore,
                    BurstScore = sec.BurstScore,

                    IsSpike = sec.Spike.IsSpike,

                    TooltipText = tooltip
                });

                bool isSpike = sec.Spike.IsSpike;
                string? spikeDesc = isSpike ? $"Difficulty Spike #{sec.SectionIndex + 1}: {sec.LocalDifficultyScore:F1} ({structName})" : null;

                if (double.IsFinite(sec.LocalDifficultyScore))
                    DifficultyPoints.Add(new DifficultyPointViewModel(
                    sec.SectionIndex,
                    (sec.StartTime + sec.EndTime) / 2.0,
                    startDisplay,
                    sec.LocalDifficultyScore,
                    isSpike,
                    spikeDesc));
            }

            if (secAnalysis.DifficultySpikeCount > 0)
            {
                HasSpikes = true;
                SpikesSummaryText = secAnalysis.DifficultySpikeCount == 1
                    ? "1 significant difficulty spike detected"
                    : $"{secAnalysis.DifficultySpikeCount} significant difficulty spikes detected";
            }
            else
            {
                HasSpikes = false;
                SpikesSummaryText = "No significant difficulty spikes detected";
            }
        }
        else
        {
            HasSpikes = false;
            SpikesSummaryText = "No section data available";
        }

        Difficulty.LoadSections(Sections);

        // 7. Bia-chan Assistant Message Generation (Deterministic)
        Assistant.Name = "Bia-chan";
        Assistant.Title = "Your Analysis Assistant";
        Assistant.StatusBadge = isCacheHit ? "Cached Analysis" : "Fresh Analysis";
        Assistant.Message = GenerateBiaCommentary(snapshot);
        Assistant.State = BiaVisualState.Idle;

        // 8. Formatted Summary for clipboard
        FormattedAnalysisSummary = GenerateAnalysisSummary(snapshot);
    }

    private static string GetDominantSignalName(BeatInsight.Models.Sections.GameplaySectionV2Analysis section)
    {
        (string Name, double Score)[] signals =
        [
            ("Jump", section.JumpScore),
            ("Stream", section.StreamScore),
            ("Tech", section.TechScore),
            ("Burst", section.BurstScore)
        ];

        (string Name, double Score) dominant = signals
            .Where(signal => double.IsFinite(signal.Score))
            .OrderByDescending(signal => signal.Score)
            .FirstOrDefault();

        return dominant.Score > 0 ? dominant.Name : "None";
    }

    private void LoadSkillPressureMetrics(SkillPressureProfile? pressure)
    {
        SkillPressureMetrics.Clear();
        AddSkillPressureMetric("Stamina", pressure?.Stamina, "AccentMagenta");
        AddSkillPressureMetric("Endurance", pressure?.Endurance, "StructureStream");
        AddSkillPressureMetric("Finger Control", pressure?.FingerControl, "StructureTech");
        AddSkillPressureMetric("Burst Control", pressure?.BurstControl, "StructureStream");
        AddSkillPressureMetric("Aim Control", pressure?.AimControl, "StructureJump");
        AddSkillPressureMetric("Precision", pressure?.Precision, "StructureTech");
        AddSkillPressureMetric("Reading Pressure", pressure?.ReadingPressure, "StructureClassicMixed");
        AddSkillPressureMetric("Rhythm Complexity", pressure?.RhythmComplexity, "StructureTech");
    }

    private void AddSkillPressureMetric(
        string name,
        SkillPressureMetric? metric,
        string accentKey)
    {
        bool isAvailable = metric is { Available: true, Score: not null }
            && double.IsFinite(metric.Score.Value);
        double value = isAvailable ? metric!.Score!.Value : 0.0;
        string displayValue = isAvailable
            ? $"{value:F0}%"
            : "N/A";
        string description = isAvailable
            ? $"Skill Pressure V2 · {(metric!.Reliable ? "Reliable" : "Partial")}"
            : string.IsNullOrWhiteSpace(metric?.Limitation)
                ? "Skill Pressure V2 · Unavailable"
                : metric.Limitation;

        SkillPressureMetrics.Add(new SkillMetricViewModel(
            name,
            value,
            displayValue,
            description,
            isAvailable,
            accentKey));
    }

    private static string GetStructureColorKey(string structureName) => structureName.ToUpperInvariant() switch
    {
        "STREAM" => "StructureStream",
        "JUMP" => "StructureJump",
        "TECH" => "StructureTech",
        "CLASSICMIXED" or "CLASSIC_MIXED" or "CLASSIC" or "MIXED" => "StructureClassicMixed",
        _ => "StructureClassicMixed"
    };

    private static string GenerateBiaCommentary(BeatmapAnalysisSnapshot snapshot)
    {
        var sb = new StringBuilder();
        string primary = snapshot.IdentityV3?.PrimaryStructure.ToString() ?? "Mixed";
        string? secondary = snapshot.IdentityV3?.SecondaryStructure?.ToString();
        double rating = snapshot.RatingV2?.Score ?? 0.0;
        double speed = snapshot.GlobalTripletSpeed?.SpeedPercent ?? 0.0;
        double stamina = snapshot.SkillPressureV2?.Stamina.Score ?? 0.0;
        int spikeCount = snapshot.SectionAnalysisV2?.DifficultySpikeCount ?? 0;

        sb.Append($"This beatmap is primarily characterized by **{primary}** gameplay");
        if (!string.IsNullOrWhiteSpace(secondary))
        {
            sb.Append($" with **{secondary}** secondary motifs");
        }
        sb.Append($". Overall BeatInsight Rating is **{rating:F1}**.");

        if (speed >= 60.0)
        {
            sb.Append($" High speed pressure is active with **{speed:F0}%** Triplet Speed demand.");
        }
        else if (stamina >= 50.0)
        {
            sb.Append($" Sustained stamina endurance is required (**{stamina:F0}%** stamina score).");
        }

        if (spikeCount > 0)
        {
            sb.Append($" Watch out for {spikeCount} pronounced difficulty spike(s) across the timeline!");
        }
        else
        {
            sb.Append(" Difficulty distribution remains relatively balanced throughout.");
        }

        return sb.ToString();
    }

    private static string FormatBpm(BeatmapMetadataSnapshot metadata)
    {
        static string Rounded(double value) =>
            Math.Round(value, 0, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture);

        if (metadata.PrimaryBpm is not > 0 || !double.IsFinite(metadata.PrimaryBpm.Value))
            return "—";

        if (metadata.IsVariableBpm == true
            && metadata.MinBpm is > 0 && double.IsFinite(metadata.MinBpm.Value)
            && metadata.MaxBpm is > 0 && double.IsFinite(metadata.MaxBpm.Value))
        {
            return $"{Rounded(metadata.MinBpm.Value)}–{Rounded(metadata.MaxBpm.Value)} BPM";
        }

        return $"{Rounded(metadata.PrimaryBpm.Value)} BPM";
    }

    private static string GenerateAnalysisSummary(BeatmapAnalysisSnapshot snapshot)
    {
        var meta = snapshot.Metadata;
        var identity = snapshot.IdentityV3;
        var rating = snapshot.RatingV2;
        var speed = snapshot.GlobalTripletSpeed;

        var sb = new StringBuilder();
        sb.AppendLine($"=== BeatInsight Analysis ===");
        if (meta is not null)
        {
            sb.AppendLine($"Map: {meta.Artist} - {meta.Title} [{meta.Version}] by {meta.Creator}");
            sb.AppendLine($"BPM: {FormatBpm(meta)} | Length: {TimeSpan.FromTicks(meta.LengthTicks):mm\\:ss} | CS: {meta.CircleSize:F1} | AR: {meta.ApproachRate:F1} | OD: {meta.OverallDifficulty:F1} | HP: {meta.HpDrain:F1}");
            sb.AppendLine($"Max Combo: {(meta.OsuMaxCombo is > 0 ? meta.OsuMaxCombo.Value.ToString(CultureInfo.InvariantCulture) : "—")}");
            sb.AppendLine($"osu! Star Rating: {meta.OsuStarRating:F2} ★");
        }
        if (rating is not null)
        {
            sb.AppendLine($"BeatInsight Rating V2: {rating.Score:F1} (Confidence: {rating.Confidence:P0})");
        }
        if (identity is not null)
        {
            sb.AppendLine($"Primary Structure: {identity.PrimaryStructure} | Secondary: {identity.SecondaryStructure?.ToString() ?? "None"} | Confidence: {identity.GlobalIdentityConfidence:P0}");
        }
        if (speed is not null)
        {
            sb.AppendLine($"Triplet Speed: {speed.SpeedPercent:F0}%");
        }
        return sb.ToString();
    }
}
