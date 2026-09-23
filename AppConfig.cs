using System.Text.Json;
using System.Text.Json.Serialization;

namespace RamCleaner;

/// <summary>
/// Um "app" monitorado: um nome que você escolhe (ex.: "Edge") com um ou mais
/// processos dentro (ex.: msedge + msedgewebview2).
/// </summary>
public class TargetApp
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Nome que aparece na tabela. Livre, você escolhe.</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>Nomes dos processos sem ".exe" (ex.: "msedge", "msedgewebview2").</summary>
    public List<string> ProcessNames { get; set; } = new();

    public bool Enabled { get; set; } = true;

    /// <summary>Só limpa quando a soma da RAM de todos os processos do grupo passar disso. 0 = sempre.</summary>
    public int ThresholdMB { get; set; } = 0;

    /// <summary>Campo da versão antiga (1 processo por linha). Só lido para migrar.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProcessName { get; set; }

    [JsonIgnore]
    public string Label => string.IsNullOrWhiteSpace(DisplayName)
        ? string.Join(", ", ProcessNames)
        : DisplayName;
}

public class AppConfig
{
    public const int MinIntervalMs = 100;
    public const int MaxIntervalMs = 3_600_000;

    public int IntervalMs { get; set; } = 30_000;

    /// <summary>Campo da versão antiga (em segundos). Só lido para migrar.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? IntervalSeconds { get; set; }

    public bool StartWithWindows { get; set; } = false;
    public bool StartMinimized { get; set; } = true;
    public bool Paused { get; set; } = false;
    public List<TargetApp> Targets { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static string ConfigDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RamCleaner");

    public static string ConfigPath => Path.Combine(ConfigDir, "config.json");

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath));
                if (cfg != null)
                {
                    cfg.Migrate();
                    return cfg;
                }
            }
        }
        catch
        {
            // Config corrompida: começa do zero.
        }
        return new AppConfig();
    }

    private void Migrate()
    {
        if (IntervalSeconds is int sec)
        {
            IntervalMs = sec * 1000;
            IntervalSeconds = null;
        }
        IntervalMs = Math.Clamp(IntervalMs, MinIntervalMs, MaxIntervalMs);

        foreach (var t in Targets)
        {
            if (string.IsNullOrWhiteSpace(t.Id)) t.Id = Guid.NewGuid().ToString("N");
            if (t.ProcessNames.Count == 0 && !string.IsNullOrWhiteSpace(t.ProcessName))
                t.ProcessNames.Add(t.ProcessName);
            if (string.IsNullOrWhiteSpace(t.DisplayName) && !string.IsNullOrWhiteSpace(t.ProcessName))
                t.DisplayName = t.ProcessName;
            t.ProcessName = null;
        }
        Targets.RemoveAll(t => t.ProcessNames.Count == 0);
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, JsonOpts));
        }
        catch
        {
            // Ignora falhas de gravação (disco cheio, permissão etc.).
        }
    }

    public static string FormatInterval(int ms) =>
        ms < 1000 ? $"{ms} ms" :
        ms % 1000 == 0 ? $"{ms / 1000} s" :
        $"{ms / 1000d:0.#} s";
}
