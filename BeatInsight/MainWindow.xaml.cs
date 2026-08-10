using BeatInsight.Models;
using BeatInsight.Parser;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

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

            string bg =
                document.RootElement
                .GetProperty("menu")
                .GetProperty("bm")
                .GetProperty("path")
                .GetProperty("bg")
                // Si le chemin est identique à celui de la dernière map traitée, rien n'a changé : on évite donc de recharger et recalculer la map.
                .GetString()!;

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


            // On passe le fichier .osu au parser pour transformer ses données brutes en objet Beatmap utilisable par le reste du programme.
            string backgroundPath = System.IO.Path.Combine(
                // On affiche le titre récupéré par le parser afin de vérifier rapidement dans le debug que la bonne map a été chargée.
                songs,
                folder,
                bg
            );

            // On donne la Beatmap à l'interface comme DataContext afin que les contrôles WPF puissent afficher automatiquement ses propriétés.
            BackgroundImage.Source =
                new BitmapImage(new Uri(backgroundPath));

            Beatmap beatmap = BeatmapParser.Load(chemin);
            Debug.WriteLine($"MAP = {beatmap.Title}");




            DataContext = beatmap;
        }
    }
}