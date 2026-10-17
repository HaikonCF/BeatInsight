using System.Text.Json.Nodes;
using BeatInsight.CommunityOverlays.Poc.Adapter;

namespace BeatInsight.CommunityOverlays.Poc.Bridge;

/// <summary>
/// Traduit un <see cref="CommunityOverlaySnapshot"/> vers les formats JSON
/// attendus par les overlays tosu :
/// <list type="bullet">
/// <item><c>/websocket/v2</c> et <c>/json/v2</c> : format tosu v2.</item>
/// <item><c>/ws</c> et <c>/json</c> : format legacy gosumemory (v1).</item>
/// <item><c>/websocket/v2/precise</c> : sous-ensemble temps/touches.</item>
/// </list>
/// Tous les chemins JSON attendus par les overlays sont toujours présents.
/// Contrat v2 (V3.14-COM-09) : un champ sans valeur (<see cref="FieldState.Unknown"/>,
/// <see cref="FieldState.NotAvailableOnScreen"/>) est émis avec la valeur neutre de
/// <see cref="Neutral"/> (0, chaîne vide, liste vide), comme tosu quand la donnée n'existe pas.
/// Le builder ne contient plus aucune constante de jeu (HP, BPM temps réel, note max,
/// client, joueur) : toutes viennent du snapshot, avec leur état.
/// </summary>
public static class TosuPayloadBuilder
{
    private static readonly (string Acronym, int Bit)[] ModBits =
    [
        ("NF", 1), ("EZ", 2), ("TD", 4), ("HD", 8), ("HR", 16), ("SD", 32),
        ("DT", 64), ("RX", 128), ("HT", 256), ("NC", 512 | 64), ("FL", 1024),
        ("AT", 2048), ("SO", 4096), ("AP", 8192), ("PF", 16384 | 32),
    ];

    /// <summary>Précisions toujours présentes dans <c>performance.accuracy</c> / <c>menu.pp</c>, comme tosu.</summary>
    public static readonly int[] TosuAccuracyKeys = [95, 96, 97, 98, 99, 100];

    /// <summary>Valeurs neutres émises pour un champ sans valeur.</summary>
    public static class Neutral
    {
        public const double Number = 0;
        public const string Text = "";
        public static IReadOnlyList<string> NoMods { get; } = [];
    }

    /// <summary>Numéro d'état osu! stable (OsuModes), tel qu'exposé par tosu.</summary>
    public static (int Number, string Name) MapState(CommunityOverlayRoute route) => route switch
    {
        CommunityOverlayRoute.Menu => (0, "menu"),
        CommunityOverlayRoute.Gameplay => (2, "play"),
        CommunityOverlayRoute.SongSelect => (5, "selectPlay"),
        CommunityOverlayRoute.Results => (7, "resultScreen"),
        _ => (10, "unknown"),
    };

    /// <summary>
    /// Note au format tosu (<c>calculateGrade</c>) : SS → X, et variante argentée
    /// XH/SH quand HD ou FL est actif. Une note vide reste vide.
    /// </summary>
    public static string TosuGrade(string grade, IReadOnlyList<string> mods)
    {
        if (grade.Length == 0) return "";
        bool silver = mods.Any(m => string.Equals(m, "HD", StringComparison.OrdinalIgnoreCase)
                                 || string.Equals(m, "FL", StringComparison.OrdinalIgnoreCase));
        return grade.ToUpperInvariant() switch
        {
            "SS" or "X" or "XH" => silver ? "XH" : "X",
            "S" or "SH" => silver ? "SH" : "S",
            var other => other,
        };
    }

    public static int ModsBitmask(IEnumerable<string> mods)
    {
        int mask = 0;
        foreach (string mod in mods)
            foreach (var (acronym, bit) in ModBits)
                if (string.Equals(acronym, mod, StringComparison.OrdinalIgnoreCase))
                    mask |= bit;
        return mask;
    }

    public static string ModsString(IReadOnlyList<string> mods) =>
        mods.Count == 0 ? "NM" : string.Concat(mods.Select(m => m.ToUpperInvariant()));

    /// <summary>
    /// Mods inconnus : nom vide, et non « NM » qui affirmerait l'absence de mods.
    /// </summary>
    public static string ModsString(FieldValue<IReadOnlyList<string>> mods) =>
        mods.HasValue ? ModsString(mods.Value) : Neutral.Text;

