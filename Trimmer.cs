using System.Diagnostics;

namespace RamCleaner;

/// <summary>Estado/estatística de um app monitorado (atualizado a cada leitura/limpeza).</summary>
public class AppStats
{
    public int ProcessCount { get; set; }
    public long WorkingSetBytes { get; set; }
    public long PrivateBytes { get; set; }
    public long LastFreedBytes { get; set; }
    public long TotalFreedBytes { get; set; }
    public DateTime? LastTrim { get; set; }
    public int AccessDenied { get; set; }

    public AppStats Clone() => (AppStats)MemberwiseClone();
}

/// <summary>
/// Foto de todos os processos abertos, agrupados por nome.
/// Tirada uma vez por ciclo (em vez de uma vez por app) para ficar leve mesmo em intervalos de 100–500 ms.
/// </summary>
internal sealed class ProcessSnapshot : IDisposable
{
    private readonly Process[] _all;
    private readonly Dictionary<string, List<Process>> _byName = new(StringComparer.OrdinalIgnoreCase);

    public ProcessSnapshot()
    {
        try { _all = Process.GetProcesses(); }
        catch { _all = Array.Empty<Process>(); }

        int self = Environment.ProcessId;
        foreach (var p in _all)
        {
            try
            {
                if (p.Id == self || p.Id == 0 || p.Id == 4) continue;
                if (!_byName.TryGetValue(p.ProcessName, out var list))
                    _byName[p.ProcessName] = list = new List<Process>();
                list.Add(p);
            }
            catch { /* processo fechou */ }
        }
    }

    public IEnumerable<Process> Get(IEnumerable<string> names)
    {
        foreach (var n in names.Distinct(StringComparer.OrdinalIgnoreCase))
            if (_byName.TryGetValue(n, out var list))
                foreach (var p in list) yield return p;
    }

    public void Dispose()
    {
        foreach (var p in _all) p.Dispose();
    }
}

internal static class Trimmer
{
    /// <summary>
    /// Lê a RAM de todos os processos do grupo e, se <paramref name="trim"/> e acima do limite, limpa.
    /// </summary>
    public static void Update(TargetApp target, AppStats stats, ProcessSnapshot snap, bool trim)
    {
        var procs = snap.Get(target.ProcessNames).ToList();

        long ws = 0, priv = 0;
        foreach (var p in procs)
        {
            try { ws += p.WorkingSet64; priv += p.PrivateMemorySize64; } catch { }
        }

        stats.ProcessCount = procs.Count;
        stats.WorkingSetBytes = ws;
        stats.PrivateBytes = priv;

        long thresholdBytes = (long)target.ThresholdMB * 1024 * 1024;
        if (!trim || !target.Enabled || procs.Count == 0 || ws < thresholdBytes)
            return;

        int denied = 0;
        foreach (var p in procs)
        {
            try { if (!Native.TrimProcess(p.Id)) denied++; }
            catch { denied++; }
        }

        long after = 0;
        foreach (var p in procs)
        {
            try { p.Refresh(); after += p.WorkingSet64; } catch { }
        }

        long freed = Math.Max(0, ws - after);
        stats.LastFreedBytes = freed;
        stats.TotalFreedBytes += freed;
        stats.LastTrim = DateTime.Now;
        stats.AccessDenied = denied;
        stats.WorkingSetBytes = after;
    }

    public static string FormatMB(long bytes) => $"{bytes / 1024d / 1024d:N0} MB";

    public static string Normalize(string name)
    {
        name = name.Trim();
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }
}
