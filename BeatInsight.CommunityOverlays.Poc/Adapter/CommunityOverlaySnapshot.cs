namespace BeatInsight.CommunityOverlays.Poc.Adapter;

/// <summary>
/// Écran courant, vu par un overlay communautaire.
/// Neutre vis-à-vis de BeatInsight et de tosu : la traduction vers les
/// numéros d'état osu! stable est faite dans <c>TosuPayloadBuilder</c>.
/// </summary>
public enum CommunityOverlayRoute
{
    Unknown,
    Menu,
    SongSelect,
    Gameplay,
    Results,
}

/// <summary>
/// Origine du snapshot. Une source est soit mock, soit live, jamais un mélange :
/// aucune valeur mock ne peut compléter un snapshot live (V3.14-COM-09, C4).
/// </summary>
public enum CommunityOverlaySourceKind
{
    Mock,
    Live,
}

/// <summary>
/// Identités qui délimitent la validité des données : un changement de l'une
/// d'elles invalide tout ce qui en dépend.
/// </summary>
/// <param name="ProcessIdentity">Génération du process osu! (null si aucun process).</param>
/// <param name="BeatmapIdentity">Beatmap sélectionnée (chemin normalisé), null si inconnue.</param>
/// <param name="PlayId">Partie en cours ou dont les résultats sont affichés ; null hors partie.</param>
public sealed record CommunityOverlayIdentity(string? ProcessIdentity, string? BeatmapIdentity, long? PlayId)
{
    public static CommunityOverlayIdentity None { get; } = new(null, null, null);
}

/// <summary>Valeur de difficulté : de base (sans mods) et convertie (avec les mods de la partie).</summary>
public sealed record CommunityOverlayDifficulty(FieldValue<double> Original, FieldValue<double> Converted)
{
    public static CommunityOverlayDifficulty Unknown { get; } = new(FieldValue<double>.Unknown, FieldValue<double>.Unknown);

    public static CommunityOverlayDifficulty Mock(double value) => new(MockField.Of(value), MockField.Of(value));
}

public sealed record CommunityOverlayBeatmap(
    FieldValue<int> BeatmapId,
    FieldValue<int> BeatmapSetId,
    FieldValue<string> Checksum,
    FieldValue<string> Artist,
    FieldValue<string> ArtistUnicode,
    FieldValue<string> Title,
    FieldValue<string> TitleUnicode,
    FieldValue<string> Mapper,
    FieldValue<string> Version,
    FieldValue<double> Stars,
    CommunityOverlayDifficulty Ar,
    CommunityOverlayDifficulty Cs,
    CommunityOverlayDifficulty Od,
    CommunityOverlayDifficulty Hp,
    FieldValue<double> BpmCommon,
    FieldValue<double> BpmMin,
    FieldValue<double> BpmMax,
    FieldValue<double> BpmRealtime,
    FieldValue<int> MaxCombo,
    FieldValue<int> Circles,
    FieldValue<int> Sliders,
    FieldValue<int> Spinners,
    FieldValue<double> FirstObjectMs,
    FieldValue<double> LastObjectMs,
    FieldValue<double> AudioLengthMs)
{
    /// <summary>Fichiers de la beatmap, relatifs au dossier Songs ; null si inconnus.</summary>
    public CommunityOverlayBeatmapFiles? Files { get; init; }

    /// <summary>Beatmap mock : mêmes paramètres que le contrat v1, toutes valeurs marquées Mock.</summary>
    public static CommunityOverlayBeatmap Mock(
        int beatmapId, int beatmapSetId, string checksum, string artist, string artistUnicode,
        string title, string titleUnicode, string mapper, string version, double stars,
        double ar, double cs, double od, double hp, double bpmCommon, double bpmMin, double bpmMax,
        int maxCombo, int circles, int sliders, int spinners,
        double firstObjectMs, double lastObjectMs, double audioLengthMs) => new(
        MockField.Of(beatmapId), MockField.Of(beatmapSetId), MockField.Of(checksum),
        MockField.Of(artist), MockField.Of(artistUnicode), MockField.Of(title), MockField.Of(titleUnicode),
        MockField.Of(mapper), MockField.Of(version), MockField.Of(stars),
        CommunityOverlayDifficulty.Mock(ar), CommunityOverlayDifficulty.Mock(cs),
        CommunityOverlayDifficulty.Mock(od), CommunityOverlayDifficulty.Mock(hp),
        MockField.Of(bpmCommon), MockField.Of(bpmMin), MockField.Of(bpmMax), MockField.Of(bpmCommon),
        MockField.Of(maxCombo), MockField.Of(circles), MockField.Of(sliders), MockField.Of(spinners),
        MockField.Of(firstObjectMs), MockField.Of(lastObjectMs), MockField.Of(audioLengthMs));
}

