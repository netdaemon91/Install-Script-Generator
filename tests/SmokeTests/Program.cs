using System.Text;
using InstallScriptGenerator.Core;

string root = Path.Combine(Path.GetTempPath(), "isg-smoke-" + Guid.NewGuid().ToString("N"));
string nested = Path.Combine(root, "nested");
Directory.CreateDirectory(nested);

try
{
    for (int i = 0; i < 4000; i++)
        File.WriteAllText(Path.Combine(nested, $"Install_{i:00000}.exe"), "");
    File.WriteAllText(Path.Combine(root, "Package with spaces.msi"), "");
    File.WriteAllText(Path.Combine(root, "Registry.reg"), "");
    File.WriteAllText(Path.Combine(nested, "Script.ps1"), "");
    File.WriteAllText(Path.Combine(root, "Archive.zip"), "");
    File.WriteAllText(Path.Combine(root, "Ignore.txt"), "");

    var scanner = new ScanService();
    var selected = ScanService.ParseExtensions([".exe", "*.msi", ".ps1", "reg"]);
    Assert(selected.Count == 4, "Extensions were not normalized correctly.");

    var found = await scanner.ScanAsync(root, selected, true, null, CancellationToken.None);
    Assert(found.Count == 4003, $"Expected 4003 files, got {found.Count}.");
    Assert(found.Select(x => x.FullPath).Distinct(StringComparer.OrdinalIgnoreCase).Count()
           == found.Count, "Duplicate search results.");
    Assert(found.Zip(found.Skip(1)).All(p =>
           StringComparer.OrdinalIgnoreCase.Compare(p.First.RelativePath,
                                                    p.Second.RelativePath) <= 0),
           "Results are not sorted.");

    var shallow = await scanner.ScanAsync(root, selected, false, null, CancellationToken.None);
    Assert(shallow.Count == 2, "Non-recursive search included nested files.");

    string batOutput = Path.Combine(root, "output.bat");
    var batOptions = new ScriptOptions("Smoke Test", ExportFormat.Batch,
        UseAbsolutePaths: false, ShowProgressLines: true,
        ShowCompletionDialog: false, CompletionMessage: "");
    await ScriptGenerator.WriteAsync(batOutput, found, batOptions);
    string bat = await File.ReadAllTextAsync(batOutput);
    Assert(bat.StartsWith("@echo off"), "BAT script header is missing.");
    Assert(bat.Contains("msiexec.exe /i"), "MSI launcher is missing.");
    Assert(bat.Contains("reg.exe import"), "Registry import is missing.");
    Assert(bat.Contains("%~dp0"), "Relative output paths are missing.");
    Assert(bat.Contains("[4003/4003]"), "Last progress line is missing.");

    string psOutput = Path.Combine(root, "output.ps1");
    var psOptions = batOptions with { Format = ExportFormat.PowerShell };
    await ScriptGenerator.WriteAsync(psOutput, found, psOptions);
    byte[] psBytes = await File.ReadAllBytesAsync(psOutput);
    Assert(psBytes.AsSpan(0, 3).SequenceEqual([0xEF, 0xBB, 0xBF]),
        "PowerShell output must use UTF-8 BOM for Windows PowerShell 5.1.");
    string ps = Encoding.UTF8.GetString(psBytes);
    Assert(ps.Contains("Start-Process -FilePath 'msiexec.exe'"),
        "PowerShell MSI launcher is missing.");
    Assert(ps.Contains("Join-Path -Path $PSScriptRoot"),
        "PowerShell relative path handling is missing.");

    string preview = ScriptGenerator.Preview(found, batOptions, batOutput);
    Assert(preview.Contains("Preview truncated."), "Large preview was not capped.");
    Assert(preview.Length < 30000, "Preview grew unexpectedly large.");

    var custom = ScanService.ParseExtensions([".zip"]);
    var zipFiles = await scanner.ScanAsync(root, custom, false, null, CancellationToken.None);
    Assert(zipFiles.Count == 1, "Custom extension scan failed.");
    Assert(ScriptGenerator.Preview(zipFiles, batOptions, batOutput)
           .Contains("SKIPPED unsupported file type"),
           "Unknown custom file types must be skipped with a warning.");

    Console.WriteLine("Smoke tests passed: 4003 matches, recursive/non-recursive, BAT/PS1, custom types, capped preview.");
}
finally
{
    Directory.Delete(root, recursive: true);
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
