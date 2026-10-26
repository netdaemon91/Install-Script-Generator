using System.IO;
using System.Text;

namespace InstallScriptGenerator.Core;

/// <summary>Streams output instead of repeatedly reading the entire script back into memory.</summary>
public static class ScriptGenerator
{
    private const int MaxPreviewItems = 60;

    public static string Preview(
        IReadOnlyList<InstallerFile> files, ScriptOptions options, string outputPath)
    {
        var preview = new StringBuilder();
        foreach (var line in GenerateLines(files, options, outputPath, MaxPreviewItems, preview: true))
            preview.AppendLine(line);
        return preview.ToString();
    }

    public static async Task WriteAsync(
        string outputPath, IReadOnlyList<InstallerFile> files, ScriptOptions options,
        CancellationToken cancellationToken = default)
    {
        // Windows PowerShell 5.1 requires a BOM for reliable non-ASCII script decoding.
        var encoding = new UTF8Encoding(options.Format == ExportFormat.PowerShell);
        await using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write,
            FileShare.None, bufferSize: 65536, useAsync: true);
        await using var writer = new StreamWriter(stream, encoding);
        foreach (var line in GenerateLines(files, options, outputPath, int.MaxValue, preview: false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(line.AsMemory(), cancellationToken);
        }
        await writer.FlushAsync(cancellationToken);
    }

    private static IEnumerable<string> GenerateLines(
        IReadOnlyList<InstallerFile> files, ScriptOptions options,
        string outputPath, int limit, bool preview)
    {
        bool ps = options.Format == ExportFormat.PowerShell;
        if (ps)
        {
            yield return "$ErrorActionPreference = 'Stop'";
            yield return "$Host.UI.RawUI.WindowTitle = " + PsQuote(options.Title);
        }
        else
        {
            yield return "@echo off";
            yield return "setlocal DisableDelayedExpansion";
            yield return "chcp 65001 >nul";
            yield return "title " + CmdEcho(options.Title);
        }
        yield return "";

        var outputFolder = Path.GetDirectoryName(Path.GetFullPath(outputPath))!;
        for (int index = 0; index < Math.Min(files.Count, limit); index++)
        {
            var file = files[index];
            var suffix = Path.GetExtension(file.FullPath).ToLowerInvariant();
            var target = options.UseAbsolutePaths
                ? Path.GetFullPath(file.FullPath)
                : Path.GetRelativePath(outputFolder, file.FullPath);

            if (options.ShowProgressLines)
            {
                var text = $"[{index + 1}/{files.Count}] {file.Name}";
                yield return ps ? "Write-Host " + PsQuote(text) : "echo " + CmdEcho(text);
            }

            if (options.IncludeSerials &&
                options.Serials?.TryGetValue(file.FullPath, out var serial) == true &&
                !string.IsNullOrWhiteSpace(serial))
            {
                // Original AMS behavior: copy each product key before launching its installer.
                // EncodedCommand uses reversible Base64 for CMD escaping; it is not encryption.
                if (preview)
                    yield return ps ? "# Serial configured (hidden in preview)"
                                    : "rem Serial configured (hidden in preview)";
                else if (ps)
                {
                    yield return "Write-Host " + PsQuote("Copying serial for " + file.Name);
                    yield return "Set-Clipboard -Value " + PsQuote(serial);
                }
                else
                {
                    var command = "Set-Clipboard -Value " + PsQuote(serial);
                    var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
                    yield return "echo " + CmdEcho("Copying serial for " + file.Name);
                    yield return "powershell.exe -NoProfile -NonInteractive -EncodedCommand " + encoded;
                    yield return "if errorlevel 1 echo [WARNING] Failed to copy serial.";
                }
            }

            if (ps)
            {
                yield return options.UseAbsolutePaths
                    ? "$target = " + PsQuote(target)
                    : "$target = Join-Path -Path $PSScriptRoot -ChildPath " + PsQuote(target);
                foreach (var command in PowerShellCommands(suffix, file.Name))
                    yield return command;
            }
            else
            {
                var escaped = target.Replace("%", "%%", StringComparison.Ordinal);
                var quoted = options.UseAbsolutePaths ? "\"" + escaped + "\""
                    : "\"%~dp0" + escaped + "\"";
                yield return BatchCommand(suffix, quoted, file.Name);
                if (IsExecutable(suffix))
                    yield return "if errorlevel 1 echo [WARNING] Installer returned a non-zero exit code.";
            }
            yield return "";
        }

        if (files.Count > limit)
        {
            var remaining = files.Count - limit;
            yield return ps
                ? $"# Preview truncated. {remaining} additional entries will be exported."
                : $"rem Preview truncated. {remaining} additional entries will be exported.";
        }
        else if (options.ShowCompletionDialog)
        {
            var message = options.CompletionMessage;
            if (ps)
            {
                yield return "Add-Type -AssemblyName System.Windows.Forms";
                yield return "[System.Windows.Forms.MessageBox]::Show(" +
                    PsQuote(message) + ", " + PsQuote(options.Title) +
                    ", 'OK', 'Information') | Out-Null";
            }
            else
            {
                var command = "Add-Type -AssemblyName System.Windows.Forms; " +
                    "[System.Windows.Forms.MessageBox]::Show(" + PsQuote(message) + ", " +
                    PsQuote(options.Title) + ") | Out-Null";
                var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
                yield return "powershell.exe -NoProfile -EncodedCommand " + encoded;
            }
        }

        if (ps) yield return "# End of generated script";
        else yield return "endlocal";
    }

