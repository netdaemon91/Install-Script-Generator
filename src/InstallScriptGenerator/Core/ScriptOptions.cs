namespace InstallScriptGenerator.Core;

public enum ExportFormat
{
    Batch,
    Command,
    PowerShell
}

public sealed record ScriptOptions(
    string Title,
    ExportFormat Format,
    bool UseAbsolutePaths,
    bool ShowProgressLines,
    bool ShowCompletionDialog,
    string CompletionMessage,
    bool IncludeSerials = false,
    IReadOnlyDictionary<string, string>? Serials = null);
