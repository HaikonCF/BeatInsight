using System.Collections.Generic;

namespace BeatInsight.Models
{
    internal class MovementAnalysis
    {
        public double TotalDistance { get; set; }

        public double AverageDistance { get; set; }

        public double MaxDistance { get; set; }

        public double TotalMovementSpeed { get; set; }

        public double AverageSpeed { get; set; }

        public double MaxSpeed { get; set; }

        public int MovementCount { get; set; }

        public List<double> Speeds { get; set; } = new List<double>();

        public int[] SpeedIntervals { get; set; } = new int[7];
    }
}