using System.Collections.Generic;

namespace BeatInsight
{
    public class CommunityTag
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";

        public int Votes { get; set; }

        public string Description { get; set; } = "";
        public List<string> Concepts { get; set; } = [];

        public List<string> MatchedConcepts { get; set; } = [];

        public double MatchScore { get; set; }

        public double VoteWeight { get; set; }

        public double VoteContribution { get; set; }

        public string Verdict { get; set; } = "";
    }
}
