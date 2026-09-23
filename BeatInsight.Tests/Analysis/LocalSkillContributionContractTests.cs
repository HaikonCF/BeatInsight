using BeatInsight.Analysis;
using BeatInsight.Models;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

public sealed class LocalSkillContributionContractTests(ITestOutputHelper output)
{
    private static readonly ExpectedGlobal[] Baselines =
    [
        new("Tower Of Heaven [Extra].osu",
            2.6590931351315246, 0.052704576976421634,
            81.76187518364655, 0.8409616273693943, 0.9722426389347306,
            22.227037550405868, 0.7519640852974186, 0.8159371492704826,
            1.3027898783488148, 0.009708737864077669, 13.221873207461806,
            "67:8D845DEEB1DC2E984A703B97B905E81A3067F91BCD7EF7799F88CAE1FB9B1221",
            "67:916F6211FB44BC7014CD2423F3089058BF61B509A7A8D63C7278B5FF1E3BC788",
            "1:9CF72BC82992C568342E925001F9509E427B92B22F6E1175B69BFED7FC4D73EC"),
        new("Kira Killer [Mocaotic's Insane].osu",
            9.01242711935666, 0.1561822125813449,
            7.189121242737026, 0.0911062906724512, 0.7890916411670877,
            33.33754542192292, 0.7406639004149378, 0.8236514522821576,
            25.97516620719823, 0.32537960954446854, 45.536869210608906,
            "87:BA15CAC7F96582B2588F6A89A64948DC2A0023B92B210B755E70D0C9D840E060",
            "131:20E756C5D3A56811EE00203526BC62A92560A2E8B63CB6DBC1EFCB5C16113673",
            "53:970163A1FD60B65E4BB048F57236EEC3519854071489F3A4D3A2002FD4C3C389"),
        new("(can you) understand me [uhh].osu",
            64.89540229885058, 0.6494252873563219,
            0, 0, 0,
            10.54224102473706, 0.4524590163934426, 0.6098360655737705,
            0, 0, 34.8396746993963,
            "0:E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855",
            "35:2020938DF9439DF656A5CF7B92DC2A09A1F8F570D4E10D1BC4D135C280F06A12",
            "0:E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855")
    ];

    [Fact]
    public void GlobalSkillOutputs_ExactlyMatchPreContractBaselines()
    {
        foreach (ExpectedGlobal expected in Baselines)
        {
            GameplayProfile profile = GameplayAnalyzer.Analyze(FixtureLoader.Load(expected.Fixture));

            Assert.Equal(expected.AimScore, profile.AimScore);
            Assert.Equal(expected.AimCoverage, profile.AimCoverage);
            Assert.Equal(expected.SpeedScore, profile.SpeedScore);
            Assert.Equal(expected.SpeedCoverage, profile.SpeedCoverage);
            Assert.Equal(expected.SpeedIntensity, profile.SpeedIntensityValue);
            Assert.Equal(expected.ReadScore, profile.ReadScore);
            Assert.Equal(expected.ReadCoverage, profile.ReadCoverage);
            Assert.Equal(expected.ReadRatio, profile.ReadRatio);
            Assert.Equal(expected.TechScore, profile.TechScore);
            Assert.Equal(expected.TechPresence, profile.TechPresence);
            Assert.Equal(expected.TechIntensity, profile.TechIntensity);
            Assert.Equal(expected.SpeedSections, Fingerprint(profile.SpeedSections));
            Assert.Equal(expected.ReadSections, Fingerprint(profile.ReadSections));
            Assert.Equal(expected.TechSections, Fingerprint(profile.TechSections));
        }
    }

    [Fact]
    public void ContributionSets_AreIndexedUniqueBoundedAndEligibilityConsistent()
    {
        foreach (ExpectedGlobal expected in Baselines)
        {
            Beatmap map = FixtureLoader.Load(expected.Fixture);
            GameplaySkillContributionProfile profile = GameplayAnalyzer.Analyze(map).SkillContributions;

            Assert.False(profile.Aim.CoverageSupported);
            Assert.Empty(profile.Aim.Contributions);
            Assert.True(profile.Speed.CoverageSupported);
            Assert.True(profile.Reading.CoverageSupported);
            Assert.True(profile.Tech.CoverageSupported);

            foreach (LocalSkillContributionSet set in new[] { profile.Speed, profile.Reading, profile.Tech })
            {
                Assert.Equal(set.Contributions.Count,
                    set.Contributions.Select(item => item.ObjectIndex).Distinct().Count());

                foreach (LocalSkillContribution contribution in set.Contributions)
                {
                    Assert.InRange(contribution.ObjectIndex, 0, map.HitObjects.Count - 1);
                    Assert.True(double.IsFinite(contribution.Pressure));
                    Assert.InRange(contribution.Pressure, 0, 1);
                    Assert.True(double.IsFinite(contribution.Weight));
                    Assert.True(contribution.Weight >= 0);
                    Assert.False(contribution.IsCoverageQualified && !contribution.IsEligible);
                }
            }
        }
    }

