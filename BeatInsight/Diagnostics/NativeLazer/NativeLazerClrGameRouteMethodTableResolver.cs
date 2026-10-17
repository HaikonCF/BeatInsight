using Microsoft.Diagnostics.Runtime;
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace BeatInsight.Diagnostics.NativeLazer;

/// <summary>
/// Generation-bound CLR type resolution. It is invoked after initial root
/// validation and only when a newly observed top needs missing route entries.
/// </summary>
internal sealed class NativeLazerClrGameRouteMethodTableResolver
    : INativeLazerGameRouteMethodTableResolver
{
    private const string SongSelectType = "osu.Game.Screens.Select.SoloSongSelect";
    private const string PlayerType = "osu.Game.Screens.Play.SoloPlayer";
    private const string ResultsType = "osu.Game.Screens.Ranking.SoloResultsScreen";

    public NativeLazerGameRouteResolution Resolve(
        NativeLazerProcessCandidate process,
        NativeLazerGenerationIdentity generation)
    {
        long started = Stopwatch.GetTimestamp();
        DataTarget? target = null;
        try
        {
            if (!IsCurrent(process, generation))
                return Failed("Process generation changed before MethodTable resolution.");

            string directory = Path.GetDirectoryName(process.ExecutablePath)
                ?? throw new InvalidOperationException("osu!lazer directory is unavailable.");
            string assemblyPath = Path.Combine(directory, "osu.Game.dll");
            string dacPath = Path.Combine(directory, "mscordaccore.dll");
            if (!File.Exists(assemblyPath) || !File.Exists(dacPath))
                return Failed("Required local osu!lazer CLR files are unavailable.");

            Dictionary<int, string> tokens = ReadCandidateTokens(assemblyPath);
            if (tokens.Count != 3)
                return Failed("One or more route CLR TypeDefs are unavailable.");

            target = DataTarget.AttachToProcess(
                process.ProcessId,
                suspend: true,
                new DataTargetOptions { SymbolPaths = Array.Empty<string>() });
            if (target.ClrVersions.Length != 1)
                return Failed($"Expected one CLR runtime, found {target.ClrVersions.Length}.");

            using ClrRuntime runtime = target.ClrVersions[0].CreateRuntime(
                dacPath,
                ignoreMismatch: true);
            Dictionary<string, ulong> tables = new(StringComparer.Ordinal);
            foreach (ClrModule module in runtime.EnumerateModules())
            {
                if (!string.Equals(
                        Path.GetFileName(module.Name),
                        "osu.Game.dll",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach ((ulong methodTable, int typeDef) in
                    module.EnumerateTypeDefToMethodTableMap())
                {
                    if (methodTable != 0 && tokens.TryGetValue(typeDef, out string? name))
                        tables[name] = methodTable;
                }
            }

            if (!IsCurrent(process, generation))
                return Failed("Process generation changed during MethodTable resolution.");
            ulong? songSelect = tables.TryGetValue(SongSelectType, out ulong songSelectValue)
                ? songSelectValue
                : null;
            ulong? player = tables.TryGetValue(PlayerType, out ulong playerValue)
                ? playerValue
                : null;
            ulong? results = tables.TryGetValue(ResultsType, out ulong resultsValue)
                ? resultsValue
                : null;

            var mapping = new NativeLazerGameRouteMethodTables(
                generation,
                songSelect,
                player,
                results);
            return mapping.IsCoherent
                ? new(generation, mapping,
                    Stopwatch.GetElapsedTime(started).TotalMilliseconds, null)
                : Failed(mapping.HasAny
                    ? "Resolved route MethodTables are incoherent."
                    : "No route MethodTables are currently constructed.");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return Failed($"MethodTable resolution failed with {exception.GetType().Name}.");
        }
        finally
        {
            target?.Dispose();
        }

        NativeLazerGameRouteResolution Failed(string reason) =>
            new(generation, null, Stopwatch.GetElapsedTime(started).TotalMilliseconds, reason);
    }

    private static Dictionary<int, string> ReadCandidateTokens(string assemblyPath)
    {
        HashSet<string> sought = new(
            [SongSelectType, PlayerType, ResultsType],
            StringComparer.Ordinal);
        Dictionary<int, string> result = new();
        using FileStream stream = File.OpenRead(assemblyPath);
        using PEReader peReader = new(stream);
        MetadataReader metadata = peReader.GetMetadataReader();
        foreach (TypeDefinitionHandle handle in metadata.TypeDefinitions)
        {
            TypeDefinition definition = metadata.GetTypeDefinition(handle);
            string name = metadata.GetString(definition.Name);
            string typeNamespace = metadata.GetString(definition.Namespace);
            string fullName = string.IsNullOrEmpty(typeNamespace)
                ? name
                : $"{typeNamespace}.{name}";
            if (sought.Contains(fullName))
                result[MetadataTokens.GetToken(handle)] = fullName;
        }
        return result;
    }

    private static bool IsCurrent(
        NativeLazerProcessCandidate process,
        NativeLazerGenerationIdentity generation)
    {
        try
        {
            using Process current = Process.GetProcessById(process.ProcessId);
            return generation.ProcessId == process.ProcessId &&
                generation.ProcessStartIdentity == process.StartTimeUtc &&
                !current.HasExited &&
                current.StartTime.ToUniversalTime() == generation.ProcessStartIdentity;
        }
        catch (Exception exception) when (exception is ArgumentException or
                                           InvalidOperationException or
                                           System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
