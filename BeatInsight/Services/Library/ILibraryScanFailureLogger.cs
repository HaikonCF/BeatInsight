namespace BeatInsight.Services.Library;

/// <summary>
/// Enregistre les échecs individuels rencontrés pendant un scan de
/// bibliothèque. Cette interface permet au scanner de rester isolé
/// du support de persistance choisi pour ces diagnostics.
/// </summary>
internal interface ILibraryScanFailureLogger
{
    void LogFailure(string filePath, Exception exception);
}