    public static JsonObject BuildV2(CommunityOverlaySnapshot s, CommunityOverlayRoute? provenRoute = null)
    {
        // Le canal precise v2 Lazer peut fournir une preuve native de gameplay alors que
        // le snapshot général est encore Unknown. Il ne remplace jamais une route connue
        // (SongSelect/Results) : ces routes restent l'autorité de sortie de gameplay.
        CommunityOverlayRoute route = s.Route == CommunityOverlayRoute.Unknown
            ? provenRoute ?? s.Route
            : s.Route;
        var (stateNumber, stateName) = MapState(route);
        var map = s.Beatmap;
        var mods = s.Mods.Or(Neutral.NoMods);
        string modsString = ModsString(s.Mods);
        string grade = Grade(s.Grade, s);
        string gradeMax = Grade(s.GradeMaxThisPlay, s);
        var files = map?.Files;

        var modsArray = new JsonArray();
        foreach (string mod in mods)
            modsArray.Add(new JsonObject { ["acronym"] = mod.ToUpperInvariant() });

        return new JsonObject
        {
            ["client"] = T(s.Client),
            ["server"] = "",
            ["state"] = new JsonObject { ["number"] = stateNumber, ["name"] = stateName },
            ["session"] = new JsonObject { ["playTime"] = 0, ["playCount"] = 0 },
            // Sous-ensemble minimal de tosu "settings" : seulement keybinds, lu sans
            // garde par KeyOverlay. Valeurs vides : BeatInsight ne connaît pas les touches.
            ["settings"] = new JsonObject
            {
                ["keybinds"] = new JsonObject
                {
                    ["osu"] = new JsonObject { ["k1"] = "", ["k2"] = "", ["smokeKey"] = "" },
                    ["fruits"] = new JsonObject { ["k1"] = "", ["k2"] = "", ["Dash"] = "" },
                    ["taiko"] = new JsonObject
                    {
                        ["innerLeft"] = "", ["innerRight"] = "", ["outerLeft"] = "", ["outerRight"] = "",
                    },
                    ["quickRetry"] = "",
                },
            },
            ["profile"] = new JsonObject
            {
                ["userStatus"] = new JsonObject { ["number"] = 0, ["name"] = "" },
                ["banchoStatus"] = new JsonObject { ["number"] = 0, ["name"] = "" },
                ["id"] = 0,
                ["name"] = T(s.PlayerName),
                ["mode"] = Mode(),
            },
            ["beatmap"] = new JsonObject
            {
                ["isKiai"] = false,
                ["isBreak"] = false,
                ["isConvert"] = false,
                ["time"] = new JsonObject
                {
                    ["live"] = Math.Round(D(s.CurrentTimeMs)),
                    ["firstObject"] = D(map, m => m.FirstObjectMs),
                    ["lastObject"] = D(map, m => m.LastObjectMs),
                    ["mp3Length"] = D(map, m => m.AudioLengthMs),
                },
                ["status"] = new JsonObject { ["number"] = 0, ["name"] = "unknown" },
                ["checksum"] = T(map, m => m.Checksum),
                ["id"] = I(map, m => m.BeatmapId),
                ["set"] = I(map, m => m.BeatmapSetId),
                ["mode"] = Mode(),
                ["artist"] = T(map, m => m.Artist),
                ["artistUnicode"] = T(map, m => m.ArtistUnicode),
                ["title"] = T(map, m => m.Title),
                ["titleUnicode"] = T(map, m => m.TitleUnicode),
                ["mapper"] = T(map, m => m.Mapper),
                ["version"] = T(map, m => m.Version),
                ["stats"] = new JsonObject
                {
                    ["stars"] = new JsonObject { ["live"] = D(map, m => m.Stars), ["total"] = D(map, m => m.Stars) },
                    ["ar"] = Stat(map?.Ar),
                    ["cs"] = Stat(map?.Cs),
                    ["od"] = Stat(map?.Od),
                    ["hp"] = Stat(map?.Hp),
                    ["bpm"] = new JsonObject
                    {
                        ["realtime"] = D(map, m => m.BpmRealtime),
                        ["common"] = D(map, m => m.BpmCommon),
                        ["min"] = D(map, m => m.BpmMin),
                        ["max"] = D(map, m => m.BpmMax),
                    },
                    ["objects"] = new JsonObject
                    {
                        ["circles"] = I(map, m => m.Circles),
                        ["sliders"] = I(map, m => m.Sliders),
                        ["spinners"] = I(map, m => m.Spinners),
                        ["holds"] = 0,
                        ["total"] = I(map, m => m.Circles) + I(map, m => m.Sliders) + I(map, m => m.Spinners),
                    },
                    ["maxCombo"] = I(map, m => m.MaxCombo),
                },
            },
            ["play"] = new JsonObject
            {
                ["playerName"] = T(s.PlayerName),
                ["mode"] = Mode(),
                ["score"] = s.Score.Or(0),
                ["accuracy"] = D(s.Accuracy),
                ["healthBar"] = new JsonObject { ["normal"] = D(s.HealthBar), ["smooth"] = D(s.HealthBar) },
                ["hits"] = HitsV2(s.Hits),
                ["hitErrorArray"] = new JsonArray(),
                ["combo"] = new JsonObject { ["current"] = I(s.Combo), ["max"] = I(s.MaxCombo) },
                ["mods"] = new JsonObject
                {
                    ["checksum"] = "",
                    ["number"] = ModsBitmask(mods),
                    ["name"] = modsString,
                    ["array"] = modsArray,
                    ["rate"] = Rate(s.Mods),
                },
                ["rank"] = new JsonObject { ["current"] = grade, ["maxThisPlay"] = gradeMax },
                ["pp"] = new JsonObject
                {
                    ["current"] = D(s.Pp.Current),
                    ["fc"] = D(s.Pp.IfFullCombo),
                    ["maxAchievedThisPlay"] = D(s.Pp.MaxAchieved),
                },
                ["unstableRate"] = 0,
            },
            ["leaderboard"] = new JsonArray(),
            ["performance"] = new JsonObject
            {
                ["accuracy"] = AccuracyTable(s.Pp),
                ["graph"] = new JsonObject { ["series"] = new JsonArray(), ["xaxis"] = new JsonArray() },
            },
            ["resultsScreen"] = new JsonObject
            {
                ["playerName"] = T(s.PlayerName),
                ["mode"] = Mode(),
                ["score"] = s.Score.Or(0),
                ["accuracy"] = D(s.Accuracy),
                ["name"] = T(s.PlayerName),
                ["hits"] = HitsV2(s.Hits),
                ["mods"] = new JsonObject { ["number"] = ModsBitmask(mods), ["name"] = modsString },
                ["maxCombo"] = I(s.MaxCombo),
                ["rank"] = grade,
                ["pp"] = new JsonObject { ["current"] = D(s.Pp.Current), ["fc"] = D(s.Pp.IfFullCombo) },
            },
            // Chemins relatifs à Songs, comme tosu. game/skin/songs restent vides :
            // aucun chemin absolu de la machine n'est exposé à un overlay tiers.
            ["folders"] = new JsonObject { ["game"] = "", ["skin"] = "", ["songs"] = "", ["beatmap"] = files?.Folder ?? "" },
            ["files"] = new JsonObject
            {
                ["beatmap"] = files?.OsuFile ?? "",
                ["background"] = files?.Background ?? "",
                ["audio"] = files?.Audio ?? "",
            },
            ["directPath"] = new JsonObject
            {
                ["beatmapFile"] = JoinLikeTosu(files?.Folder, files?.OsuFile),
                ["beatmapBackground"] = JoinLikeTosu(files?.Folder, files?.Background),
                ["beatmapAudio"] = JoinLikeTosu(files?.Folder, files?.Audio),
                ["beatmapFolder"] = files?.Folder ?? "",
                ["skinFolder"] = "",
            },
        };
    }

