using System.Text;
using InstallScriptGenerator.Core;

string root = Path.Combine(Path.GetTempPath(), "isg-smoke-" + Guid.NewGuid().ToString("N"));
string nested = Path.Combine(root, "nested");
Directory.CreateDirectory(nested);

try
{
    for (int i = 0; i < 4000; i++)
        File.WriteAllText(Path.Combine(nested, $"Install_{i:00000}.exe"), "");
    File.WriteAllText(Path.Combine(root, "Package with spaces.msi"), "testing");
    File.WriteAllText(Path.Combine(root, "Registry.reg"), "");
    File.WriteAllText(Path.Combine(nested, "Script.ps1"), "");
    File.WriteAllText(Path.Combine(root, "Archive.zip"), "");
    File.WriteAllText(Path.Combine(root, "Ignore.txt"), "");

    var scanner = new ScanService();
    var selected = ScanService.ParseExtensions([".exe", "*.msi", ".ps1", "reg"]);
    Assert(selected.Count == 4, "Extensions were not normalized correctly.");

    var found = await scanner.ScanAsync(root, selected, true, null, CancellationToken.None);
    Assert(found.Count == 4003, $"Expected 4003 files, got {found.Count}.");
    Assert(found.All(item => item.IsIncluded), "Scan results must be included by default.");
    var msi = found.Single(item => item.Extension == ".MSI");
    Assert(msi.SizeBytes == 7 && msi.SizeLabel == "7 B",
        "Dashboard file size metadata should be accurate.");
    found[0].IsIncluded = false;
    Assert(found.Count(item => item.IsIncluded) == 4002,
        "Per-file include selection must be writable.");
    found[0].IsIncluded = true;
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
    Assert(bat.Contains("%~dp0"), "Portable output path anchor missing.");
    Assert(bat.Contains("\"%~dp0Install_00000.exe\""),
        "Portable mode must reference files directly next to the batch script.");
    Assert(!bat.Contains("%~dp0nested"), "Portable mode must not keep discovered folder names.");
    Assert(bat.Contains("[4003/4003]"), "Last progress line is missing.");

    string absOutput = Path.Combine(root, "output-absolute.bat");
    await ScriptGenerator.WriteAsync(absOutput, found, batOptions with { UseAbsolutePaths = true });
    string abs = await File.ReadAllTextAsync(absOutput);
    Assert(abs.Contains(Path.Combine(root, "nested", "Install_00000.exe")),
        "Absolute mode must preserve the full original file path.");
    Assert(!abs.Contains("\"%~dp0Install_00000.exe\""),
        "Absolute mode must not use the script-relative prefix.");

    var selection = found.Take(2).ToArray();
    selection[0].IsIncluded = false;
    var selectedOnly = selection.Where(item => item.IsIncluded).ToArray();
    string selectedOutput = Path.Combine(root, "only-selected.bat");
    await ScriptGenerator.WriteAsync(selectedOutput, selectedOnly, batOptions);
    var selectedScript = await File.ReadAllTextAsync(selectedOutput);
    Assert(selectedScript.Contains("[1/1]") &&
           !selectedScript.Contains(selection[0].Name),
           "Selected-only export must omit excluded installers.");
    selection[0].IsIncluded = true;

    string psOutput = Path.Combine(root, "output.ps1");
    var psOptions = batOptions with { Format = ExportFormat.PowerShell };
    await ScriptGenerator.WriteAsync(psOutput, found, psOptions);
    byte[] psBytes = await File.ReadAllBytesAsync(psOutput);
    Assert(psBytes.Length >= 3 && psBytes[0] == 0xEF && psBytes[1] == 0xBB && psBytes[2] == 0xBF,
        "PowerShell output must use UTF-8 BOM for Windows PowerShell 5.1.");
    string ps = Encoding.UTF8.GetString(psBytes);
    Assert(ps.Contains("Start-Process -FilePath 'msiexec.exe'"),
        "PowerShell MSI launcher is missing.");
    Assert(ps.Contains("Join-Path -Path $PSScriptRoot"),
        "PowerShell script-folder anchoring is missing.");
    Assert(ps.Contains("-ChildPath 'Install_00000.exe'"),
        "PowerShell portable mode should emit only the filename.");
    Assert(!ps.Contains("-ChildPath 'nested\\Install_00000.exe'"),
        "PowerShell portable mode should not retain scanned subfolders.");

    string preview = ScriptGenerator.Preview(found, batOptions, batOutput);
    Assert(preview.Contains("Preview truncated."), "Large preview was not capped.");
    Assert(preview.Length < 30000, "Preview grew unexpectedly large.");

    const string serial = "ABCD-%!&'ÄÖÜ";
    var licensedFile = found.First(file => file.Extension == ".EXE");
    var serialOptions = batOptions with {
        IncludeSerials = true,
        Serials = new Dictionary<string, string> { [licensedFile.FullPath] = serial }
    };
    var serialPreview = ScriptGenerator.Preview(found, serialOptions, batOutput);
    Assert(serialPreview.Contains("Serial configured"), "Serial preview label missing.");
    Assert(!serialPreview.Contains(serial), "Serial leaked into preview.");
    await ScriptGenerator.WriteAsync(batOutput, found, serialOptions);
    bat = await File.ReadAllTextAsync(batOutput);
    var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(
        "Set-Clipboard -Value 'ABCD-%!&''ÄÖÜ'"));
    Assert(bat.Contains(encoded), "Serial clipboard command missing in BAT.");
    var psWithSerial = psOptions with { IncludeSerials = true, Serials = serialOptions.Serials };
    await ScriptGenerator.WriteAsync(psOutput, found, psWithSerial);
    ps = await File.ReadAllTextAsync(psOutput);
    Assert(ps.Contains("Set-Clipboard -Value 'ABCD-%!&''ÄÖÜ'"),
        "Serial clipboard command missing in PowerShell.");

    string serialFile = Path.Combine(root, "serials.json");
    var serialStore = new SerialStore(serialFile);
    serialStore.Set(licensedFile.FullPath, serial);
    Assert(new SerialStore(serialFile).Get(licensedFile.FullPath) == serial,
        "Local serial store roundtrip failed.");
    serialStore.Set(licensedFile.FullPath, "");
    Assert(new SerialStore(serialFile).Get(licensedFile.FullPath) == "",
        "Local serial deletion failed.");

    string? launcherTemplate = Environment.GetEnvironmentVariable("ISG_LAUNCHER_TEMPLATE");
    if (!string.IsNullOrEmpty(launcherTemplate))
    {
        Assert(File.Exists(launcherTemplate), "Launcher executable was not published.");
        string marker = Path.Combine(root, "launcher-ok.txt");
        string installerScript = Path.Combine(root, "MakeMarker.ps1");
        File.WriteAllText(installerScript,
            "Set-Content -LiteralPath (Join-Path $env:ISG_LAUNCH_DIR 'launcher-ok.txt') -Value 'OK'");
        string exeDestination = Path.Combine(root, "my-launcher.exe");
        await ExeExporter.WriteAsync(launcherTemplate, exeDestination,
            [new InstallerFile(installerScript, "MakeMarker.ps1")],
            psOptions with { Format = ExportFormat.LauncherExe, IncludeSerials = false });
        Assert(File.Exists(exeDestination), "EXE exporter did not write an executable.");
        using var process = System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo(exeDestination)
            {
                UseShellExecute = false,
                WorkingDirectory = root
            }) ?? throw new InvalidOperationException("Could not start generated EXE.");
        await process.WaitForExitAsync();
        Assert(process.ExitCode == 0, "Generated EXE returned non-zero exit code.");
        Assert(File.Exists(marker) && File.ReadAllText(marker).Contains("OK"),
            "Generated EXE did not execute the embedded script.");
    }

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
