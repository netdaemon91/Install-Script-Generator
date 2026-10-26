# Install Script Generator

[![Build Windows x64](https://github.com/netdaemon91/Install-Script-Generator/actions/workflows/build.yml/badge.svg)](https://github.com/netdaemon91/Install-Script-Generator/actions/workflows/build.yml)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)
![Windows](https://img.shields.io/badge/platform-Windows%20x64-0078D6)
![MIT](https://img.shields.io/badge/license-MIT-green)

A Windows utility that scans folders and generates installation scripts. Originally created with AutoPlay Media Studio, now rebuilt in **C# / .NET 10 / WPF**.

> **Status:** 2.0.0-alpha.3 — first C# rebuild. The EXE launcher exporter is available; embedding the installers themselves is planned.

## Features

- Discover EXE, MSI, BAT, CMD, PS1, REG, VBS and AU3 files, and optionally custom extensions.
- Recursive scanning, cancellation, inaccessible-folder handling, sorted results and virtualized file table.
- Memory-friendly preview limited to 60 entries; full script output is streamed to disk without re-reading it after each command.
- Export BAT, CMD or PowerShell (.ps1), with path selection and optional progress output or completion popup.
- Dedicated handlers for MSI, REG, VBS and AU3 (AutoIt must be installed for AU3 files).
- German and English interface, Dark/Light switch, About dialog, website and repository links.
- GitHub Actions Windows x64 build, including a self-contained distributable application.

## Get started

Open the [Actions page](https://github.com/netdaemon91/Install-Script-Generator/actions/workflows/build.yml), select a successful **Build Windows x64** run and download the Windows artifact, or build from source:

```powershell
dotnet build .\src\InstallScriptGenerator\InstallScriptGenerator.csproj -c Release
dotnet publish .\src\InstallScriptGenerator\InstallScriptGenerator.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Requires **Windows 10/11** to run. Building locally requires the **.NET 10 SDK** and Windows desktop targeting support.

## Safety and limitations

- **Inspect all generated commands before running them.** This program does not install software automatically when scanning or exporting.
- EXE files may require application-specific command-line parameters; this alpha does not guess silent-install switches.
- Unknown custom file types are included in search results but exported as skipped/warning entries.
- PS1 files are subject to the target machine's PowerShell execution policy.
- Exported scripts do not embed software packages; keep the referenced installer files in their expected relative location or select absolute paths.
- Some uncommon command-interpreter metacharacters in file paths may require manual verification. Test exported scripts on sample data before relying on them.

## Roadmap

- [x] Migrate original v1.3 scanner and BAT/CMD/PS1 generator to C#.
- [x] Responsive scanning, preview, bilingual UI and theme toggle.
- [ ] Per-installer arguments, manual ordering, presets and stronger diagnostics.
- [x] Portable launcher EXE export (embeds the generated script; installer files remain external).
- [ ] Optional all-in-one EXE bundle with embedded installers.

## Credits

**Original Idea & Code by:** NetDaemon   
**License:** MIT (see [LICENSE](LICENSE)).

The original Install Script Generator v1.3 was written in AutoPlay Media Studio as a learning project. This rewrite requires no AMS runtime.

## Serial / product key management

Click a discovered file to edit its product key. Choose **Save**, **Remove**, or **Copy** to the Windows clipboard. Per-file serials are also saved automatically when changing the selected file or exporting. They are stored as unencrypted, locally readable JSON in `%LOCALAPPDATA%\\InstallScriptGenerator\\serials.json`, by the source file's absolute path.

To reproduce the original AMS v1.3 behavior, enable **Copy serials before launching installers**. The exported script copies the associated key to the clipboard immediately before invoking each installer. PowerShell scripts contain the key as readable text; BAT/CMD uses reversible Base64 encoding for proper command-line escaping. This is **not encryption**: anyone with the exported script can recover the serials. The preview hides key contents, and a warning appears before export. Do not publish generated scripts containing real product keys.

## EXE launcher export (alpha.3)

Select **EXE launcher (.exe)** under export format. The generator embeds the PowerShell installation instructions directly in a standalone Windows x64 executable. The EXE requires no additional .NET runtime or Visual Studio installation on the destination machine. Each installer referenced by the launcher must still be available in its original relative location, or must be reachable by absolute path when that option is selected. This is **not yet an all-in-one installer bundle**.

The launcher executes the embedded installation script using Windows PowerShell. License keys are recoverable from the exported EXE, just as they are from exported scripts. Only run installers from sources you trust.

To build the launcher template locally before building the WPF application:

```powershell
dotnet publish tools/InstallerLauncher/InstallerLauncher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true -o src/InstallScriptGenerator/Assets
dotnet publish src/InstallScriptGenerator/InstallScriptGenerator.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

The GitHub Actions workflow creates and packages the template automatically.