    /// <summary>Format legacy gosumemory (<c>/ws</c>), encore utilisé par beaucoup d'overlays.</summary>
    public static JsonObject BuildV1(CommunityOverlaySnapshot s)
    {
        var (stateNumber, _) = MapState(s.Route);
        var map = s.Beatmap;
        var mods = s.Mods.Or(Neutral.NoMods);
        string grade = Grade(s.Grade, s);
        string gradeMax = Grade(s.GradeMaxThisPlay, s);
        var files = map?.Files;

        var menuPp = AccuracyTable(s.Pp);
        menuPp["strains"] = new JsonArray();

        return new JsonObject
        {
            ["menu"] = new JsonObject
            {
                ["state"] = stateNumber,
                ["gameMode"] = 0,
                ["isChatEnabled"] = 0,
                ["bm"] = new JsonObject
                {
                    ["time"] = new JsonObject
                    {
                        ["firstObj"] = D(map, m => m.FirstObjectMs),
                        ["current"] = Math.Round(D(s.CurrentTimeMs)),
                        ["full"] = D(map, m => m.LastObjectMs),
                        ["mp3"] = D(map, m => m.AudioLengthMs),
                    },
                    ["id"] = I(map, m => m.BeatmapId),
                    ["set"] = I(map, m => m.BeatmapSetId),
                    ["md5"] = T(map, m => m.Checksum),
                    ["rankedStatus"] = 0,
                    ["metadata"] = new JsonObject
                    {
                        ["artist"] = T(map, m => m.Artist),
                        ["artistOriginal"] = T(map, m => m.ArtistUnicode),
                        ["title"] = T(map, m => m.Title),
                        ["titleOriginal"] = T(map, m => m.TitleUnicode),
                        ["mapper"] = T(map, m => m.Mapper),
                        ["difficulty"] = T(map, m => m.Version),
                    },
                    ["stats"] = new JsonObject
                    {
                        // gosumemory : AR/CS/OD/HP sont les valeurs avec mods.
                        ["AR"] = Converted(map?.Ar),
                        ["CS"] = Converted(map?.Cs),
                        ["OD"] = Converted(map?.Od),
                        ["HP"] = Converted(map?.Hp),
                        ["SR"] = D(map, m => m.Stars),
                        ["fullSR"] = D(map, m => m.Stars),
                        ["maxCombo"] = I(map, m => m.MaxCombo),
                        ["BPM"] = new JsonObject
                        {
                            ["common"] = D(map, m => m.BpmCommon),
                            ["min"] = D(map, m => m.BpmMin),
                            ["max"] = D(map, m => m.BpmMax),
                        },
                    },
                    ["path"] = new JsonObject
                    {
                        ["full"] = JoinLikeTosu(files?.Folder, files?.Background),
                        ["folder"] = files?.Folder ?? "",
                        ["file"] = files?.OsuFile ?? "",
                        ["bg"] = files?.Background ?? "",
                        ["audio"] = files?.Audio ?? "",
                    },
                },
                ["mods"] = new JsonObject { ["num"] = ModsBitmask(mods), ["str"] = ModsString(s.Mods) },
                ["pp"] = menuPp,
            },
            ["gameplay"] = new JsonObject
            {
                ["gameMode"] = 0,
                ["name"] = T(s.PlayerName),
                ["score"] = s.Score.Or(0),
                ["accuracy"] = D(s.Accuracy),
                ["combo"] = new JsonObject { ["current"] = I(s.Combo), ["max"] = I(s.MaxCombo) },
                // gosumemory : barre de vie sur 200.
                ["hp"] = new JsonObject { ["normal"] = D(s.HealthBar) * 2, ["smooth"] = D(s.HealthBar) * 2 },
                ["hits"] = new JsonObject
                {
                    ["300"] = I(s.Hits.Great),
                    ["geki"] = I(s.Hits.Geki),
                    ["100"] = I(s.Hits.Ok),
                    ["katu"] = I(s.Hits.Katu),
                    ["50"] = I(s.Hits.Meh),
                    ["0"] = I(s.Hits.Miss),
                    ["sliderBreaks"] = I(s.Hits.SliderBreaks),
                    ["grade"] = new JsonObject { ["current"] = grade, ["maxThisPlay"] = gradeMax },
                    ["unstableRate"] = 0,
                    ["hitErrorArray"] = new JsonArray(),
                },
                ["pp"] = new JsonObject
                {
                    ["current"] = D(s.Pp.Current),
                    ["fc"] = D(s.Pp.IfFullCombo),
                    ["maxThisPlay"] = D(s.Pp.MaxAchieved),
                },
                ["leaderboard"] = new JsonObject { ["hasLeaderboard"] = false, ["slots"] = new JsonArray() },
            },
        };
    }

