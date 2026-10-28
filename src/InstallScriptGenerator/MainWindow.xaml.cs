using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using InstallScriptGenerator.Core;
using Microsoft.Win32;

namespace InstallScriptGenerator;

public partial class MainWindow : Window
{
    private readonly ScanService _scanner = new();
    private SerialStore? _serialStore;
    private string? _serialFile;
    private bool _serialDirty;
    private bool _loadingSerial;
    private IReadOnlyList<InstallerFile> _files = Array.Empty<InstallerFile>();
    private CancellationTokenSource? _scanCancellation;
    private bool _dark = true;
    private bool _ready;
    private bool _changingInclude;
    private TimeSpan _lastScanDuration = TimeSpan.Zero;

    private bool German => LanguageCombo.SelectedIndex != 1;
    private string T(string german, string english) => German ? german : english;

    public MainWindow()
    {
        InitializeComponent();
        LanguageCombo.SelectedIndex = 0;
        try { _serialStore = new SerialStore(); }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Serial data",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        _ready = true;
        ApplyLanguage();
        SelectSerial(null);
        ApplyTheme();
        PreviewTextBox.Text = T("Ordner auswählen und Suche starten.",
            "Choose a folder and start scanning.");
    }

    private void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_ready) ApplyLanguage();
    }

    private void ApplyLanguage()
    {
        SubtitleText.Text = T("Installationsskripte schnell und übersichtlich erstellen",
            "Fast directory scanning and installation script export");
        FolderLabel.Text = T("Quellordner", "Source folder");
        BrowseButton.Content = T("Durchsuchen...", "Browse...");
        ScanButton.Content = T("Suchen", "Scan");
        CancelButton.Content = T("Abbrechen", "Cancel");
        AboutButton.Content = T("Über", "About");
        ProjectLabel.Text = T("Projektname", "Project name");
        ScanSettingsTitle.Text = T("📁  Sucheinstellungen", "📁  Scan settings");
        ScanStepText.Text = T("Verzeichnissuche", "Directory scan");
        FilesStepText.Text = T("Gefundene Dateien", "Discovered files");
        ExportStepText.Text = T("Skript / EXE erstellen", "Script / EXE export");
        ResultsSearchBox.ToolTip = T("Nach Dateinamen oder Pfad filtern",
            "Filter by filename or path");
        TypesTitle.Text = T("Dateitypen", "File types");
        CustomCheckBox.Content = T("Eigene Erweiterungen", "Custom extensions");
        OptionsTitle.Text = T("Optionen", "Options");
        RecursiveCheckBox.Content = T("Unterordner durchsuchen", "Include subfolders");
        FullPathCheckBox.Content = T("Vollständige Originalpfade verwenden",
            "Use full original file paths");
        ApplyPathsHint();
        ProgressCheckBox.Content = T("Fortschritt ausgeben", "Progress messages");
        CompletionCheckBox.Content = T("Abschlussdialog anzeigen", "Completion dialog");
        ResultsTitle.Text = T($"📂  Gefundene Dateien ({_files.Count:N0})",
            $"📂  Found files ({_files.Count:N0})");
        PreviewTitle.Text = T("Skriptvorschau (max. 60 Dateien)",
            "Script preview (first 60 files)");
        FormatLabel.Text = T("Exportformat", "Export as");
        ExportButton.Content = T("Skript / EXE erstellen", "Generate script / EXE");
        FilesGrid.Columns[0].Header = T("Datei", "File");
        FilesGrid.Columns[1].Header = T("Typ", "Type");
        FilesGrid.Columns[2].Header = T("Relativer Pfad", "Relative path");
        SerialTitle.Text = T("Seriennummer / Produktschlüssel", "Serial / product key");
        UseSerialsCheckBox.Content = T("Schlüssel vor Installerstart kopieren",
            "Copy serials before launching installers");
        CopySerialButton.Content = T("Kopieren", "Copy");
        SaveSerialButton.Content = T("Speichern", "Save");
        RemoveSerialButton.Content = T("Entfernen", "Remove");
        StatsTitle.Text = T("◉  Statistiken", "◉  Statistics");
        StatFilesLabel.Text = T("Gefundene Dateien", "Files found");
        StatFoldersLabel.Text = T("Ordner", "Folders");
        StatSizeLabel.Text = T("Gesamtgröße", "Total size");
        StatTimeLabel.Text = T("Suchdauer", "Scan time");
        IncludeAllButton.Content = T("✓  Alle auswählen", "✓  Select all");
        IncludeNoneButton.Content = T("☐  Alle abwählen", "☐  Select none");
        OpenFolderButton.Content = T("Ordner öffnen", "Open folder");
        RefreshStatistics();
        RefreshSelectionSummary();
        if (_serialFile is null)
            SerialFileLabel.Text = T("Datei aus der Liste auswählen",
                "Select a file in the list");
        if (_scanCancellation is null)
            StatusText.Text = _files.Count == 0 ? T("Bereit.", "Ready.")
                : T($"{_files.Count:N0} Dateien gefunden.", $"{_files.Count:N0} files found.");
        UpdatePreview();
    }

    private void ApplyPathsHint()
    {
        PathsHintText.Text = FullPathCheckBox.IsChecked == true
            ? T("An: vollständige Originalpfade verwenden.",
                "On: use full original paths for all installers.")
            : T("Aus: Installer müssen direkt neben dem Skript liegen.",
                "Off: installers must be beside the generated script.");
    }

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        _dark = !_dark;
        ApplyTheme();
    }

    private void ApplyTheme()
    {
        var colors = _dark
            ? new[] { "#07151D", "#11242E", "#091B25", "#2A4552", "#F0F8FC",
                      "#A3B7C4", "#1A3340", "#244351", "#17634B", "#49D959", "#071B16",
                      "#F0F8FC", "#82EE78" }
            : new[] { "#EEF4F8", "#FFFFFF", "#F7FAFD", "#C5D3DC", "#152E3E",
                      "#526B78", "#E5EEF5", "#D7E8EC", "#C9EED6", "#218F38", "#FFFFFF",
                      "#152E3E", "#176F2D" };
        string[] names = ["WindowBrush", "PanelBrush", "FieldBrush",
                          "BorderBrush", "TextBrush", "MutedBrush", "PanelAltBrush",
                          "HoverBrush", "SelectionBrush", "AccentBrush", "ActionTextBrush",
                          "ComboTextBrush", "AccentHoverBrush"];
        for (int i = 0; i < names.Length; i++)
            Resources[names[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i])!);
        ThemeButton.Content = _dark ? "☀" : "☾";
        ThemeButton.ToolTip = _dark ? T("Hellen Modus aktivieren", "Switch to light mode")
                                    : T("Dunklen Modus aktivieren", "Switch to dark mode");
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = T("Quellordner auswählen", "Select source folder")
        };
        if (Directory.Exists(FolderTextBox.Text))
            dialog.InitialDirectory = FolderTextBox.Text;
        if (dialog.ShowDialog(this) == true) FolderTextBox.Text = dialog.FolderName;
    }

    private IReadOnlySet<string> SelectedExtensions()
    {
        var selection = new List<string>();
        if (ExeCheckBox.IsChecked == true) selection.Add(".exe");
        if (MsiCheckBox.IsChecked == true) selection.Add(".msi");
        if (BatCheckBox.IsChecked == true) selection.Add(".bat");
        if (CmdCheckBox.IsChecked == true) selection.Add(".cmd");
        if (Ps1CheckBox.IsChecked == true) selection.Add(".ps1");
        if (Au3CheckBox.IsChecked == true) selection.Add(".au3");
        if (RegCheckBox.IsChecked == true) selection.Add(".reg");
        if (VbsCheckBox.IsChecked == true) selection.Add(".vbs");
        if (CustomCheckBox.IsChecked == true) selection.Add(CustomTextBox.Text);
        return ScanService.ParseExtensions(selection);
    }

    private ScriptOptions SelectedOptions() => new(
        Title: ProjectTextBox.Text.Trim(),
        Format: FormatCombo.SelectedIndex switch
        {
            1 => ExportFormat.Command,
            2 => ExportFormat.PowerShell,
            3 => ExportFormat.LauncherExe,
            _ => ExportFormat.Batch
        },
        UseAbsolutePaths: FullPathCheckBox.IsChecked == true,
        ShowProgressLines: ProgressCheckBox.IsChecked == true,
        ShowCompletionDialog: CompletionCheckBox.IsChecked == true,
        CompletionMessage: CompletionTextBox.Text,
        IncludeSerials: UseSerialsCheckBox.IsChecked == true,
        Serials: _serialStore?.Snapshot());

    private string DefaultOutputPath()
    {
        string suffix = FormatCombo.SelectedIndex switch
        {
            1 => ".cmd", 2 => ".ps1", 3 => ".exe", _ => ".bat"
        };
        var folder = Directory.Exists(FolderTextBox.Text)
            ? FolderTextBox.Text : Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var title = ProjectTextBox.Text.Trim();
        if (title.Length == 0 || title.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            title = "Installer";
        return Path.Combine(folder, title + suffix);
    }

    private void Settings_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        if (sender == RecursiveCheckBox || sender == CustomTextBox ||
            sender == ExeCheckBox || sender == MsiCheckBox ||
            sender == BatCheckBox || sender == CmdCheckBox ||
            sender == Ps1CheckBox || sender == Au3CheckBox ||
            sender == RegCheckBox || sender == VbsCheckBox ||
            sender == CustomCheckBox)
        {
            InvalidateScan();
            return;
        }
        if (sender == FullPathCheckBox) ApplyPathsHint();
        UpdatePreview();
    }

    private void ScanSelection_Changed(object sender, TextChangedEventArgs e)
    {
        if (_ready) InvalidateScan();
    }

    private void ResultsSearch_Changed(object sender, TextChangedEventArgs e)
    {
        if (_ready) ApplyResultsSearch();
    }

    private void ApplyResultsSearch()
    {
        if (FilesGrid.ItemsSource is null) return;
        var query = ResultsSearchBox.Text.Trim();
        var view = CollectionViewSource.GetDefaultView(FilesGrid.ItemsSource);
        view.Filter = value => value is InstallerFile item &&
            (query.Length == 0 ||
             item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             item.RelativePath.Contains(query, StringComparison.OrdinalIgnoreCase));
        RefreshSelectionSummary();
    }

    private InstallerFile[] IncludedFiles() =>
        _files.Where(file => file.IsIncluded).ToArray();

    private void Included_Click(object sender, RoutedEventArgs e)
    {
        if (_ready && !_changingInclude) RefreshSelectionSummary();
    }

    private void IncludeAll_Click(object sender, RoutedEventArgs e) =>
        SetAllIncluded(true);

    private void IncludeNone_Click(object sender, RoutedEventArgs e) =>
        SetAllIncluded(false);

    private void SetAllIncluded(bool included)
    {
        _changingInclude = true;
        try
        {
            foreach (var file in _files) file.IsIncluded = included;
        }
        finally { _changingInclude = false; }
        RefreshSelectionSummary();
    }

    private void RefreshSelectionSummary()
    {
        if (!_ready) return;
        var count = _files.Count(file => file.IsIncluded);
        SelectedCountText.Text = T($"{count:N0} / {_files.Count:N0} ausgewählt",
            $"{count:N0} / {_files.Count:N0} selected");
        IncludeAllButton.IsEnabled = _files.Count > 0 && count < _files.Count;
        IncludeNoneButton.IsEnabled = count > 0;
        OpenFolderButton.IsEnabled = Directory.Exists(FolderTextBox.Text);
        ExportButton.IsEnabled = count > 0 && _scanCancellation is null;
        UpdatePreview();
    }

    private void RefreshStatistics()
    {
        if (!_ready) return;
        StatFilesText.Text = _files.Count.ToString("N0");
        StatFoldersText.Text = _files
            .Select(file => Path.GetDirectoryName(file.RelativePath) ?? "")
            .Distinct(StringComparer.OrdinalIgnoreCase).Count().ToString("N0");
        var bytes = _files.Sum(file => file.SizeBytes);
        StatSizeText.Text = bytes >= 1024L * 1024 * 1024
            ? (bytes / (1024d * 1024 * 1024)).ToString("0.##") + " GB"
            : bytes >= 1024 * 1024
                ? (bytes / (1024d * 1024)).ToString("0.#") + " MB"
                : bytes >= 1024
                    ? (bytes / 1024d).ToString("0.#") + " KB"
                    : bytes + " B";
        StatTimeText.Text = _lastScanDuration.ToString(@"hh\:mm\:ss");
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var chosenFile = FilesGrid.SelectedItem as InstallerFile;
        var folder = chosenFile is null
            ? FolderTextBox.Text : Path.GetDirectoryName(chosenFile.FullPath);
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;
        try
        {
            var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
            start.ArgumentList.Add(folder);
            Process.Start(start);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void InvalidateScan()
    {
        if (!SaveSerial()) return;
        _scanCancellation?.Cancel();
        _files = Array.Empty<InstallerFile>();
        _lastScanDuration = TimeSpan.Zero;
        FilesGrid.ItemsSource = null;
        PreviewTextBox.Text = T("Bitte Suche erneut starten.", "Run the scan again.");
        RefreshStatistics();
        RefreshSelectionSummary();
        StatusText.Text = T("Suchoptionen geändert.", "Scan options changed.");
    }

    private void UpdatePreview()
    {
        if (!_ready || _files.Count == 0) return;
        var included = IncludedFiles();
        if (included.Length == 0)
        {
            PreviewTextBox.Text = T("Keine Dateien für den Export ausgewählt.",
                "No files selected for export.");
            return;
        }
        try
        {
            PreviewTextBox.Text = ScriptGenerator.Preview(
                included, SelectedOptions(), DefaultOutputPath());
        }
        catch (Exception ex)
        {
            PreviewTextBox.Text = ex.Message;
        }
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        if (_scanCancellation is not null) return;
        if (!Directory.Exists(FolderTextBox.Text))
        {
            MessageBox.Show(this, T("Bitte einen gültigen Ordner auswählen.",
                "Please select an existing folder."), Title,
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var selected = SelectedExtensions();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, T("Bitte mindestens einen Dateityp auswählen.",
                "Select at least one file type."), Title,
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!SaveSerial()) return;
        _files = Array.Empty<InstallerFile>();
        _lastScanDuration = TimeSpan.Zero;
        FilesGrid.ItemsSource = null;
        PreviewTextBox.Clear();
        RefreshStatistics();
        RefreshSelectionSummary();
        ScanButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        ScanProgressBar.IsIndeterminate = true;
        _scanCancellation = new CancellationTokenSource();
        var token = _scanCancellation.Token;
        var stopwatch = Stopwatch.StartNew();
        var progress = new Progress<int>(count =>
            StatusText.Text = T($"{count:N0} Dateien geprüft...",
                $"{count:N0} files examined..."));

        try
        {
            _files = await _scanner.ScanAsync(
                FolderTextBox.Text, selected, RecursiveCheckBox.IsChecked == true,
                progress, token);
            stopwatch.Stop();
            _lastScanDuration = stopwatch.Elapsed;
            FilesGrid.ItemsSource = _files;
            ApplyResultsSearch();
            ResultsTitle.Text = T($"📂  Gefundene Dateien ({_files.Count:N0})",
                $"📂  Found files ({_files.Count:N0})");
            RefreshStatistics();
            RefreshSelectionSummary();
            StatusText.Text = T($"{_files.Count:N0} Dateien gefunden.",
                $"{_files.Count:N0} files found.");
            if (_files.Count == 0)
                PreviewTextBox.Text = T("Keine passenden Dateien gefunden.",
                    "No matching files found.");
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = T("Suche abgebrochen.", "Scan cancelled.");
        }
        catch (Exception ex)
        {
            StatusText.Text = T("Suche fehlgeschlagen.", "Scan failed.");
            MessageBox.Show(this, ex.Message, Title,
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            stopwatch.Stop();
            _scanCancellation.Dispose();
            _scanCancellation = null;
            ScanButton.IsEnabled = true;
            CancelButton.IsEnabled = false;
            ScanProgressBar.IsIndeterminate = false;
            RefreshSelectionSummary();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) =>
        _scanCancellation?.Cancel();

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var includedFiles = IncludedFiles();
        if (includedFiles.Length == 0) return;
        if (!SaveSerial()) return;
        var options = SelectedOptions();
        if (options.IncludeSerials && includedFiles.Any(item =>
            options.Serials?.TryGetValue(item.FullPath, out var serial) == true &&
            !string.IsNullOrWhiteSpace(serial)))
        {
            if (MessageBox.Show(this,
                T("Schlüssel sind im exportierten Skript wiederherstellbar gespeichert " +
                  "und werden beim Start in die Zwischenablage kopiert. Trotzdem exportieren?",
                  "Serials are recoverable from exported scripts and copied to the " +
                  "clipboard at runtime. Export anyway?"),
                Title, MessageBoxButton.YesNo, MessageBoxImage.Warning)
                != MessageBoxResult.Yes) return;
        }
        if (options.Title.Length == 0)
        {
            MessageBox.Show(this, T("Bitte einen Projektnamen eingeben.",
                "Please enter a project name."), Title);
            return;
        }

        string ext = options.Format switch
        {
            ExportFormat.Command => ".cmd",
            ExportFormat.PowerShell => ".ps1",
            ExportFormat.LauncherExe => ".exe",
            _ => ".bat"
        };
        var dialog = new SaveFileDialog
        {
            Title = T("Installationsskript speichern", "Save installation script"),
            Filter = "Batch (*.bat)|*.bat|Command (*.cmd)|*.cmd|PowerShell (*.ps1)|*.ps1|EXE launcher (*.exe)|*.exe",
            FilterIndex = options.Format switch
            {
                ExportFormat.Command => 2,
                ExportFormat.PowerShell => 3,
                ExportFormat.LauncherExe => 4,
                _ => 1
            },
            DefaultExt = ext,
            AddExtension = true,
            FileName = options.Title + ext,
            InitialDirectory = Directory.Exists(FolderTextBox.Text)
                ? FolderTextBox.Text : Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true) return;

        // Never replace one of the discovered source files with the generated script.
        if (_files.Any(f => string.Equals(
            Path.GetFullPath(f.FullPath), Path.GetFullPath(dialog.FileName),
            StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, T("Die Zieldatei gehört zu den gefundenen Quelldateien. " +
                "Bitte einen anderen Dateinamen oder Ordner wählen.",
                "The output is one of the discovered input files. Choose another destination."),
                Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // SaveFileDialog permits changing the selected type; honor the real extension.
        var chosenFormat = Path.GetExtension(dialog.FileName).ToLowerInvariant() switch
        {
            ".cmd" => ExportFormat.Command,
            ".ps1" => ExportFormat.PowerShell,
            ".exe" => ExportFormat.LauncherExe,
            ".bat" => ExportFormat.Batch,
            _ => options.Format
        };
        options = options with { Format = chosenFormat };

        // Portable mode requires every installer beside the exported script/EXE.
        if (!options.UseAbsolutePaths)
        {
            var collisions = includedFiles.GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group => group.Count() > 1);
            if (collisions is not null)
            {
                MessageBox.Show(this,
                    T("Mehrere Installationsdateien haben denselben Namen: " + collisions.Key +
                      ". Im Modus ohne vollständige Pfade wäre keine eindeutige Zuordnung möglich. " +
                      "Bitte Dateien umbenennen oder vollständige Pfade aktivieren.",
                      "Several installers have the same name: " + collisions.Key +
                      ". Portable mode cannot distinguish them. Rename the files or " +
                      "enable absolute paths."),
                    Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(dialog.FileName));
            if (includedFiles.Any(file => !string.Equals(
                Path.GetDirectoryName(Path.GetFullPath(file.FullPath)), outputDirectory,
                StringComparison.OrdinalIgnoreCase)))
            {
                var answer = MessageBox.Show(this,
                    T("Ohne vollständige Pfade erwartet das erzeugte Skript bzw. die EXE " +
                      "sämtliche Installationsdateien direkt im selben Ordner. " +
                      "Unterordner werden nicht berücksichtigt und Dateien werden nicht " +
                      "automatisch kopiert. Trotzdem fortfahren?",
                      "Without absolute paths, the generated script or EXE expects every " +
                      "installer in the same folder as itself. Subfolders are ignored " +
                      "and files are not copied automatically. Continue?"),
                    Title, MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (answer != MessageBoxResult.Yes) return;
            }
        }

        PreviewTextBox.Text = ScriptGenerator.Preview(includedFiles, options, dialog.FileName);
        ExportButton.IsEnabled = false;
        StatusText.Text = options.Format == ExportFormat.LauncherExe
            ? T("EXE-Launcher wird erstellt...", "Creating standalone EXE launcher...")
            : T("Skript wird geschrieben...", "Writing script...");
        try
        {
            var exportOptions = options;
            if (options.Format == ExportFormat.LauncherExe)
            {
                var template = Path.Combine(AppContext.BaseDirectory, "LauncherTemplate.exe");
                await ExeExporter.WriteAsync(template, dialog.FileName, includedFiles, exportOptions);
            }
            else
                await ScriptGenerator.WriteAsync(dialog.FileName, includedFiles, exportOptions);
            StatusText.Text = T("Skript erfolgreich exportiert.", "Script exported successfully.");
            MessageBox.Show(this, T("Skript gespeichert unter:", "Script saved to:") +
                Environment.NewLine + dialog.FileName, Title,
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText.Text = T("Export fehlgeschlagen.", "Export failed.");
            MessageBox.Show(this, ex.Message, Title,
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            RefreshSelectionSummary();
        }
    }

    private void FilesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        if (!SaveSerial()) return;
        SelectSerial((FilesGrid.SelectedItem as InstallerFile)?.FullPath);
    }

    private void SelectSerial(string? path)
    {
        _serialFile = path;
        _serialDirty = false;
        _loadingSerial = true;
        try
        {
            bool active = path is not null && _serialStore is not null;
            SerialTextBox.IsEnabled = active;
            CopySerialButton.IsEnabled = active;
            SaveSerialButton.IsEnabled = active;
            RemoveSerialButton.IsEnabled = active;
            SerialTextBox.Text = active ? _serialStore!.Get(path!) : "";
            SerialFileLabel.Text = path is null
                ? T("Datei aus der Liste auswählen", "Select a file in the list")
                : Path.GetFileName(path);
            SerialStatusText.Text = active
                ? (SerialTextBox.Text.Length == 0 ? T("Kein Schlüssel", "No key")
                                                    : T("Gespeichert", "Saved"))
                : "";
        }
        finally { _loadingSerial = false; }
    }

    private void Serial_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loadingSerial || !_ready) return;
        _serialDirty = true;
        SerialStatusText.Text = T("Ungespeichert", "Unsaved");
    }

    private bool SaveSerial()
    {
        if (!_serialDirty || _serialFile is null || _serialStore is null) return true;
        try
        {
            _serialStore.Set(_serialFile, SerialTextBox.Text);
            _serialDirty = false;
            SerialStatusText.Text = T("Gespeichert", "Saved");
            UpdatePreview();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, T("Schlüssel konnte nicht gespeichert werden",
                "Failed to save serial"), MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void SaveSerial_Click(object sender, RoutedEventArgs e) => SaveSerial();

    private void RemoveSerial_Click(object sender, RoutedEventArgs e)
    {
        if (_serialFile is null) return;
        SerialTextBox.Text = "";
        _serialDirty = true;
        SaveSerial();
    }

    private void CopySerial_Click(object sender, RoutedEventArgs e)
    {
        if (SerialTextBox.Text.Length == 0) return;
        try
        {
            Clipboard.SetText(SerialTextBox.Text);
            SerialStatusText.Text = T("In Zwischenablage kopiert", "Copied to clipboard");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Title,
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        new AboutWindow(German, _dark) { Owner = this }.ShowDialog();
    }

    protected override void OnClosed(EventArgs e)
    {
        SaveSerial();
        _scanCancellation?.Cancel();
        base.OnClosed(e);
    }
}
