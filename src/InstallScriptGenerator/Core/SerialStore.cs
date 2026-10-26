using System.IO;
using System.Text.Json;

namespace InstallScriptGenerator.Core;

/// <summary>Stores per-installer serials in plain local JSON, by absolute file path.</summary>
public sealed class SerialStore
{
    private readonly Dictionary<string, string> _values =
        new(StringComparer.OrdinalIgnoreCase);

    public string FilePath { get; }

    public SerialStore(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "InstallScriptGenerator", "serials.json");
        if (!File.Exists(FilePath)) return;
        var values = JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(FilePath)) ?? new();
        foreach (var entry in values)
            _values[Path.GetFullPath(entry.Key)] = entry.Value;
    }

    public string Get(string path) =>
        _values.TryGetValue(Path.GetFullPath(path), out var key) ? key : "";

    public IReadOnlyDictionary<string, string> Snapshot() =>
        new Dictionary<string, string>(_values, StringComparer.OrdinalIgnoreCase);

    public void Set(string path, string value)
    {
        path = Path.GetFullPath(path);
        bool hadOld = _values.TryGetValue(path, out var previous);
        if (string.IsNullOrWhiteSpace(value)) _values.Remove(path);
        else _values[path] = value;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_values,
                new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch
        {
            if (hadOld) _values[path] = previous!;
            else _values.Remove(path);
            throw;
        }
    }
}
