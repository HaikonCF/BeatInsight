namespace BeatInsight.Services.Download;

/// <summary>
/// Ouvre un fichier via l'association Windows par défaut. BeatInsight ne
/// manipule jamais la base interne d'osu! : il se contente de remettre le
/// fichier au système d'exploitation, exactement comme un double-clic
/// utilisateur.
/// </summary>
internal interface IBeatmapImportShell
{
    bool TryOpen(string filePath, out string? failureReason);
}

internal sealed class ProcessBeatmapImportShell : IBeatmapImportShell
{
    public bool TryOpen(string filePath, out string? failureReason)
    {
        try
        {
            using System.Diagnostics.Process? process =
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = filePath,
                        UseShellExecute = true,
                    });

            failureReason = null;
            return true;
        }
        catch (Exception ex)
        {
            failureReason = ex.Message;
            return false;
        }
    }
}
