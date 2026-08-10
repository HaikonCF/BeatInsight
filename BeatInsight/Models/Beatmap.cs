using BeatInsight.Parser;

namespace BeatInsight.Models;

public class Beatmap
{
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Creator { get; set; } = "";
    public string Version { get; set; } = "";
    public double StarRating { get; set; }
    public TimeSpan Lenght { get; set; }
    public string LengthDisplay { get; set; } = "";
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

}
