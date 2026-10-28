using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace InstallScriptGenerator.Core;

/// <summary>A scan result with a user-controlled export selection.</summary>
public sealed class InstallerFile : INotifyPropertyChanged
{
    private bool _isIncluded = true;

    public InstallerFile(string fullPath, string relativePath, long sizeBytes = 0)
    {
        FullPath = fullPath;
        RelativePath = relativePath;
        SizeBytes = sizeBytes;
    }

    public string FullPath { get; }
    public string RelativePath { get; }
    public string Name => Path.GetFileName(FullPath);
    public string Extension => Path.GetExtension(FullPath).ToUpperInvariant();
    public long SizeBytes { get; }

    public string SizeLabel => SizeBytes switch
    {
        < 1024 => SizeBytes + " B",
        < 1024 * 1024 => (SizeBytes / 1024d).ToString("0.#") + " KB",
        < 1024L * 1024 * 1024 => (SizeBytes / (1024d * 1024)).ToString("0.#") + " MB",
        _ => (SizeBytes / (1024d * 1024 * 1024)).ToString("0.##") + " GB"
    };

    public bool IsIncluded
    {
        get => _isIncluded;
        set
        {
            if (_isIncluded == value) return;
            _isIncluded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsIncluded)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class ScanService
{
    public Task<IReadOnlyList<InstallerFile>> ScanAsync(
        string sourceDirectory,
        IReadOnlySet<string> extensions,
        bool recursive,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        return Task.Run<IReadOnlyList<InstallerFile>>(() =>
        {
            var results = new List<InstallerFile>();
            var root = Path.GetFullPath(sourceDirectory);
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = recursive,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
                ReturnSpecialDirectories = false
            };

            int examined = 0;
            foreach (var file in Directory.EnumerateFiles(root, "*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                examined++;
                if (extensions.Contains("*") ||
                    extensions.Any(ext => file.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                {
                    long size = 0;
                    try { size = new FileInfo(file).Length; }
                    catch (IOException) { /* File disappeared during scan. */ }
                    catch (UnauthorizedAccessException) { /* Size may be unavailable. */ }
                    results.Add(new InstallerFile(file, Path.GetRelativePath(root, file), size));
                }

                if ((examined & 511) == 0) progress?.Report(examined);
            }

            results.Sort((a, b) =>
                StringComparer.OrdinalIgnoreCase.Compare(a.RelativePath, b.RelativePath));
            progress?.Report(examined);
            return results;
        }, cancellationToken);
    }

    public static IReadOnlySet<string> ParseExtensions(IEnumerable<string> extensions)
    {
        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in extensions.SelectMany(x =>
                     x.Split([',', ';', ' ', '\r', '\n', '\t'],
                         StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)))
        {
            if (token is "*" or "*.*")
            {
                normalized.Add("*");
                continue;
            }

            var ext = token.StartsWith("*.") ? token[1..] :
                      token.StartsWith('.') ? token : "." + token;
            if (ext.Length > 1 &&
                ext[1..].All(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_'))
            {
                normalized.Add(ext);
            }
        }
        return normalized;
    }
}