    private static bool IsExecutable(string ext) =>
        ext is ".exe" or ".msi" or ".bat" or ".cmd" or ".ps1" or ".vbs" or ".reg" or ".au3";

    private static string BatchCommand(string ext, string target, string name) => ext switch
    {
        ".exe" => "start \"\" /wait " + target,
        ".msi" => "start \"\" /wait msiexec.exe /i " + target + " /norestart",
        ".bat" or ".cmd" => "call " + target,
        ".ps1" => "powershell.exe -NoProfile -File " + target,
        ".reg" => "reg.exe import " + target,
        ".vbs" => "cscript.exe //nologo " + target,
        ".au3" => "AutoIt3.exe " + target,
        _ => "rem SKIPPED unsupported file type: " + CmdEcho(name)
    };

    private static IEnumerable<string> PowerShellCommands(string ext, string name)
    {
        if (ext is ".exe" or ".msi")
        {
            yield return ext == ".exe"
                ? "$process = Start-Process -FilePath $target -Wait -PassThru"
                : "$process = Start-Process -FilePath 'msiexec.exe' -ArgumentList ('/i \"' + $target + '\" /norestart') -Wait -PassThru";
            yield return "if ($process.ExitCode -ne 0 -and $process.ExitCode -ne 3010) { " +
                "Write-Warning ('Installer returned exit code ' + $process.ExitCode) }";
        }
        else if (ext is ".bat" or ".cmd" or ".ps1" or ".reg" or ".vbs" or ".au3")
        {
            yield return "$global:LASTEXITCODE = 0";
            yield return ext switch
            {
                ".reg" => "& reg.exe import $target",
                ".vbs" => "& cscript.exe //nologo $target",
                ".au3" => "& AutoIt3.exe $target",
                _ => "& $target"
            };
            yield return "if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne 3010) { " +
                "Write-Warning ('Installer returned exit code ' + $LASTEXITCODE) }";
        }
        else
        {
            yield return "Write-Warning " + PsQuote("Skipped unsupported file type: " + name);
        }
    }

    private static string PsQuote(string value) => "'" + value.Replace("'", "''") + "'";

    private static string CmdEcho(string value)
    {
        var safe = value.Replace("\r", " ").Replace("\n", " ");
        foreach (var ch in new[] { '^', '&', '|', '<', '>', '(', ')' })
            safe = safe.Replace(ch.ToString(), "^" + ch);
        return safe.Replace("%", "%%", StringComparison.Ordinal);
    }
}
