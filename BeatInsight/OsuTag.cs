namespace BeatInsight
{
    public class OsuTag
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";

        public int? RulesetId { get; set; }

        public string Description { get; set; } = "";
        public int Votes { get; set; }
    }
}
