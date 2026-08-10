using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BeatInsight.Models;

public class GameplayStyleProfile
{
    public string PrimaryStyle { get; set; } = "Balanced";

    public double AimInfluence { get; set; }

    public double SpeedInfluence { get; set; }

    public double TechInfluence { get; set; }

    public double ReadInfluence { get; set; }

    public string Description { get; set; } = "";
}