    [Fact]
    public void SpeedContributions_CaptureTheExistingFastMaskAndCadenceSignal()
    {
        Beatmap map = CreateCircleMap(0, 100, 200, 500);
        LocalSkillContributionSet speed = GameplayAnalyzer.Analyze(map).SkillContributions.Speed;

        Assert.Equal(new[] { 0, 1, 2 }, speed.Contributions
            .Where(item => item.IsCoverageQualified).Select(item => item.ObjectIndex));

        foreach (LocalSkillContribution contribution in speed.Contributions.Take(3))
        {
            Assert.Equal(10.0 / 12.0, contribution.Pressure, 12);
            Assert.Equal(1.0 / 6.0, contribution.Weight, 12);
        }

        Assert.Equal(0, speed.Contributions[3].Pressure);
        Assert.Equal(0, speed.Contributions[3].Weight);
        Assert.False(speed.Contributions[3].IsCoverageQualified);
    }

    [Fact]
    public void ReadingContributions_CaptureExistingLocalIntensityExactly()
    {
        Beatmap map = CreateCircleMap(0, 100, 200, 300);
        foreach (HitObject hitObject in map.HitObjects)
        {
            hitObject.X = 100;
            hitObject.Y = 100;
        }

        LocalSkillContributionSet reading = GameplayAnalyzer.Analyze(map).SkillContributions.Reading;

        Assert.Equal(4, reading.Contributions.Count);
        Assert.Equal(0.68, reading.Contributions[0].Pressure, 12);
        Assert.Equal((0.2 * 0.40 + 1.0 * 0.35) / 0.75, reading.Contributions[1].Pressure, 12);
        Assert.Equal(0, reading.Contributions[2].Pressure);
        Assert.Equal(0, reading.Contributions[3].Pressure);
        Assert.Equal(new[] { 0, 1 }, reading.Contributions
            .Where(item => item.IsCoverageQualified).Select(item => item.ObjectIndex));
    }

    [Fact]
    public void TechContributions_PreserveV1EvidenceAndCircleCoverageSemantics()
    {
        Beatmap map = FixtureLoader.Load("Kira Killer [Mocaotic's Insane].osu");
        GameplayProfile profile = GameplayAnalyzer.Analyze(map);
        LocalSkillContributionSet tech = profile.SkillContributions.Tech;

        Assert.Equal(map.HitObjects.Count, tech.Contributions.Count);
        Assert.All(tech.Contributions, contribution =>
            Assert.Equal((map.HitObjects[contribution.ObjectIndex].Type & 1) == 1, contribution.IsEligible));

        int[] expectedQualified = profile.TechSections
            .SelectMany(section => Enumerable.Range(section.StartObjectIndex,
                section.EndObjectIndex - section.StartObjectIndex + 1))
            .Where(index => (map.HitObjects[index].Type & 1) == 1)
            .Distinct().ToArray();

        Assert.Equal(expectedQualified, tech.Contributions
            .Where(item => item.IsCoverageQualified).Select(item => item.ObjectIndex));
        Assert.All(tech.Contributions.Where(item => item.IsCoverageQualified), item =>
            Assert.True(item.Pressure > 0));
    }

    [Fact]
    public void AvailableFixtures_ReportRepresentativeLocalSnapshots()
    {
        foreach (string fixture in new[] { "Tower Of Heaven [Extra].osu", "Kira Killer [Mocaotic's Insane].osu" })
        {
            Beatmap map = FixtureLoader.Load(fixture);
            GameplaySkillContributionProfile profile = GameplayAnalyzer.Analyze(map).SkillContributions;
            output.WriteLine(fixture);
            output.WriteLine("ObjectIndex|Time|Speed P/E/Q|Reading P/E/Q|Tech P/E/Q");

            foreach (int objectIndex in profile.Speed.Contributions
                         .Concat(profile.Reading.Contributions).Concat(profile.Tech.Contributions)
                         .Where(item => item.Pressure > 0 || item.IsCoverageQualified)
                         .Select(item => item.ObjectIndex).Distinct().Take(12))
            {
                output.WriteLine($"{objectIndex}|{map.HitObjects[objectIndex].Time}|" +
                    $"{Format(profile.Speed, objectIndex)}|{Format(profile.Reading, objectIndex)}|" +
                    Format(profile.Tech, objectIndex));
            }
        }
    }

    private static Beatmap CreateCircleMap(params int[] times) => new()
    {
        AR = 5,
        CS = 4,
        HitObjects = times.Select((time, index) => new HitObject
        {
            Type = 1, Time = time, X = 100 + index * 10, Y = 100
        }).ToList()
    };

    private static string Format(LocalSkillContributionSet set, int objectIndex)
    {
        LocalSkillContribution? item = set.Contributions.SingleOrDefault(x => x.ObjectIndex == objectIndex);
        return item is null ? "-" : $"{item.Pressure:F4}/{item.IsEligible}/{item.IsCoverageQualified}";
    }

    private static string Format(IReadOnlyList<GameplaySection> sections) =>
        string.Join(",", sections.Select(section =>
            $"{section.StartObjectIndex}-{section.EndObjectIndex}@{R(section.StartTime)}-{R(section.EndTime)}#{section.ObjectCount}"));

    private static string Fingerprint(IReadOnlyList<GameplaySection> sections) =>
        $"{sections.Count}:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Format(sections))))}";

    private static string R(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private sealed record ExpectedGlobal(
        string Fixture, double AimScore, double AimCoverage,
        double SpeedScore, double SpeedCoverage, double SpeedIntensity,
        double ReadScore, double ReadCoverage, double ReadRatio,
        double TechScore, double TechPresence, double TechIntensity,
        string SpeedSections, string ReadSections, string TechSections);
}
