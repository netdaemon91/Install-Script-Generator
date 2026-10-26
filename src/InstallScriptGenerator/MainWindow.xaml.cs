using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using InstallScriptGenerator.Core;
using Microsoft.Win32;

namespace InstallScriptGenerator;

public partial class MainWindow : Window
{
    private readonly ScanService _scanner = new();
    private IReadOnlyList<InstallerFile> _files = Array.Empty<InstallerFile>();
    private CancellationTokenSource? _scanCancellation;
    private bool _dark = true;
    private bool _ready;

    private bool German => LanguageCombo.SelectedIndex != 1;
    private string T(string german, string english) => German ? german : english;

    public MainWindow()
    {
        InitializeComponent();
        LanguageCombo.SelectedIndex = 0;
        _ready = true;
        ApplyLanguage();
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
        TypesTitle.Text = T("Dateitypen", "File types");
        CustomCheckBox.Content = T("Eigene Erweiterungen", "Custom extensions");
        OptionsTitle.Text = T("Optionen", "Options");
        RecursiveCheckBox.Content = T("Unterordner durchsuchen", "Include subfolders");
        FullPathCheckBox.Content = T("Absolute Pfade verwenden", "Use absolute paths");
        ProgressCheckBox.Content = T("Fortschritt ausgeben", "Progress messages");
        CompletionCheckBox.Content = T("Abschlussdialog anzeigen", "Completion dialog");
        ResultsTitle.Text = T("Gefundene Dateien", "Discovered files");
        PreviewTitle.Text = T("Skriptvorschau (max. 60 Dateien)",
            "Script preview (first 60 files)");
        FormatLabel.Text = T("Exportformat", "Export as");
        ExportButton.Content = T("Skript exportieren", "Export script");
        FilesGrid.Columns[0].Header = T("Datei", "File");
        FilesGrid.Columns[1].Header = T("Typ", "Type");
        FilesGrid.Columns[2].Header = T("Relativer Pfad", "Relative path");
        if (_scanCancellation is null)
            StatusText.Text = _files.Count == 0 ? T("Bereit.", "Ready.")
                : T($"{_files.Count:N0} Dateien gefunden.", $"{_files.Count:N0} files found.");
        UpdatePreview();
    }

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        _dark = !_dark;
        ApplyTheme();
    }

    private void ApplyTheme()
    {
        var colors = _dark
            ? new[] { "#101820", "#1B2733", "#101C27", "#344658", "#EAF1F7", "#A8B8C7" }
            : new[] { "#F1F5F9", "#FFFFFF", "#FAFCFF", "#CDD8E3", "#172B3B", "#516575" };
        string[] names = ["WindowBrush", "PanelBrush", "FieldBrush",
                          "BorderBrush", "TextBrush", "MutedBrush"];
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
            _ => ExportFormat.Batch
        },
        UseAbsolutePaths: FullPathCheckBox.IsChecked == true,
        ShowProgressLines: ProgressCheckBox.IsChecked == true,
        ShowCompletionDialog: CompletionCheckBox.IsChecked == true,
        CompletionMessage: CompletionTextBox.Text);

    private string DefaultOutputPath()
    {
        string suffix = FormatCombo.SelectedIndex switch
        {
            1 => ".cmd", 2 => ".ps1", _ => ".bat"
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
        if (_ready) UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (!_ready || _files.Count == 0) return;
        try
        {
            PreviewTextBox.Text = ScriptGenerator.Preview(
                _files, SelectedOptions(), DefaultOutputPath());
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

        _files = Array.Empty<InstallerFile>();
        FilesGrid.ItemsSource = null;
        ExportButton.IsEnabled = false;
        PreviewTextBox.Clear();
        ScanButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        ScanProgressBar.IsIndeterminate = true;
        _scanCancellation = new CancellationTokenSource();
        var token = _scanCancellation.Token;
        var progress = new Progress<int>(count =>
            StatusText.Text = T($"{count:N0} Dateien geprüft...",
                $"{count:N0} files examined..."));

        try
        {
            _files = await _scanner.ScanAsync(
                FolderTextBox.Text, selected, RecursiveCheckBox.IsChecked == true,
                progress, token);
            FilesGrid.ItemsSource = _files;
            ExportButton.IsEnabled = _files.Count > 0;
            StatusText.Text = T($"{_files.Count:N0} Dateien gefunden.",
                $"{_files.Count:N0} files found.");
            PreviewTextBox.Text = _files.Count == 0
                ? T("Keine passenden Dateien gefunden.", "No matching files found.")
                : ScriptGenerator.Preview(_files, SelectedOptions(), DefaultOutputPath());
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
            _scanCancellation.Dispose();
            _scanCancellation = null;
            ScanButton.IsEnabled = true;
            CancelButton.IsEnabled = false;
            ScanProgressBar.IsIndeterminate = false;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) =>
        _scanCancellation?.Cancel();

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_files.Count == 0) return;
        var options = SelectedOptions();
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
            _ => ".bat"
        };
        var dialog = new SaveFileDialog
        {
            Title = T("Installationsskript speichern", "Save installation script"),
            Filter = "Batch (*.bat)|*.bat|Command (*.cmd)|*.cmd|PowerShell (*.ps1)|*.ps1",
            FilterIndex = options.Format switch
            {
                ExportFormat.Command => 2,
                ExportFormat.PowerShell => 3,
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
            ".bat" => ExportFormat.Batch,
            _ => options.Format
        };
        options = options with { Format = chosenFormat };
        ExportButton.IsEnabled = false;
        StatusText.Text = T("Skript wird geschrieben...", "Writing script...");
        try
        {
            var exportOptions = options;
            await Task.Run(() => ScriptGenerator.WriteAsync(dialog.FileName, _files, exportOptions));
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
            ExportButton.IsEnabled = _files.Count > 0;
        }
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        new AboutWindow(German, _dark) { Owner = this }.ShowDialog();
    }

    protected override void OnClosed(EventArgs e)
    {
        _scanCancellation?.Cancel();
        base.OnClosed(e);
    }
}
