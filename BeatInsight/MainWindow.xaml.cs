using AutoMapper;
using BeatInsight.Models;
using BeatInsight.Parser;
using BeatInsight.Services;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using static BeatInsight.OsuApiService;

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
        private readonly OsuApiService osuApi = new OsuApiService();

        // Le constructeur prépare la fenêtre et démarre la surveillance automatique de la map.
        private string? currentMapPath;
        private bool isUpdating;
        private bool tosuConnected;
        private string? currentBeatmapUrl;
        private readonly OsuApiService osuApiService = new();
        private async Task TestOsuApi(int beatmapId)
        {
            try
            {
                string json = await osuApi.GetBeatmap(beatmapId);

                Debug.WriteLine("============================================================");
                Debug.WriteLine("OSU BEATMAP API OK");
                Debug.WriteLine($"BEATMAP ID = {beatmapId}");
                Debug.WriteLine(json);
                Debug.WriteLine("============================================================");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("============================================================");
                Debug.WriteLine("OSU BEATMAP API ERROR");
                Debug.WriteLine($"BEATMAP ID = {beatmapId}");
                Debug.WriteLine(ex);
                Debug.WriteLine("============================================================");
            }
        }


        public MainWindow()
        // On crée le timer qui servira à vérifier régulièrement l'état actuel d'osu!.
        {

            InitializeComponent();

            


            mapTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };

            mapTimer.Tick += MapTimer_Tick;
            mapTimer.Start(); }

        public string AppVersion => $"BeatInsight v{Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)}";

        // Si une mise à jour précédente n'est pas terminée, on quitte immédiatement pour éviter deux traitements simultanés.

        private async void MapTimer_Tick(object? sender, EventArgs e)
        {
            if (isUpdating)
                return;

            isUpdating = true;

            try
            {
                await UpdateMap();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("============================================================");
                Debug.WriteLine("UPDATE MAP ERROR");
                Debug.WriteLine(ex);
                Debug.WriteLine("============================================================");
            }
            finally
            {
                isUpdating = false;
            }
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
                $"Read: Density {profile.ReadDensitySignal:P0} / " +
                $"Clutter {profile.ReadClutterSignal:P0} / " +
                "Persistence neutralized / " +
                $"CS {profile.ReadCSSignal:P0} (neutralized)");

            text.AppendLine(
                $"Read: Intensity {profile.ReadIntensity} / " +
                $"Presence {profile.ReadCoverage:P0} / " +
                $"Sections {profile.ReadSections.Count}");

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
        private async Task<List<CommunityTag>> GetCommunityTags(int beatmapId)
        {
            try
            {
                List<OsuApiService.OsuTagVote> tags =
                    await osuApi.GetBeatmapCommunityTags(beatmapId);

                return tags
                    .Select(tag => new CommunityTag
                    {
                        Name = tag.Name,
                        Votes = tag.Votes
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"COMMUNITY TAGS ERROR | {ex.Message}");

                return new List<CommunityTag>();
            }
        }

        private async Task TestTagsApi()
        {
            try
            {
                string token = await GetOsuAccessToken();

                using HttpRequestMessage request = new(
                    HttpMethod.Get,
                    "https://osu.ppy.sh/api/v2/tags"
                );

                request.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue(
                        "Bearer",
                        token
                    );

                request.Headers.Add("Accept", "application/json");

                using HttpResponseMessage response =
                    await client.SendAsync(request);

                string body =
                    await response.Content.ReadAsStringAsync();

                Debug.WriteLine("============================================================");
                Debug.WriteLine("OSU TAGS API TEST");
                Debug.WriteLine($"HTTP = {(int)response.StatusCode} ({response.StatusCode})");
                Debug.WriteLine("RESPONSE:");
                Debug.WriteLine(body);
                Debug.WriteLine("============================================================");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("============================================================");
                Debug.WriteLine("OSU TAGS API ERROR");
                Debug.WriteLine(ex);
                Debug.WriteLine("============================================================");
            }
        }

        private string? osuAccessToken;
        private DateTime osuTokenExpiration;

        private async Task<string> GetOsuAccessToken()
        {
            if (!string.IsNullOrWhiteSpace(osuAccessToken) &&
                DateTime.UtcNow < osuTokenExpiration)
            {
                return osuAccessToken;
            }

            string? clientSecret =
                Environment.GetEnvironmentVariable(
                    "BEATINSIGHT_OSU_CLIENT_SECRET");

            if (string.IsNullOrWhiteSpace(clientSecret))
                throw new Exception(
                    "BEATINSIGHT_OSU_CLIENT_SECRET est introuvable.");

            Debug.WriteLine(
                $"OSU AUTH | Client ID = 66257");

            Debug.WriteLine(
                $"OSU AUTH | Secret présent = {!string.IsNullOrWhiteSpace(clientSecret)}");

            Debug.WriteLine(
                $"OSU AUTH | Secret longueur = {clientSecret?.Length ?? 0}");

            using HttpRequestMessage request =
                new HttpRequestMessage(
                    HttpMethod.Post,
                    "https://osu.ppy.sh/oauth/token");

            request.Headers.Add(
                "Accept",
                "application/json");

            request.Content =
    new FormUrlEncodedContent(
        new Dictionary<string, string>
        {
            ["client_id"] = "66257",
            ["client_secret"] = OsuSecrets.ClientSecret,
            ["grant_type"] = "client_credentials",
            ["scope"] = "public"
        });

            using HttpResponseMessage response =
    await client.SendAsync(request);

            string responseBody =
     await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(
                    $"osu! API HTTP {(int)response.StatusCode} ({response.StatusCode})\n" +
                    $"Response: {responseBody}"
                );
            }

            using JsonDocument document =
                JsonDocument.Parse(responseBody);

            

            osuAccessToken =
             document.RootElement
         .GetProperty("access_token")
         .GetString()
     ?? throw new Exception("osu! API : access_token absent de la réponse.");

            int expiresIn =
                document.RootElement
                    .GetProperty("expires_in")
                    .GetInt32();

            osuTokenExpiration =
                DateTime.UtcNow.AddSeconds(expiresIn - 60);

            Debug.WriteLine(
                $"OSU API AUTH OK | Expires in {expiresIn}s");

            return osuAccessToken!;
        }

        private async Task<bool> IsTosuAvailable()
        {
            try
            {
                using HttpResponseMessage response =
                    await client.GetAsync(
                        "http://127.0.0.1:24050/json",
                        HttpCompletionOption.ResponseHeadersRead);

                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private void SetTosuStatus(bool connected)
        {
            TosuStatusText.Text = connected
                ? "● Tosu connecté"
                : "● En attente de Tosu...";
        }

        private async Task UpdateMap()
        {
            // ============================================================
            // TOSU CONNECTION
            // ============================================================

            if (!await IsTosuAvailable())
            {
                if (tosuConnected)
                {
                    tosuConnected = false;

                    Debug.WriteLine(
                        "TOSU | Déconnecté.");
                }

                SetTosuStatus(false);

                return;
            }

            // Tosu vient d'être détecté.
            if (!tosuConnected)
            {
                tosuConnected = true;

                Debug.WriteLine(
                    "TOSU | Connecté.");

                SetTosuStatus(true);
            }

            // ============================================================
            // RÉCUPÉRATION DES DONNÉES TOSU
            // ============================================================

            string json = await client.GetStringAsync(
                $"http://127.0.0.1:24050/json?t={DateTime.UtcNow.Ticks}"
            );

            // On récupère le dossier racine dans lequel osu! stocke ses beatmaps.
            JsonDocument document = JsonDocument.Parse(json);

            int debugBeatmapId = document.RootElement
                .GetProperty("menu")
                .GetProperty("bm")
                .GetProperty("id")
                .GetInt32();

            string debugFile = document.RootElement
                .GetProperty("menu")
                .GetProperty("bm")
                .GetProperty("path")
                .GetProperty("file")
                .GetString()!;

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

            

            string chemin = System.IO.Path.Combine(
                songs,
                folder,
                file
            );

            // ============================================================
            // MÊME MAP → ON NE FAIT RIEN
            // ============================================================

            if (chemin == currentMapPath)
            {
                return;
            }

            // ============================================================
            // NOUVELLE MAP
            // ============================================================

            // On mémorise immédiatement la nouvelle map
            currentMapPath = chemin;

            // URL osu!
            currentBeatmapUrl = $"https://osu.ppy.sh/b/{beatmapId}";

           
            Beatmap beatmap = await Task.Run(() =>
            BeatmapParser.Load(chemin));

            // ============================================================
            // COMMUNITY TAGS
            // ============================================================

            List<OsuApiService.OsuTagVote> osuCommunityTags =
                await osuApi.GetBeatmapCommunityTags(beatmapId);

            beatmap.CommunityTags =
                osuCommunityTags
                    .Select(tag => new CommunityTag
                    {
                        Name = tag.Name,
                        Votes = tag.Votes
                    })
                    .ToList();


            // ============================================================
            // COMMUNITY TAGS <-> GAMEPLAY IDENTITY
            // ============================================================

            GameplayProfile gameplayProfile =
                beatmap.GameplayProfile;

            GameplayIdentity identity =
                gameplayProfile.Identity;


            // ------------------------------------------------------------
            // Comparaison
            // ------------------------------------------------------------

            GameplayTagComparisonResult tagComparison =
                GameplayTagComparer.Compare(
                    beatmap.CommunityTags,
                    identity.FullName,
                    identity.Traits);


            // ------------------------------------------------------------
            // Stockage du résultat
            // ------------------------------------------------------------

            beatmap.TagComparison =
                tagComparison;


            // ============================================================
            // DEBUG
            // ============================================================

            Debug.WriteLine(
                "===== TAG / GAMEPLAY IDENTITY =====");

            Debug.WriteLine(
                $"GAMEPLAY IDENTITY = {identity.FullName}");

            Debug.WriteLine(
                $"PRIMARY = {identity.Primary}");

            Debug.WriteLine(
                $"SECONDARY = {identity.Secondary}");

            Debug.WriteLine(
                $"PATTERN = {identity.Pattern}");

            Debug.WriteLine(
                $"IDENTITY CONFIDENCE = {identity.Confidence:F1}%");

            


            // ------------------------------------------------------------
            // Traits
            // ------------------------------------------------------------

            Debug.WriteLine(
                $"TRAITS = {(identity.Traits.Count > 0
                    ? string.Join(" | ", identity.Traits)
                    : "None")}");


            // ------------------------------------------------------------
            // Community
            // ------------------------------------------------------------

            if (!tagComparison.HasTags)
            {
                Debug.WriteLine(
                    "TAG COMPARISON = Unavailable");

                Debug.WriteLine(
                    "COMMUNITY TAGS = 0");

                Debug.WriteLine(
                    "COMMUNITY VOTES = 0");
            }
            else
            {
                Debug.WriteLine(
                    $"TAG CONSISTENCY = {tagComparison.Score * 100:F1}%");

                Debug.WriteLine(
                    $"TAG STATUS = {tagComparison.Status}");

                Debug.WriteLine(
                    $"TOTAL COMMUNITY VOTES = {tagComparison.TotalVotes}");

                foreach (GameplayTagComparison match
                         in tagComparison.Matches)
                {
                    Debug.WriteLine(
                        $"TAG = {match.Tag} | " +
                        $"VOTES = {match.Votes} | " +
                        $"STATUS = {match.Status} | " +
                        $"SCORE = {match.Score * 100:F1}% | " +
                        $"WEIGHT = {match.VoteWeight:F3} | " +
                        $"CONCEPTS = {string.Join(", ", match.Concepts)}");
                }
            }

            Debug.WriteLine("===== TAG / GAMEPLAY IDENTITY =====");

            if (!tagComparison.HasTags)
            {
                Debug.WriteLine(
                    "TAG COMPARISON = Unavailable | No community tags");
            }
            else
            {
                Debug.WriteLine(
                    $"TAG CONSISTENCY = {tagComparison.Score * 100:F1}%");

                Debug.WriteLine(
                    $"TAG STATUS = {tagComparison.Status}");

                Debug.WriteLine(
                    $"TOTAL COMMUNITY VOTES = {tagComparison.TotalVotes}");

               
            }

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
