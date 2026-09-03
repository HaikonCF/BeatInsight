using BeatInsight.Models.Discovery;
using System.IO;

namespace BeatInsight.Services.CommunityDiscovery;

/// <summary>
/// Vérifie qu'un candidat de découverte possède réellement un fichier local
/// utilisable avant de proposer un chargement de revue. Cette décision est
/// purement transitoire : elle ne crée ni sample ML ni annotation humaine.
/// </summary>
internal static class CommunityDiscoveryReviewResolver
{
    internal static CommunityDiscoveryReviewTarget Resolve(
        CommunityBeatmapCandidate candidate,
        string? sourceFilePath)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (!candidate.AlreadyOwned
            || string.IsNullOrWhiteSpace(sourceFilePath)
            || !File.Exists(sourceFilePath))
        {
            return new CommunityDiscoveryReviewTarget(
                CanLoad: false,
                SourceFilePath: null,
                Status: "Map not installed locally.");
        }

        return new CommunityDiscoveryReviewTarget(
            CanLoad: true,
            SourceFilePath: sourceFilePath,
            Status: "Ready for review.");
    }
}

internal readonly record struct CommunityDiscoveryReviewTarget(
    bool CanLoad,
    string? SourceFilePath,
    string Status);
