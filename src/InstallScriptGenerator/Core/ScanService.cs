using System.IO;

namespace InstallScriptGenerator.Core;

public sealed record InstallerFile(string FullPath, string RelativePath)
{
    public string Name => Path.GetFileName(FullPath);
    public string Extension => Path.GetExtension(FullPath).ToUpperInvariant();
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
                    results.Add(new InstallerFile(file, Path.GetRelativePath(root, file)));
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