    /// <summary>
    /// <c>/websocket/v2/precise</c>, même forme que tosu (<c>buildResultV2Precise</c>) :
    /// keys, hitErrors, tourney. Pas de champ temps dans la réponse tosu.
    /// </summary>
    public static JsonObject BuildPrecise(CommunityOverlaySnapshot s)
    {
        // Touches inconnues : relâchées, compteur 0 (comme tosu avant toute frappe).
        var keys = s.Keys.Or(CommunityOverlayKeys.Idle);
        return new()
        {
            ["keys"] = new JsonObject
            {
                ["k1"] = Key(keys.K1),
                ["k2"] = Key(keys.K2),
                ["m1"] = Key(keys.M1),
                ["m2"] = Key(keys.M2),
            },
            ["hitErrors"] = new JsonArray(),
            ["tourney"] = new JsonArray(),
        };
    }

    private static JsonObject Key(CommunityOverlayKey key) =>
        new() { ["isPressed"] = key.IsPressed, ["count"] = key.Count };

    /// <summary>tosu utilise <c>path.join(folder, file)</c> : séparateur <c>\</c> sous Windows.</summary>
    public static string JoinLikeTosu(string? folder, string? file)
    {
        if (string.IsNullOrEmpty(folder)) return file ?? "";
        if (string.IsNullOrEmpty(file)) return folder;
        return folder.TrimEnd('\\', '/') + "\\" + file;
    }