/// <summary>
/// Emplacement des fichiers de la beatmap courante, comme tosu <c>menu.folder</c>,
/// <c>menu.filename</c>, <c>menu.backgroundFilename</c>, <c>menu.audioFilename</c>.
/// <paramref name="Folder"/> est relatif à la racine Songs ; les noms sont relatifs à ce dossier.
/// </summary>
public sealed record CommunityOverlayBeatmapFiles(
    string Folder,
    string? OsuFile,
    string? Background,
    string? Audio);

/// <summary>Compteurs de jugements osu!standard, chacun avec son propre état.</summary>
public sealed record CommunityOverlayHits(
    FieldValue<int> Great,
    FieldValue<int> Ok,
    FieldValue<int> Meh,
    FieldValue<int> Miss,
    FieldValue<int> Geki,
    FieldValue<int> Katu,
    FieldValue<int> SliderBreaks)
{
    public static CommunityOverlayHits Unknown { get; } = Uniform(FieldValue<int>.Unknown);

    public static CommunityOverlayHits NotAvailableOnScreen { get; } = Uniform(FieldValue<int>.NotAvailableOnScreen);

    public static CommunityOverlayHits Mock(int great, int ok, int meh, int miss, int geki, int katu, int sliderBreaks) => new(
        MockField.Of(great), MockField.Of(ok), MockField.Of(meh), MockField.Of(miss),
        MockField.Of(geki), MockField.Of(katu), MockField.Of(sliderBreaks));

    private static CommunityOverlayHits Uniform(FieldValue<int> v) => new(v, v, v, v, v, v, v);
}

/// <summary>
/// État d'une touche osu! (format tosu <c>/websocket/v2/precise</c>).
/// Aucune source BeatInsight ne fournit ces données : seul le mock les simule.
/// </summary>
public sealed record CommunityOverlayKey(bool IsPressed, int Count)
{
    public static CommunityOverlayKey Idle { get; } = new(false, 0);
}

public sealed record CommunityOverlayKeys(
    CommunityOverlayKey K1,
    CommunityOverlayKey K2,
    CommunityOverlayKey M1,
    CommunityOverlayKey M2)
{
    public static CommunityOverlayKeys Idle { get; } = new(
        CommunityOverlayKey.Idle, CommunityOverlayKey.Idle, CommunityOverlayKey.Idle, CommunityOverlayKey.Idle);
}

/// <summary>
/// PP de la partie. <paramref name="ByAccuracy"/> ne contient que les précisions réellement
/// fournies par la source ; le builder émet un neutre pour les autres.
/// </summary>
public sealed record CommunityOverlayPerformance(
    FieldValue<double> Current,
    FieldValue<double> IfFullCombo,
    FieldValue<double> MaxAchieved,
    IReadOnlyDictionary<int, FieldValue<double>> ByAccuracy)
{
    public static CommunityOverlayPerformance Unknown { get; } = new(
        FieldValue<double>.Unknown, FieldValue<double>.Unknown, FieldValue<double>.Unknown,
        new Dictionary<int, FieldValue<double>>());

    public static CommunityOverlayPerformance NotAvailableOnScreen { get; } = new(
        FieldValue<double>.NotAvailableOnScreen, FieldValue<double>.NotAvailableOnScreen,
        FieldValue<double>.NotAvailableOnScreen, new Dictionary<int, FieldValue<double>>());
}

/// <summary>
/// Instantané unique consommé par le bridge (contrat v2, V3.14-COM-09). C'est le seul point
/// de contact entre une source (mock ou live) et le format tosu. Chaque champ porte son état :
/// un champ inconnu n'est jamais confondu avec un zéro réellement observé.
/// </summary>
/// <param name="Accuracy">Précision en pourcentage (0–100), comme tosu.</param>
/// <param name="Grade">Note neutre : SS, S, A, B, C ou D. Traduite au format tosu (X/XH/SH…) par le bridge.</param>
/// <param name="HealthBar">Barre de vie 0–100 (v1 tosu : ×2).</param>
public sealed record CommunityOverlaySnapshot(
    CommunityOverlaySourceKind Source,
    CommunityOverlayRoute Route,
    DateTimeOffset SnapshotAtUtc,
    CommunityOverlayIdentity Identity,
    FieldValue<string> Client,
    FieldValue<string> PlayerName,
    CommunityOverlayBeatmap? Beatmap,
    FieldValue<double> CurrentTimeMs,
    FieldValue<long> Score,
    FieldValue<int> Combo,
    FieldValue<int> MaxCombo,
    FieldValue<double> Accuracy,
    FieldValue<string> Grade,
    FieldValue<string> GradeMaxThisPlay,
    CommunityOverlayHits Hits,
    FieldValue<IReadOnlyList<string>> Mods,
    FieldValue<double> HealthBar,
    CommunityOverlayPerformance Pp)
{
    /// <summary>Touches k1/k2/m1/m2 ; inconnues pour toute source live.</summary>
    public FieldValue<CommunityOverlayKeys> Keys { get; init; } = FieldValue<CommunityOverlayKeys>.Unknown;
}
