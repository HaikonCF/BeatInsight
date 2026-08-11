using BeatInsight.Models;
using BeatInsight.Parser;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Linq;
using System.Text;
using System.Reflection;

// Cette classe représente la fenêtre principale de BeatInsight et contient la logique qui surveille la map actuellement sélectionnée.

namespace BeatInsight
// On garde un HttpClient réutilisable pour demander régulièrement les données JSON d'osu! via l'API locale.
{
    // Le DispatcherTimer permet de vérifier périodiquement si la map sélectionnée a changé.
    public partial class MainWindow : Window
    {
        // Chemin complet de la dernière map traitée. Il sert à éviter de recharger la même map en boucle.
        private readonly HttpClient client = new HttpClient();
        // Indique qu'une mise à jour est déjà en cours pour éviter que deux appels UpdateMap() se chevauchent.
        private readonly DispatcherTimer mapTimer;

        // Le constructeur prépare la fenêtre et démarre la surveillance automatique de la map.
        private string? currentMapPath;
        private bool isUpdating;
        private string? currentBeatmapUrl;

        // On initialise les composants graphiques générés par WPF avant de manipuler l'interface.

        public MainWindow()
        // On crée le timer qui servira à vérifier régulièrement l'état actuel d'osu!.
        {
            // Une vérification toutes les 500 ms permet de détecter rapidement un changement de map sans interroger osu! en permanence.
            InitializeComponent();

            // Quand le timer déclenche son événement, on appelle la méthode qui vérifie la map actuelle.

            mapTimer = new DispatcherTimer();
            // On démarre immédiatement le timer afin que la surveillance commence dès l'ouverture de BeatInsight.
            mapTimer.Interval = TimeSpan.FromMilliseconds(500);
            mapTimer.Tick += MapTimer_Tick;

            // On lance une première mise à jour sans bloquer le constructeur sur l'opération asynchrone.

            mapTimer.Start();

            // Cette méthode est appelée automatiquement à chaque déclenchement du DispatcherTimer.
            _ = UpdateMap();


        }

        public string AppVersion => $"BeatInsight v{Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)}";

        // Si une mise à jour précédente n'est pas terminée, on quitte immédiatement pour éviter deux traitements simultanés.

        private async void MapTimer_Tick(object? sender, EventArgs e)
        {
            // On verrouille temporairement les mises à jour pendant le traitement courant.
            if (isUpdating)
                return;

            // On récupère les informations de la map depuis l'API locale d'osu! avant de les analyser.
            isUpdating = true;

            // Même en cas d'erreur ou d'arrêt pendant UpdateMap(), le verrou sera toujours libéré.
            try
            {
                await UpdateMap();
                // On demande à osu! les données JSON qui décrivent notamment la beatmap actuellement sélectionnée.
            }
            finally
            {
                // On transforme le texte JSON reçu en document afin de pouvoir accéder à ses propriétés.
                isUpdating = false;
            }
            // On récupère le dossier de la beatmap sélectionnée dans les informations envoyées par osu!.
        }

        private void CopyAnalysis_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not Beatmap beatmap)
                return;

            GameplayProfile profile = beatmap.GameplayProfile;

            StringBuilder text = new();

            // ============================================================
            // MAP
            // ============================================================

            text.AppendLine($"MAP = {beatmap.Title}");

            if (!string.IsNullOrWhiteSpace(beatmap.Version))
                text.AppendLine($"DIFFICULTY = {beatmap.Version}");

            text.AppendLine();

            // ============================================================
            // IDENTITY
            // ============================================================

            text.AppendLine("GAMEPLAY IDENTITY");
            text.AppendLine(profile.Identity.FullName);

            text.AppendLine();

            text.AppendLine("CONFIDENCE");
            text.AppendLine($"{profile.Identity.Confidence:F0}%");

            text.AppendLine();

            // ============================================================
            // TRAITS
            // ============================================================

            text.AppendLine("TRAITS");

            var traits = profile.Identity.Traits
                .Distinct()
                .ToList();

            if (traits.Count == 0)
            {
                text.AppendLine("None");
            }
            else
            {
                foreach (string trait in traits)
                    text.AppendLine($"• {trait}");
            }

            text.AppendLine();

            // ============================================================
            // PATTERNS
            // ============================================================

            text.AppendLine("PATTERNS");

            text.AppendLine(
                $"Stream: {profile.StreamRatio * 100:F2}%");

            text.AppendLine(
                $"Jump: {profile.JumpRatio * 100:F2}%");

            text.AppendLine(
                $"Burst: {profile.BurstRatio * 100:F2}%");

            text.AppendLine();

            // ============================================================
            // SCORES
            // ============================================================

            text.AppendLine("SCORES");

            text.AppendLine(
                $"Tech: {profile.TechScore:F0}/100");

            text.AppendLine(
                $"Read: {profile.ReadScore:F0}/100");

            text.AppendLine(
                $"Speed: {profile.SpeedScore:F0}/100");

            text.AppendLine(
                $"Aim: {profile.AimScore:F0}/100");

            text.AppendLine();

            // ============================================================
            // SIGNALS
            // ============================================================

            text.AppendLine("SIGNALS");

            text.AppendLine(
                $"Tech: Transition {profile.TechTransitionSignal:F0}% / " +
                $"Structure {profile.TechStructureSignal:F0}% / " +
                $"Spatial {profile.TechSpatialSignal:F0}% / " +
                $"Temporal {profile.TechTemporalSignal:F0}%");

            text.AppendLine(
                $"Read: Density {profile.ReadDensitySignal:F0}% / " +
                $"Clutter {profile.ReadClutterSignal:F0}% / " +
                $"Persistence {profile.ReadPersistenceSignal:F0}% / " +
                $"CS {profile.ReadCSSignal:F0}%");

            text.AppendLine(
                $"Speed: Fast {profile.SpeedFastObjectRatio * 100:F0}% / " +
                $"Density {profile.SpeedDensitySignal:F0}% / " +
                $"AR {profile.SpeedARSignal:F0}%");

            text.AppendLine(
                $"Aim: Distance {profile.AimDistanceSignal:F0}% / " +
                $"Speed {profile.AimSpeedSignal:F0}% / " +
                $"Angle {profile.AimAngleSignal:F0}% / " +
                $"Temporal {profile.AimTemporalSignal:F0}%");

            // ============================================================
            // COPY
            // ============================================================

            Clipboard.SetText(text.ToString());
        }

        private void OpenBeatmap_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(currentBeatmapUrl))
                return;

            Process.Start(new ProcessStartInfo
            {
                FileName = currentBeatmapUrl,
                UseShellExecute = true
            });
        }

        private void ReportClassification_Click(object sender, RoutedEventArgs e)
{
    if (DataContext is not Beatmap beatmap)
        return;

    GameplayProfile profile = beatmap.GameplayProfile;

    string traits = profile.Identity.Traits.Count == 0
        ? "None"
        : string.Join(
            "\n",
            profile.Identity.Traits
                .Distinct()
                .Select(t => $"- {t}")
        );

    string body = $"""
## Beatmap

**Title:** {beatmap.Title}

**Difficulty:** {beatmap.Version}

## BeatInsight

**Version:** {AppVersion}

**Identity:** {profile.Identity.FullName}

**Confidence:** {profile.Identity.Confidence:F0}%

### Traits

{traits}

### Gameplay

- Stream: {profile.StreamRatio * 100:F2}%
- Jump: {profile.JumpRatio * 100:F2}%
- Burst: {profile.BurstRatio * 100:F2}%

### Scores

- Tech: {profile.TechScore:F0}/100
- Read: {profile.ReadScore:F0}/100
- Speed: {profile.SpeedScore:F0}/100
- Aim: {profile.AimScore:F0}/100

## Expected classification

<!-- What should BeatInsight classify this map as? -->


## Why?

<!-- Explain why you think the classification is incorrect. -->

## Additional information

<!-- Any other useful information? -->
""";

    string title =
        $"Classification incorrecte - {beatmap.Title} [{beatmap.Version}]";

    string url =
        "https://github.com/HaikonCF/BeatInsight/issues/new" +
        $"?title={Uri.EscapeDataString(title)}" +
        $"&body={Uri.EscapeDataString(body)}";

    Process.Start(new ProcessStartInfo
    {
        FileName = url,
        UseShellExecute = true
    });
}

        private async Task UpdateMap()
        {
            string json = await client.GetStringAsync(
                "http://127.0.0.1:24050/json"
            );

            // On récupère le dossier racine dans lequel osu! stocke ses beatmaps.
            JsonDocument document = JsonDocument.Parse(json);

            string folder =
                document.RootElement
                .GetProperty("menu")
                .GetProperty("bm")
                .GetProperty("path")
                .GetProperty("folder")
                // On récupère le nom du fichier .osu actuellement sélectionné.
                .GetString()!;

            string songs =
                document.RootElement
                .GetProperty("settings")
                .GetProperty("folders")
                .GetProperty("songs")
                .GetString()!;

            // On récupère le chemin du background associé à la difficulté actuelle.

            string file =
                document.RootElement
                .GetProperty("menu")
                .GetProperty("bm")
                .GetProperty("path")
                .GetProperty("file")
                .GetString()!;

            // On assemble le dossier Songs, le dossier de la beatmap et le fichier .osu pour obtenir son chemin complet.

            string? bg = null;

            if (document.RootElement
                .GetProperty("menu")
                .GetProperty("bm")
                .GetProperty("path")
                .TryGetProperty("bg", out JsonElement bgElement))
            {
                bg = bgElement.GetString();
            }

            int beatmapId =
                document.RootElement
                .GetProperty("menu")
                .GetProperty("bm")
                .GetProperty("id")
                .GetInt32();

            currentBeatmapUrl = $"https://osu.ppy.sh/b/{beatmapId}";

            string chemin = System.IO.Path.Combine(
                songs,
                // Une nouvelle map a été détectée : on mémorise son chemin pour les prochaines vérifications.
                folder,
                file
            // Ce message permet de repérer clairement dans le debug le début du traitement d'une nouvelle map.
            );

            // Même map → on ne fait rien
            // On construit le chemin complet vers l'image de background de la beatmap.
            if (chemin == currentMapPath)
                return;

            // Nouvelle map
            currentMapPath = chemin;

            // On charge le background dans le contrôle graphique de l'interface.
            Debug.WriteLine($"----- New Map -----");


            Beatmap beatmap = BeatmapParser.Load(chemin);

            Debug.WriteLine($"MAP = {beatmap.Title}");


            // ============================================================
            // BACKGROUND
            // ============================================================

            if (!string.IsNullOrWhiteSpace(bg))
            {
                string backgroundPath = System.IO.Path.Combine(
                    songs,
                    folder,
                    bg
                );

                if (System.IO.File.Exists(backgroundPath))
                {
                    try
                    {
                        BackgroundImage.Source =
                            new BitmapImage(new Uri(backgroundPath));
                    }
                    catch
                    {
                        BackgroundImage.Source = null;
                    }
                }
                else
                {
                    BackgroundImage.Source = null;
                }
            }
            else
            {
                BackgroundImage.Source = null;
            }
           
    DataContext = beatmap;
        }
    }
}