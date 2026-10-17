using Microsoft.Diagnostics.Runtime;
using System.Diagnostics;

namespace BeatInsight.Diagnostics.NativeLazer;

internal sealed class NativeLazerClrGameplayStatsMethodTableResolver
    : INativeLazerGameplayStatsMethodTableResolver
{
    public NativeLazerGameplayStatsMethodTables? Resolve(
        NativeLazerProcessCandidate process,
        NativeLazerGenerationIdentity generation,
        NativeLazerGameplayStatsLayout layout)
    {
        if (!IsCurrent(process, generation)) return null;
        string directory = Path.GetDirectoryName(process.ExecutablePath) ?? string.Empty;
        string dac = Path.Combine(directory, "mscordaccore.dll");
        if (!File.Exists(dac)) return null;

        try
        {
            using DataTarget target = DataTarget.AttachToProcess(process.ProcessId, suspend: false,
                new DataTargetOptions { SymbolPaths = Array.Empty<string>() });
            if (target.ClrVersions.Length != 1) return null;
            using ClrRuntime runtime = target.ClrVersions[0].CreateRuntime(dac, ignoreMismatch: true);
            ulong player = 0;
            ulong processor = 0;
            var mods = new Dictionary<ulong, string>();
            foreach (ClrModule module in runtime.EnumerateModules())
            {
                string file = Path.GetFileName(module.Name ?? string.Empty);
                if (NativeLazerGameplayStatsModuleNames.IsGame(file))
                {
                    foreach ((ulong methodTable, int token) in module.EnumerateTypeDefToMethodTableMap())
                    {
                        if (token == layout.SoloPlayerToken) player = methodTable;
                    }
                }
                else if (NativeLazerGameplayStatsModuleNames.IsOsuRuleset(file))
                {
                    foreach ((ulong methodTable, int token) in module.EnumerateTypeDefToMethodTableMap())
                    {
                        if (token == layout.ScoreProcessorToken) processor = methodTable;
                        if (layout.SupportedModTokens.TryGetValue(token, out string? acronym))
                            mods[methodTable] = acronym;
                    }
                }
            }

            if (!IsCurrent(process, generation)) return null;
            var result = new NativeLazerGameplayStatsMethodTables(generation, player, processor, mods);
            return result.IsCoherent ? result : null;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
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
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or
                                           System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
