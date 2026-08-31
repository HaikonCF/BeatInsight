using BeatInsight.Services;
using System.Linq;
namespace BeatInsight.Models;

public class Beatmap
{

    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Creator { get; set; } = "";
    public string Version { get; set; } = "";
    public List<CommunityTag> CommunityTags { get; set; } = new();
    public string CommunityTagsDisplay =>
       CommunityTags.Count > 0
           ? string.Join(
               ", ",
               CommunityTags.Select(
                   tag => $"{tag.Name} ({tag.Votes})"))
           : "None";

    public bool HasCommunityTag(string tag)
    {
        return CommunityTags.Any(
            t => string.Equals(
                t.Name,
                tag,
                StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Star Rating calculé par le système de difficulté d'osu!.
    /// </summary>
    public double OsuStarRating { get; set; }

    /// <summary>
    /// Rating de difficulté propre à BeatInsight.
    /// Ne représente pas le Star Rating officiel d'osu!.
    /// </summary>
    public double BeatInsightRating { get; set; }
    public TimeSpan Length { get; set; }
    public string LengthDisplay =>
        Length.ToString(@"m\:ss");
    public int BPM { get; set; }
    public int MaxCombo { get; set; }

    public double AR { get; set; }
    public double OD { get; set; }
    public double CS { get; set; }
    public double HP { get; set; }

    public List<TimingPoint> TimingPoints { get; set; } = new();

    public List<HitObject> HitObjects { get; set; } = new();
    public GameplayProfile GameplayProfile { get; set; } = new();
    public int CircleCount { get; set; }
    public int SliderCount { get; set; }
    public int SpinnerCount { get; set; }
    public double SliderMultiplier { get; set; }
    public double SliderTickRate { get; set; }
    internal MovementAnalysis MovementAnalysis { get; set; } = new MovementAnalysis();
    public GameplayTagComparisonResult? TagComparison { get; set; }

}
