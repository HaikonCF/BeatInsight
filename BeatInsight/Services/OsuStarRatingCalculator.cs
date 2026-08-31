using BeatInsight.Diagnostics;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Formats;
using osu.Game.IO;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Difficulty;
using System;
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

        DebugLogger.Detailed(
            $"OSU STAR RATING | File={osuFilePath}");

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

        DebugLogger.Detailed(
            $"OSU STAR RATING | HitObjects={beatmap.HitObjects.Count}");

        if (beatmap.HitObjects.Count == 0)
        {
            DebugLogger.Log(
                "OSU STAR RATING | Aucun HitObject | StarRating=0");

            return 0;
        }

        // ============================================================
        // WORKING BEATMAP
        // ============================================================

        var workingBeatmap =
            new InMemoryWorkingBeatmap(beatmap);

        // ============================================================
        // RULESET OFFICIEL OSU!
        // ============================================================

        var ruleset =
            new OsuRuleset();

        // ============================================================
        // CALCULATEUR OFFICIEL
        // ============================================================

        var calculator =
            ruleset.CreateDifficultyCalculator(
                workingBeatmap);

        var attributes =
            calculator.Calculate(
                Array.Empty<Mod>());

        var osuAttributes =
            (OsuDifficultyAttributes)attributes;

        DebugLogger.Detailed(
            $"OSU STAR RATING | " +
            $"Ruleset={ruleset.RulesetInfo.Name} | " +
            $"Calculator={calculator.GetType().Name} | " +
            $"Attributes={attributes.GetType().Name} | " +
            $"StarRating={osuAttributes.StarRating:F3}");

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