using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Formats;
using osu.Game.IO;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Difficulty;
using System;
using System.Diagnostics;
using System.IO;


namespace BeatInsight.Services;

public static class OsuStarRatingCalculator
{
    public static double Calculate(string osuFilePath)
    {
        if (!File.Exists(osuFilePath))
            throw new FileNotFoundException(
                "Beatmap osu! introuvable.",
                osuFilePath);

        Debug.WriteLine("===== OSU STAR RATING DEBUG =====");
        Debug.WriteLine($"FILE = {osuFilePath}");

        // Décodeur utilisé par le système de difficulté osu!
        LegacyDifficultyCalculatorBeatmapDecoder.Register();

        Beatmap beatmap;

        using (var stream = File.OpenRead(osuFilePath))
        using (var reader = new LineBufferedReader(stream))
        {
            var decoder =
                Decoder.GetDecoder<Beatmap>(reader);

            beatmap = decoder.Decode(reader);
        }

        Debug.WriteLine(
            $"OSU DEBUG | HitObjects={beatmap.HitObjects.Count}");

        

        if (beatmap.HitObjects.Count == 0)
        {
            Debug.WriteLine(
                "OSU DEBUG | Aucun HitObject => StarRating=0");

            return 0;
        }

        // ============================================================
        // WORKING BEATMAP
        // ============================================================

        var workingBeatmap =
            new InMemoryWorkingBeatmap(beatmap);

        Debug.WriteLine(
            $"OSU DEBUG | WorkingBeatmap créée");

        // ============================================================
        // RULESET OFFICIEL OSU!
        // ============================================================

        var ruleset =
            new OsuRuleset();

        Debug.WriteLine(
            $"OSU DEBUG | Ruleset={ruleset.RulesetInfo.Name}");

        // ============================================================
        // CALCULATEUR OFFICIEL
        // ============================================================

        var calculator =
            ruleset.CreateDifficultyCalculator(
                workingBeatmap);

        Debug.WriteLine(
            $"OSU DEBUG | Calculator={calculator.GetType().Name}");

        var attributes =
            calculator.Calculate(
                Array.Empty<Mod>());

        Debug.WriteLine(
            $"OSU DEBUG | Attributes={attributes.GetType().Name}");

        var osuAttributes =
            (OsuDifficultyAttributes)attributes;

        Debug.WriteLine(
            $"OSU DEBUG | StarRating={osuAttributes.StarRating}");

        Debug.WriteLine(
            "=================================");

        return osuAttributes.StarRating;
    }


    private sealed class InMemoryWorkingBeatmap : WorkingBeatmap
    {
        private readonly IBeatmap beatmap;

        public InMemoryWorkingBeatmap(IBeatmap beatmap)
            : base(beatmap.BeatmapInfo, null!)
        {
            this.beatmap = beatmap;
        }

        protected override IBeatmap GetBeatmap()
        {
            return beatmap;
        }



        public override osu.Framework.Graphics.Textures.Texture GetBackground()
        {
            return null!;
        }

        protected override osu.Framework.Audio.Track.Track GetBeatmapTrack()
        {
            return null!;
        }

        protected override osu.Game.Skinning.ISkin GetSkin()
        {
            return null!;
        }

        public override Stream GetStream(string storagePath)
        {
            return Stream.Null;
        }
    }
}