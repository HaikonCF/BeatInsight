using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BeatInsight.Models
{
    public class HitObject
    {
        public int Type { get; set; }
        public int Time { get; set; }
        public int Slides { get; set; }
        public double Length { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public string SliderCurveType { get; set; } = "";
        public List<SliderControlPoint> SliderControlPoints { get; set; } = new();
        public SliderControlPoint SliderEndPosition { get; set; } = new();

    }

    public class SliderControlPoint
    {
        public int X { get; set; }
        public int Y { get; set; }
    }
}
