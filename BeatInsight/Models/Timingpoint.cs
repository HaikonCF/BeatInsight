using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BeatInsight.Models;

public class TimingPoint
{
    public double Time { get; set; }

    public double BeatLength { get; set; }

    public bool Uninherited { get; set; }
}