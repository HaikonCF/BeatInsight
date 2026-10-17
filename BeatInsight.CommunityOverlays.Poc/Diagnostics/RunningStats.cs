using System.Text.Json.Nodes;

namespace BeatInsight.CommunityOverlays.Poc.Diagnostics;

/// <summary>
/// Statistiques de durée (ms) à coût constant : moyenne, maximum et p95 sur les
/// 2 048 dernières valeurs. Utilisé pour le profilage du bridge (V3.14-COM-04).
/// </summary>
public sealed class RunningStats
{
    private const int Window = 2048;
    private readonly double[] _recent = new double[Window];
    private long _count;
    private double _sum;
    private double _max;

    public long Count => _count;

    public void Add(double value)
    {
        _recent[_count % Window] = value;
        _count++;
        _sum += value;
        if (value > _max) _max = value;
    }

    public JsonObject ToJson()
    {
        int n = (int)Math.Min(_count, Window);
        double p95 = 0;
        if (n > 0)
        {
            var sorted = _recent.Take(n).OrderBy(v => v).ToArray();
            p95 = sorted[Math.Min(n - 1, (int)Math.Ceiling(n * 0.95) - 1)];
        }

        return new JsonObject
        {
            ["count"] = _count,
            ["meanMs"] = _count == 0 ? 0 : Math.Round(_sum / _count, 3),
            ["p95Ms"] = Math.Round(p95, 3),
            ["maxMs"] = Math.Round(_max, 3),
        };
    }
}