    private static JsonObject Mode() => new() { ["number"] = 0, ["name"] = "osu" };

    private static double D(FieldValue<double> f) => f.Or(Neutral.Number);

    private static int I(FieldValue<int> f) => f.Or(0);

    private static string T(FieldValue<string> f) => f.Or(Neutral.Text);

    private static double D(CommunityOverlayBeatmap? map, Func<CommunityOverlayBeatmap, FieldValue<double>> field) =>
        map is null ? Neutral.Number : D(field(map));

    private static int I(CommunityOverlayBeatmap? map, Func<CommunityOverlayBeatmap, FieldValue<int>> field) =>
        map is null ? 0 : I(field(map));

    private static string T(CommunityOverlayBeatmap? map, Func<CommunityOverlayBeatmap, FieldValue<string>> field) =>
        map is null ? Neutral.Text : T(field(map));

    private static string Grade(FieldValue<string> grade, CommunityOverlaySnapshot s) =>
        TosuGrade(T(grade), s.Mods.Or(Neutral.NoMods));

    /// <summary>Vitesse de lecture ; 1 si les mods sont inconnus.</summary>
    private static double Rate(FieldValue<IReadOnlyList<string>> mods)
    {
        if (!mods.HasValue) return 1.0;
        var list = mods.Value;
        return list.Any(m => m is "DT" or "NC") ? 1.5 : list.Contains("HT") ? 0.75 : 1.0;
    }

    private static JsonObject Stat(CommunityOverlayDifficulty? value) => new()
    {
        ["original"] = value is null ? Neutral.Number : D(value.Original),
        ["converted"] = Converted(value),
    };

    private static double Converted(CommunityOverlayDifficulty? value) =>
        value is null ? Neutral.Number : D(value.Converted);

    private static JsonObject HitsV2(CommunityOverlayHits h) => new()
    {
        ["0"] = I(h.Miss),
        ["50"] = I(h.Meh),
        ["100"] = I(h.Ok),
        ["300"] = I(h.Great),
        ["geki"] = I(h.Geki),
        ["katu"] = I(h.Katu),
        ["sliderBreaks"] = I(h.SliderBreaks),
        ["sliderEndHits"] = 0,
        ["sliderTickHits"] = 0,
    };

    private static JsonObject AccuracyTable(CommunityOverlayPerformance pp)
    {
        var table = new JsonObject();
        foreach (int accuracy in TosuAccuracyKeys.Union(pp.ByAccuracy.Keys).Order())
        {
            double value = pp.ByAccuracy.TryGetValue(accuracy, out var field) ? D(field) : Neutral.Number;
            table[accuracy.ToString(System.Globalization.CultureInfo.InvariantCulture)] = value;
        }
        return table;
    }
}
