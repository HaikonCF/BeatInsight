using BeatInsight.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace BeatInsight.Parser
{
    internal class BeatmapParser
    {
        public static Beatmap Load(string filePath)
        {
            Beatmap beatmap = new Beatmap();

            string[] lines = File.ReadAllLines(filePath);

            string currentSection = "";

            // On parcourt toutes les lignes du fichier .osu une par une pour savoir dans quelle section elles se trouvent et récupérer les données.
            foreach (string line in lines)
            {
                // Une ligne entre crochets comme [Metadata] indique le début d'une nouvelle section du fichier .osu.
                if ((line.StartsWith("[")) && (line.EndsWith("]")))
                {
                    currentSection = line.Trim('[', ']');
                    continue;
                }

                // On ne traite les informations suivantes que lorsqu'on se trouve dans la section [Metadata].
                if (currentSection == "Metadata")
                {
                    // On vérifie si cette ligne contient le titre afin de l'enregistrer dans la Beatmap.
                    if (line.StartsWith("Title:"))
                    {
                        beatmap.Title = line.Substring(6);
                    }
                    // On vérifie si cette ligne contient l'artiste afin de l'enregistrer dans la Beatmap.
                    if (line.StartsWith("Artist:"))
                    {
                        beatmap.Artist = line.Substring(7);
                    }
                    // On vérifie si cette ligne contient le créateur de la difficulté afin de l'enregistrer dans la Beatmap.
                    if (line.StartsWith("Creator:"))
                    {
                        beatmap.Creator = line.Substring(8);
                    }
                    // On vérifie si cette ligne contient le nom/version de la difficulté afin de l'enregistrer dans la Beatmap.
                    if (line.StartsWith("Version:"))
                    {
                        beatmap.Version = line.Substring(8);
                    }
                }
                // On ne lit les paramètres AR, OD, CS, HP et sliders que dans la section [Difficulty].
                if (currentSection == "Difficulty")
                {
                    // On récupère l'Approach Rate de la difficulté actuelle.
                    if (line.StartsWith("ApproachRate:"))
                    {
                        beatmap.AR = double.Parse(line.Substring(13), CultureInfo.InvariantCulture);
                    }

                    // On récupère l'Overall Difficulty de la difficulté actuelle.
                    if (line.StartsWith("OverallDifficulty:"))
                    {
                        beatmap.OD = double.Parse(line.Substring(18), CultureInfo.InvariantCulture);
                    }

                    // On récupère la taille des cercles (Circle Size).
                    if (line.StartsWith("CircleSize:"))
                    {
                        beatmap.CS = double.Parse(line.Substring(11), CultureInfo.InvariantCulture);
                    }

                    // On récupère la valeur de HP Drain.
                    if (line.StartsWith("HPDrainRate:"))
                    {
                        beatmap.HP = double.Parse(line.Substring(12), CultureInfo.InvariantCulture);
                    }

                    // On récupère le multiplicateur utilisé pour calculer les sliders.
                    if (line.StartsWith("SliderMultiplier:"))
                    {
                        beatmap.SliderMultiplier = double.Parse(line.Substring(17), CultureInfo.InvariantCulture);
                    }

                    // On récupère la fréquence des ticks des sliders. Elle peut être décimale, donc on utilise un double.
                    if (line.StartsWith("SliderTickRate:"))
                    {
                        beatmap.SliderTickRate = double.Parse(line.Substring(15), CultureInfo.InvariantCulture);
                    }

                }
                // On ne lit les lignes suivantes que dans la section [TimingPoints].
                if (currentSection == "TimingPoints")
                {
                    // On ignore les lignes vides pour éviter d'essayer de les découper comme des points de timing.
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        string[] valeurs = line.Split(',');
                        TimingPoint timingPoint = new TimingPoint();
                        timingPoint.Time = double.Parse(valeurs[0], CultureInfo.InvariantCulture);
                        timingPoint.BeatLength = double.Parse(valeurs[1], CultureInfo.InvariantCulture);
                        timingPoint.Uninherited = valeurs[6] == "1";
                        beatmap.TimingPoints.Add(timingPoint);

                    }
                }
                // On ne crée des HitObjects que dans la section [HitObjects].
                if (currentSection == "HitObjects")
                {
                    // On ignore les lignes vides pour éviter de créer un objet invalide.
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        string[] valeurs = line.Split(',');
                        HitObject hitObject = new HitObject();
                        hitObject.X = int.Parse(valeurs[0], CultureInfo.InvariantCulture);
                        hitObject.Y = int.Parse(valeurs[1], CultureInfo.InvariantCulture);
                        hitObject.Type = int.Parse(valeurs[3], CultureInfo.InvariantCulture);
                        hitObject.Time = int.Parse(valeurs[2], CultureInfo.InvariantCulture);

                        // Le bit 2 indique qu'il s'agit d'un slider : on peut donc lire ses répétitions et sa longueur.
                        if ((hitObject.Type & 2) == 2)
                        {
                            hitObject.Slides = int.Parse(valeurs[6], CultureInfo.InvariantCulture);
                            hitObject.Length = double.Parse(valeurs[7], CultureInfo.InvariantCulture);

                            // Le premier élément décrit la forme du slider
                            // (L, B, C ou P) ; les suivants sont ses points de
                            // contrôle. Ils serviront au signal Tech V0.
                            string[] curveParts = valeurs[5].Split('|');
                            hitObject.SliderCurveType = curveParts[0];

                            for (int i = 1; i < curveParts.Length; i++)
                            {
                                string[] pointParts = curveParts[i].Split(':');
                                if (pointParts.Length != 2)
                                    continue;

                                hitObject.SliderControlPoints.Add(new SliderControlPoint
                                {
                                    X = int.Parse(pointParts[0], CultureInfo.InvariantCulture),
                                    Y = int.Parse(pointParts[1], CultureInfo.InvariantCulture)
                                });
                            }
                        }
                        beatmap.HitObjects.Add(hitObject);

                    }
                }

            }

            // On parcourt les points de timing pour trouver le premier timing principal hérité par la map et en déduire son BPM.
            foreach (TimingPoint timingPoint in beatmap.TimingPoints)
            {
                // Un timing non hérité représente un BPM de base, contrairement aux points de timing qui modifient seulement la vitesse du slider.
                if (timingPoint.Uninherited == true)
                {
                    beatmap.BPM = (int)(60000 / timingPoint.BeatLength);
                    break;
                }

            }
            int totalSliderRepeats = 0;
            int totalSliderTails = 0;
            int totalSliderTicks = 0;

            // On parcourt tous les objets pour compter séparément les cercles, sliders et spinners.
            foreach (HitObject hitObject in beatmap.HitObjects)
            {
                // Le bit 1 correspond à un cercle, donc on augmente le compteur de cercles.
                if ((hitObject.Type & 1) == 1)
                {
                    beatmap.CircleCount++;
                }

                // Le bit 2 correspond à un slider, donc on augmente le compteur de sliders.
                if ((hitObject.Type & 2) == 2)
                {
                    beatmap.SliderCount++;
                }

                // Le bit 8 correspond à un spinner, donc on augmente le compteur de spinners.
                if ((hitObject.Type & 8) == 8)
                {
                    beatmap.SpinnerCount++;
                }
            }

            int lenght = beatmap.HitObjects[beatmap.HitObjects.Count - 1].Time - beatmap.HitObjects[0].Time;
            TimeSpan duree = TimeSpan.FromMilliseconds(lenght);
            beatmap.Lenght = duree;
            beatmap.LengthDisplay = $"{duree.Minutes}:{duree.Seconds:00}";

            beatmap.MaxCombo = beatmap.HitObjects.Count;


            //Debug.WriteLine($"SliderMultiplier = {beatmap.SliderMultiplier}");
            //Debug.WriteLine($"SliderTickRate = {beatmap.SliderTickRate}");



            // On reparcourt les HitObjects pour calculer le combo maximal, car les sliders ajoutent des répétitions, une fin et des ticks au combo.
            foreach (HitObject hitObject in beatmap.HitObjects)
            {
                // Seuls les sliders peuvent ajouter des éléments supplémentaires au combo.
                if ((hitObject.Type & 2) == 2)
                {
                    beatmap.MaxCombo += hitObject.Slides - 1;
                    beatmap.MaxCombo += 1;

                    totalSliderRepeats += hitObject.Slides - 1;
                    totalSliderTails++;

                    int sliderTicks = 0;

                    double sliderVelocity = GetSliderVelocity(beatmap, hitObject.Time);

                    double tickDistance = (beatmap.SliderMultiplier * 100) / beatmap.SliderTickRate * sliderVelocity;

                    double minDistanceFromEnd = sliderVelocity * 10;

                    double distance = tickDistance;

                    // On avance de tick en tick tant qu'il reste assez de longueur sur le slider pour placer un tick valide.
                    while (distance <= hitObject.Length)
                    {
                        // On évite de placer un tick trop près de la fin du slider, car cette zone est réservée à la fin du slider.
                        if (distance >= hitObject.Length - minDistanceFromEnd)
                        {
                            break;
                        }

                        sliderTicks++;

                        distance += tickDistance;
                    }
                    // On affiche le détail du slider seulement lorsqu'au moins un tick a réellement été calculé.
                    if (sliderTicks > 0)
                    {
                        //Debug.WriteLine($"TICK -> Time={hitObject.Time} | Length={hitObject.Length} | Slides={hitObject.Slides} | SV={sliderVelocity} | Ticks={sliderTicks}");
                    }

                    totalSliderTicks += sliderTicks;

                    beatmap.MaxCombo += sliderTicks;
                }
            }
            //Debug.WriteLine($"TOTAL SLIDER TICKS = {totalSliderTicks}");
            //Debug.WriteLine($"MAX COMBO = {beatmap.MaxCombo}");

            // Le profil est volontairement isolé du Star Rating actuel :
            // il sert d'abord à observer et calibrer les patterns.
            beatmap.GameplayProfile = global::BeatInsight.Analysis.GameplayAnalyzer.Analyze(beatmap);

            CalculateMovementStats(beatmap);

            return beatmap;
        }
        private static double GetSliderVelocity(Beatmap beatmap, int time)
        {
            double sliderVelocity = 1.0;

            // On parcourt les points de timing dans l'ordre pour trouver le dernier changement de vitesse applicable à cet objet.
            foreach (TimingPoint timingPoint in beatmap.TimingPoints)
            {
                // Dès qu'on dépasse le temps de l'objet, les points suivants ne peuvent plus l'influencer : on peut arrêter la recherche.
                if (timingPoint.Time > time)
                    break;

                // Un point non hérité avec une longueur négative définit un SV (Slider Velocity) utilisable pour le slider.
                if (!timingPoint.Uninherited && timingPoint.BeatLength < 0)
                {
                    sliderVelocity = -100.0 / timingPoint.BeatLength;
                }
            }
            return sliderVelocity;
        }

        private static void CalculateMovementStats(Beatmap beatmap)
        {
            List<double> speeds = new List<double>();

            int objectCount = beatmap.HitObjects.Count;

            // On récupère le temps du premier objet de la map.
            double firstObjectTime = beatmap.HitObjects.First().Time;

            // On récupère le temps du dernier objet de la map.
            double lastObjectTime = beatmap.HitObjects.Last().Time;

            // On calcule la durée entre le premier et le dernier objet.
            double mapLength = lastObjectTime - firstObjectTime;

            Debug.WriteLine($"MAP LENGTH = {mapLength / 1000.0:F2} seconds");

            // On garde la difficulté moyenne des sections
            // pour pouvoir l'utiliser plus loin dans le calcul global.
            double averageSectionDifficulty = 0;

            // On garde la difficulté maximale des sections
            // pour pouvoir l'utiliser plus loin dans le calcul global.
            double maxSectionDifficulty = 0;

            double objectDensity = 0;

            // Il faut au moins deux objets pour calculer une durée et une densité d'objets par seconde.
            if (objectCount > 1)
            {
                double firstTime = beatmap.HitObjects[0].Time;
                double lastTime = beatmap.HitObjects[^1].Time;

                double durationSeconds = (lastTime - firstTime) / 1000.0;

                // On évite une division par zéro si les deux objets ont exactement le même timestamp.
                if (durationSeconds > 0)
                {
                    objectDensity = objectCount / durationSeconds;
                }
            }

            // On découpe la map en fenêtres d'une seconde.
            // Le but est de connaître la quantité d'objets présente
            // dans chaque partie de la map.
            if (beatmap.HitObjects.Count > 1)
            {
                double firstTime = beatmap.HitObjects[0].Time;
                double lastTime = beatmap.HitObjects[^1].Time;
                // On garde la plus grande densité rencontrée dans une section.
                int maxSectionDensity = 0;

                // On additionne le nombre d'objets de toutes les sections
                // pour pouvoir calculer la densité moyenne à la fin.
                int totalSectionObjects = 0;

                // On garde le plus gros strain de densité rencontré dans la map.
                double maxDensityStrain = 0;

                // On additionne les difficultés de toutes les sections non vides.
                double totalSectionDifficulty = 0;

                // On compte combien de sections contiennent réellement des objets.
                int sectionDifficultyCount = 0;

                // On additionne les strains pour pouvoir observer
                // le niveau moyen de difficulté des sections.
                double totalDensityStrain = 0;

                // On avance d'une seconde à chaque nouvelle section.
                for (double sectionStart = firstTime; sectionStart < lastTime;
                     sectionStart += 1000)
                {

                    double sectionEnd = sectionStart + 1000;

                    int objectsInSection = 0;

                    // On additionnera les vitesses des mouvements
                    // appartenant à cette section.
                    double sectionTotalSpeed = 0;

                    // On compte combien de mouvements ont une vitesse calculée
                    // dans cette section.
                    int sectionSpeedCount = 0;

                    // On parcourt les objets à partir du deuxième,
                    // car le premier n'a pas d'objet précédent avec lequel comparer.
                    for (int i = 1; i < beatmap.HitObjects.Count; i++)
                    {
                        HitObject previous = beatmap.HitObjects[i - 1];
                        HitObject current = beatmap.HitObjects[i];

                        // On vérifie si l'objet actuel appartient à notre section.
                        if (current.Time >= sectionStart &&
                            current.Time < sectionEnd)
                        {
                            objectsInSection++;

                            // On calcule le temps nécessaire pour passer
                            // de l'objet précédent à l'objet actuel.
                            double deltaTime = current.Time - previous.Time;

                            // On calcule la différence de position entre
                            // l'objet précédent et l'objet actuel.
                            double deltaX = current.X - previous.X;
                            double deltaY = current.Y - previous.Y;

                            // On calcule la distance réelle parcourue entre les deux objets.
                            double distance = Math.Sqrt(
                                deltaX * deltaX +
                                deltaY * deltaY
                            );

                            // On calcule la vitesse du mouvement en pixels par seconde.
                            double speed = distance / (deltaTime / 1000.0);

                            // On ajoute la vitesse de ce mouvement au total de la section.
                            sectionTotalSpeed += speed;

                            // On compte cette vitesse parmi les mouvements de la section.
                            sectionSpeedCount++;
                        }
                    }

                    // On calcule la vitesse moyenne des mouvements de cette section.
                    // On vérifie d'abord qu'il existe au moins une vitesse valide.
                    double sectionAverageSpeed = 0;

                    if (sectionSpeedCount > 0)
                    {
                        sectionAverageSpeed = sectionTotalSpeed / sectionSpeedCount;
                    }

                    // On calcule un facteur qui représente la quantité d'objets
                    // présents dans cette section.
                    double densityFactor = Math.Min(objectsInSection / 5.0, 1.0);

                    // On transforme la vitesse en une valeur plus facile
                    // à comparer avec le strain de densité.
                    double sectionSpeedDifficulty = sectionAverageSpeed / 1000.0;

                    // On réduit l'influence de la vitesse lorsque la section
                    // contient peu d'objets.
                    sectionSpeedDifficulty *= densityFactor;

                    //Debug.WriteLine($"SECTION SPEED DIFFICULTY -> " +$"{sectionSpeedDifficulty:F2}");

                    // On affiche la vitesse moyenne de la section dans le debug.
                    //Debug.WriteLine( $"SECTION SPEED -> {sectionStart:F0}ms - {sectionEnd:F0}ms | " + $"Average Speed = {sectionAverageSpeed:F2} px/s");

                    // On ajoute les objets de cette section au total.
                    totalSectionObjects += objectsInSection;

                    // Si cette section contient plus d'objets que notre précédent maximum,
                    // elle devient la nouvelle section la plus dense.
                    if (objectsInSection > maxSectionDensity)
                    {
                        maxSectionDensity = objectsInSection;
                    }

                    // On transforme le nombre d'objets de la section en une
                    // première valeur de difficulté.
                    // Plus il y a d'objets dans une seconde, plus le strain augmente.
                    double sectionDensityStrain = objectsInSection / 5.0;

                    // On combine la difficulté liée à la densité
                    // et la difficulté liée à la vitesse.
                    double sectionDifficulty = sectionDensityStrain + sectionSpeedDifficulty;

                    // On ajoute cette difficulté au total uniquement si la section
                    // contient au moins un objet.
                    if (sectionDifficulty > 0)
                    {
                        totalSectionDifficulty += sectionDifficulty;
                        sectionDifficultyCount++;
                    }

                    // Si cette section est plus difficile que toutes les précédentes,
                    // elle devient notre nouvelle difficulté maximale.
                    if (sectionDifficulty > maxSectionDifficulty)
                    {
                        maxSectionDifficulty = sectionDifficulty;
                    }

                    //Debug.WriteLine($"SECTION DIFFICULTY -> {sectionDifficulty:F2}");

                    // On ajoute le strain de cette section au total.
                    totalDensityStrain += sectionDensityStrain;

                    // Si cette section est plus difficile que toutes les précédentes,
                    // elle devient notre nouveau strain maximum.
                    if (sectionDensityStrain > maxDensityStrain)
                    {
                        maxDensityStrain = sectionDensityStrain;
                    }

                    //Debug.WriteLine($"SECTION STRAIN -> {sectionStart:F0}ms - {sectionEnd:F0}ms | " +$"Density = {objectsInSection} | Strain = {sectionDensityStrain:F2}");

                    //Debug.WriteLine($"SECTION -> {sectionStart:F0}ms - {sectionEnd:F0}ms | Objects = {objectsInSection}");

                }
                // On calcule le strain moyen de toutes les sections.
                double averageDensityStrain = totalDensityStrain / ((lastTime - firstTime) / 1000.0);

                if (sectionDifficultyCount > 0)
                {
                    averageSectionDifficulty =
                        totalSectionDifficulty / sectionDifficultyCount;
                }

                //Debug.WriteLine($"MAX SECTION DIFFICULTY = {maxSectionDifficulty:F2}");

                //Debug.WriteLine( $"AVERAGE SECTION DIFFICULTY = {averageSectionDifficulty:F2}");

                //Debug.WriteLine($"MAX DENSITY STRAIN = {maxDensityStrain:F2}");
                //Debug.WriteLine($"AVERAGE DENSITY STRAIN = {averageDensityStrain:F2}");
            }

            double totalDistance = 0;

            // On compare chaque objet avec celui qui le précède afin de mesurer la distance parcourue et la vitesse entre les deux.
            for (int i = 1; i < beatmap.HitObjects.Count; i++)
            {
                HitObject previous = beatmap.HitObjects[i - 1];
                HitObject current = beatmap.HitObjects[i];

                double deltaTime = current.Time - previous.Time;

                // Un temps nul ou négatif ne permet pas de calculer une vitesse de mouvement valide, donc on ignore cette transition.
                if (deltaTime <= 0)
                    continue;

                double deltaX = current.X - previous.X;
                double deltaY = current.Y - previous.Y;

                double distance = Math.Sqrt(
                    (deltaX * deltaX) +
                    (deltaY * deltaY)
                );

                double speed = distance / (deltaTime / 1000.0);

                totalDistance += distance;
                speeds.Add(speed);

            }

            double maxLocalDensity = 0;
            int denseWindows = 0;
            int extremeDensityWindows = 0;
            int totalWindows = 0;

            // Il faut au moins deux objets pour avoir une période sur laquelle mesurer des fenêtres de densité.
            if (beatmap.HitObjects.Count > 1)
            {
                double firstTime = beatmap.HitObjects[0].Time;
                double lastTime = beatmap.HitObjects[^1].Time;

                // On parcourt la map par fenêtres d'une seconde pour mesurer localement combien d'objets apparaissent.
                for (double windowStart = firstTime;
                     windowStart < lastTime;
                     windowStart += 1000)
                {
                    double windowEnd = windowStart + 1000;

                    int objectsInWindow = 0;

                    // On examine chaque HitObject pour savoir s'il appartient à la fenêtre temporelle actuelle.
                    foreach (HitObject hitObject in beatmap.HitObjects)
                    {
                        // On compte uniquement les objets dont le temps tombe dans la fenêtre d'une seconde en cours.
                        if (hitObject.Time >= windowStart &&
                            hitObject.Time < windowEnd)
                        {
                            objectsInWindow++;
                        }
                    }

                    double localDensity = objectsInWindow;
                    totalWindows++;

                    // À partir de 6 objets dans une seconde, on considère cette fenêtre comme dense.
                    if (localDensity >= 6)
                        denseWindows++;

                    // À partir de 10 objets dans une seconde, on considère cette fenêtre comme extrêmement dense.
                    if (localDensity >= 10)
                        extremeDensityWindows++;

                    // On conserve la plus forte densité locale rencontrée afin de connaître le pic de densité de la map.
                    if (localDensity > maxLocalDensity)
                        maxLocalDensity = localDensity;
                }
            }

            double denseRatio = 0;
            double extremeDensityRatio = 0;

            // On ne calcule les ratios que s'il existe au moins une fenêtre, pour éviter une division par zéro.
            if (totalWindows > 0)
            {
                denseRatio = (double)denseWindows / totalWindows;
                extremeDensityRatio = (double)extremeDensityWindows / totalWindows;
            }

            double averageSpeed = 0;
            double maxSpeed = 0;
            double weightedSpeed = 0;

            // On calcule les statistiques de vitesse seulement s'il existe au moins une transition valide entre des objets.
            if (speeds.Count > 0)
            {
                averageSpeed = speeds.Average();
                maxSpeed = speeds.Max();

                double totalWeight = 0;

                // On parcourt toutes les vitesses pour leur attribuer un poids : les vitesses élevées doivent influencer davantage la moyenne pondérée.
                foreach (double speed in speeds)
                {
                    double weight = speed * speed;

                    weightedSpeed += speed * weight;
                    totalWeight += weight;
                }

                // On vérifie que le poids total est positif avant de diviser pour obtenir la vitesse pondérée.
                if (totalWeight > 0)
                {
                    weightedSpeed /= totalWeight;
                }

            }
            int[] speedIntervals = new int[7];

            // On répartit chaque vitesse dans une tranche afin de voir comment la vitesse est distribuée dans toute la map.
            foreach (double speed in speeds)
            {
                // Les mouvements sous 400 px/s sont placés dans la première tranche.
                if (speed < 400)
                    speedIntervals[0]++;
                // Les mouvements de 400 à moins de 600 px/s sont placés dans la deuxième tranche.
                else if (speed < 600)
                    speedIntervals[1]++;
                // Les mouvements de 600 à moins de 800 px/s sont placés dans la troisième tranche.
                else if (speed < 800)
                    speedIntervals[2]++;
                // Les mouvements de 800 à moins de 1000 px/s sont placés dans la quatrième tranche.
                else if (speed < 1000)
                    speedIntervals[3]++;
                // Les mouvements de 1000 à moins de 1200 px/s sont placés dans la cinquième tranche.
                else if (speed < 1200)
                    speedIntervals[4]++;
                // Les mouvements de 1200 à moins de 1500 px/s sont placés dans la sixième tranche.
                else if (speed < 1500)
                    speedIntervals[5]++;
                else
                    speedIntervals[6]++;
            }
            double speedDifficulty = 0;

            // On calcule le score de vitesse seulement s'il existe des vitesses à analyser.
            if (speeds.Count > 0)
            {
                double weightedIntervalScore =
                      speedIntervals[0] * 0.00
                    + speedIntervals[1] * 0.25
                    + speedIntervals[2] * 0.50
                    + speedIntervals[3] * 1.00
                    + speedIntervals[4] * 1.50
                    + speedIntervals[5] * 2.00
                    + speedIntervals[6] * 3.00;

                speedDifficulty = weightedIntervalScore / speeds.Count;
            }

            // On transforme la difficulté Speed en une petite contribution
            // utilisable plus tard dans la difficulté globale.
            double speedContribution = speedDifficulty * 0.10;

            //Debug.WriteLine($"SPEED CONTRIBUTION = {speedContribution:F2}");

            double totalAimDistance = 0;
            double maxAimDistance = 0;
            double averageAimDistance = 0;

            double totalAimMovementSpeed = 0;
            double maxAimMovementSpeed = 0;

            int aimMovementCount = 0;
            int aimSpeedCount = 0;

            // On compare chaque HitObject avec le précédent pour mesurer les distances de mouvement utilisées par les statistiques Aim.
            for (int i = 1; i < beatmap.HitObjects.Count; i++)
            {
                HitObject previous = beatmap.HitObjects[i - 1];
                HitObject current = beatmap.HitObjects[i];

                double dx = current.X - previous.X;
                double dy = current.Y - previous.Y;

                double distance = Math.Sqrt(
                    dx * dx +
                    dy * dy
                );

                double timeDifference = current.Time - previous.Time;

                // On calcule la vitesse de déplacement Aim uniquement lorsque le temps disponible entre les deux objets est positif.
                if (timeDifference > 0)
                {
                    double movementSpeed =
                        distance / timeDifference * 1000.0;

                    totalAimMovementSpeed += movementSpeed;

                    // On conserve la vitesse de mouvement la plus élevée pour connaître le pic de vitesse Aim.
                    if (movementSpeed > maxAimMovementSpeed)
                        maxAimMovementSpeed = movementSpeed;

                    aimSpeedCount++;
                }

                totalAimDistance += distance;

                // On conserve la plus grande distance parcourue entre deux objets pour connaître le plus gros déplacement Aim.
                if (distance > maxAimDistance)
                    maxAimDistance = distance;

                aimMovementCount++;
            }

            double averageAimMovementSpeed = 0;

            // On calcule la vitesse Aim moyenne uniquement s'il existe au moins une vitesse valide.
            if (aimSpeedCount > 0)
            {
                averageAimMovementSpeed =
                    totalAimMovementSpeed / aimSpeedCount;
            }


            // On calcule la distance Aim moyenne uniquement s'il existe au moins un mouvement.
            if (aimMovementCount > 0)
            {
                averageAimDistance =
                    totalAimDistance / aimMovementCount;
            }

            //Debug.WriteLine($"TOTAL DISTANCE = {totalDistance:F2}");
            //Debug.WriteLine($"AVERAGE SPEED = {averageSpeed:F2} px/s");
            //Debug.WriteLine($"MAX SPEED = {maxSpeed:F2} px/s");
            //Debug.WriteLine($"WEIGHTED SPEED = {weightedSpeed:F2} px/s");
            //Debug.WriteLine("----- SPEED INTERVALS -----");

            //Debug.WriteLine($"< 400      : {speedIntervals[0]}");
            //Debug.WriteLine($"400 - 600  : {speedIntervals[1]}");
            //Debug.WriteLine($"600 - 800  : {speedIntervals[2]}");
            //Debug.WriteLine($"800 - 1000 : {speedIntervals[3]}");
            //Debug.WriteLine($"1000 - 1200: {speedIntervals[4]}");
            //Debug.WriteLine($"1200 - 1500: {speedIntervals[5]}");
            //Debug.WriteLine($">= 1500    : {speedIntervals[6]}");
            //Debug.WriteLine($"SPEED DIFFICULTY = {speedDifficulty:F2}");
            double fastRatio =
                (double)(speedIntervals[3]
                + speedIntervals[4]
                + speedIntervals[5]
                + speedIntervals[6])
                / speeds.Count;

            double extremeRatio =
           (double)(speedIntervals[5]
           + speedIntervals[6])
           / speeds.Count;

            //Debug.WriteLine($"FAST RATIO = {fastRatio:P2}");
            //Debug.WriteLine($"EXTREME RATIO = {extremeRatio:P2}");
            //Debug.WriteLine($"OBJECT COUNT = {objectCount}");
            //Debug.WriteLine($"OBJECT DENSITY = {objectDensity:F2} objects/s");
            //Debug.WriteLine($"MAX LOCAL DENSITY = {maxLocalDensity:F2} objects/s");
            //Debug.WriteLine($"DENSE RATIO = {denseRatio:P2}");
            //Debug.WriteLine($"EXTREME DENSITY RATIO = {extremeDensityRatio:P2}");
            //Debug.WriteLine("----- AIM -----");
            //Debug.WriteLine($"TOTAL AIM DISTANCE = {totalAimDistance:F2} px");
            //Debug.WriteLine($"AVERAGE AIM DISTANCE = {averageAimDistance:F2} px");
            //Debug.WriteLine($"MAX AIM DISTANCE = {maxAimDistance:F2} px");
            //Debug.WriteLine($"AVERAGE AIM MOVEMENT SPEED = {averageAimMovementSpeed:F2} px/s");
            //Debug.WriteLine($"MAX AIM MOVEMENT SPEED = {maxAimMovementSpeed:F2} px/s");

            double totalAngle = 0;
            double maxAngle = 0;
            int angleCount = 0;

            int angleUnder45 = 0;
            int angle45To90 = 0;
            int angle90To135 = 0;
            int angleOver135 = 0;

            // On prend trois objets consécutifs afin de mesurer le changement de direction entre le mouvement précédent et le suivant.
            for (int i = 1; i < beatmap.HitObjects.Count - 1; i++)
            {
                HitObject previous = beatmap.HitObjects[i - 1];
                HitObject current = beatmap.HitObjects[i];
                HitObject next = beatmap.HitObjects[i + 1];

                double vector1X = current.X - previous.X;
                double vector1Y = current.Y - previous.Y;

                double vector2X = next.X - current.X;
                double vector2Y = next.Y - current.Y;

                double length1 = Math.Sqrt(
                    vector1X * vector1X +
                    vector1Y * vector1Y
                );

                double length2 = Math.Sqrt(
                    vector2X * vector2X +
                    vector2Y * vector2Y
                );

                // Si deux objets sont au même endroit, un vecteur a une longueur nulle et l'angle n'est pas calculable : on ignore donc ce cas.
                if (length1 == 0 || length2 == 0)
                    continue;

                double dotProduct =
                    vector1X * vector2X +
                    vector1Y * vector2Y;

                double cosine = dotProduct / (length1 * length2);

                // Protection contre les petites erreurs flottantes
                cosine = Math.Clamp(cosine, -1.0, 1.0);

                double angle = Math.Acos(cosine) * (180.0 / Math.PI);

                totalAngle += angle;

                // On conserve le plus grand angle rencontré pour connaître le changement de direction maximal.
                if (angle > maxAngle)
                    maxAngle = angle;

                // Un angle inférieur à 45 degrés représente un changement de direction relativement faible.
                if (angle < 45)
                {
                    angleUnder45++;
                }
                // Entre 45 et 90 degrés, le changement de direction devient plus marqué.
                else if (angle < 90)
                {
                    angle45To90++;
                }
                // Entre 90 et 135 degrés, le mouvement change fortement de direction.
                else if (angle < 135)
                {
                    angle90To135++;
                }
                else
                {
                    angleOver135++;
                }

                angleCount++;
            }



            double averageAngle = 0;

            // On calcule l'angle moyen uniquement lorsqu'au moins un angle valide a été mesuré.
            if (angleCount > 0)
            {
                averageAngle = totalAngle / angleCount;
            }


            double sharpAngleRatio = 0;
            double reverseRatio = 0;

            // On calcule les ratios d'angles uniquement lorsqu'il existe des angles valides, afin d'éviter une division par zéro.
            if (angleCount > 0)
            {
                sharpAngleRatio =
                    (double)(angle90To135 + angleOver135) / angleCount;

                reverseRatio =
                    (double)angleOver135 / angleCount;
            }

            //Debug.WriteLine($"AVERAGE ANGLE = {averageAngle:F2}°");
            //Debug.WriteLine($"MAX ANGLE = {maxAngle:F2}°");
            //Debug.WriteLine("----- ANGLE INTERVALS -----");
            //Debug.WriteLine($"< 45°       : {angleUnder45}");
            //Debug.WriteLine($"45 - 90°    : {angle45To90}");
            //Debug.WriteLine($"90 - 135°   : {angle90To135}");
            //Debug.WriteLine($">= 135°     : {angleOver135}");
            //Debug.WriteLine($"SHARP ANGLE RATIO = {sharpAngleRatio:P2}");
            //Debug.WriteLine($"REVERSE RATIO = {reverseRatio:P2}");

            double aimBase = averageAimDistance / 100.0;

            double angleFactor =
                0.5 +
                sharpAngleRatio +
                reverseRatio * 0.5;

            double aimDifficulty =
                aimBase * angleFactor;

            // On mesure à quel point le passage le plus difficile
            // est au-dessus de la difficulté moyenne.
            double peakDifference = maxSectionDifficulty - averageSectionDifficulty;

            //Debug.WriteLine($"PEAK DIFFERENCE = {peakDifference:F2}");

            // On combine l'Aim et la difficulté moyenne des sections.
            // Pour l'instant, les deux ont le même poids.
            double baseDifficulty = (aimDifficulty + averageSectionDifficulty) / 2.0;

            // On commence à prendre en compte la longueur après 3 minutes.
            double lengthFactor = 1.0;

            double mapLengthSeconds = mapLength / 1000.0;

            if (mapLengthSeconds > 180)
            {
                lengthFactor +=
                    Math.Min(0.05, (mapLengthSeconds - 180) / 3600.0);
            }

            // On affiche le facteur pour pouvoir observer son influence.
            //Debug.WriteLine($"LENGTH FACTOR = {lengthFactor:F3}");

            // L'Overall Difficulty influence la difficulté de précision.
            // Pour l'instant on mesure seulement son influence.
            double accuracyDifficulty = beatmap.OD * 0.10;

            //Debug.WriteLine($"ACCURACY DIFFICULTY = {accuracyDifficulty:F2}");

            double accuracyContribution = accuracyDifficulty * 0.10;

            //Debug.WriteLine($"ACCURACY CONTRIBUTION = {accuracyContribution:F2}");

            // On ajoute la contribution du Speed à la difficulté de base.
            // Le facteur de longueur est ensuite appliqué à l'ensemble.
            double finalDifficulty = (baseDifficulty + (peakDifference * 0.10) + speedContribution + accuracyContribution) * lengthFactor;

            //Debug.WriteLine($"FINAL DIFFICULTY = {finalDifficulty:F2}" );

            // On convertit notre difficulté finale en une première estimation
            // du Star Rating. Le seuil évite qu'une difficulté faible fasse
            // descendre artificiellement les maps sous 5 étoiles.
            double starRating = 5.0 + 1.6 * Math.Pow(Math.Max(0, finalDifficulty - 1.3), 2.0);

            //Debug.WriteLine($"STAR RATING = {starRating:F2}★");



            //Debug.WriteLine($"AIM BASE = {aimBase:F2}");
            //Debug.WriteLine($"ANGLE FACTOR = {angleFactor:F2}");
            //Debug.WriteLine($"AIM DIFFICULTY = {aimDifficulty:F2}");
            //Debug.WriteLine($"BASE DIFFICULTY = {baseDifficulty:F2}");

        }
    }

}
