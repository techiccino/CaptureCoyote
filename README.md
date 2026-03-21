# CaptureCoyote

CaptureCoyote is a Windows desktop screenshot app built in WPF on .NET 8. The product is designed around a simple promise:

`Capture -> Explain -> Save or Share`

Unlike flat-image snipping tools, CaptureCoyote treats screenshots as editable documents. A capture can be reopened later, with arrows, text, highlights, blur regions, and crop state still intact inside a native `.coyote` project file.

## MVP Features

- Region capture with a dark multi-monitor overlay
- Window capture with hover highlight
- Full-screen capture
- Delayed capture with `0s`, `3s`, and `5s`
- Global hotkeys with persisted bindings
- Editable `.coyote` project format
- Reopenable layered screenshots
- Annotation editor with:
  - Select, move, resize
  - Arrow, rectangle, ellipse, line
  - Text
  - Numbered steps
  - Highlight
  - Blur or pixelate region
  - Crop
- Undo and redo
- Copy to clipboard
- Quick Save, Save As, Save Editable
- Review window for fast post-capture actions
- Local settings persistence
- Windows OCR integration with extracted text stored in `.coyote` metadata
- Copy detected text from the launcher, review window, and editor
- Searchable snip browser with type, mode, date, and sort filters
- Searchable recent workspace list powered by project names, window titles, and OCR text
- Recent capture history with reopenable cached capture documents
- Recovery drafts for unsaved editor work
- Close-time unsaved changes prompt with recovery fallback
- Pin-to-screen always-on-top reference window
- Zoom controls plus endpoint editing for lines and arrows
- Style preset profiles for bug reports, documentation, and tutorials
- Tool-aware annotation defaults for text, steps, highlight, and pixelate
- First-run setup window for startup and editor defaults
- `.coyote` project opening from Windows shell launch arguments

## Architecture

The solution is split into focused projects:

```text
CaptureCoyote.App
  WPF shell, launcher, capture overlay, review window, settings window,
  tray integration, recent search, and recovery entry points

CaptureCoyote.Core
  Domain models, enums, primitives, MVVM base infrastructure

CaptureCoyote.Services
  Service interfaces and app-facing abstractions

CaptureCoyote.Infrastructure
  Windows-specific implementations for capture, hotkeys, serialization,
  clipboard, export, dialogs, annotation rendering, OCR, recent workspace
  caching, recovery drafts, and settings persistence

CaptureCoyote.Editor
  Editor window, editor view model, annotation surface control, converters
```

## Editable Project Format

`.coyote` files are ZIP-based project documents containing:

- `project.json` with capture metadata, crop state, extracted text, and layered annotations
- `original.png` with the original captured bitmap

This keeps editing non-destructive until export.

## Key Models

- `CaptureSession`
- `CaptureResult`
- `ScreenshotProject`
- `AnnotationObject` plus concrete annotation types
- `CropState`
- `ExportOptions`
- `AppSettings`

## Key Services

- `IScreenCaptureService`
- `IHotkeyService`
- `IClipboardService`
- `IFileExportService`
- `IProjectSerializationService`
- `ISettingsService`
- `IOcrService`
- `IAnnotationRenderService`
- `IWindowLocatorService`
- `IRecentWorkspaceService`
- `IRecoveryDraftService`

## Build and Run

Requirements:

- Windows 10 or 11
- .NET SDK 9 installed locally is fine; the app targets `net8.0-windows10.0.19041.0`

Build:

```powershell
dotnet build CaptureCoyote.sln
```

Run:

```powershell
dotnet run --project .\CaptureCoyote.App\CaptureCoyote.App.csproj
```

## Branding Assets

Optional shell branding lives in `CaptureCoyote.App/Assets`:

- `CaptureCoyoteLogo.png` for the launcher, settings, review, and editor headers
- `CaptureCoyote.ico` for the window icon, taskbar icon, and packaged executable icon

If those files are present during build, the app picks them up automatically.

## Installer and File Association

CaptureCoyote includes installer scaffolding in `installer/`:

- `installer/Build-Installer.ps1` publishes the app and, if Inno Setup 6 is installed, builds a Windows installer.
- `installer/CaptureCoyote.iss` registers `.coyote` as an editable CaptureCoyote project document.

Typical installer build flow:

```powershell
powershell -ExecutionPolicy Bypass -File .\installer\Build-Installer.ps1 -Configuration Release -RuntimeIdentifier win-x64
```

After installation, double-clicking a `.coyote` file should launch CaptureCoyote and open that project directly.

## Diagnostics

CaptureCoyote writes local troubleshooting logs here:

```text
%LocalAppData%\CaptureCoyote\Logs
```

The Settings window also includes an `Open Logs Folder` action for quicker support and troubleshooting.

## Distribution and Support

Recommended release path for CaptureCoyote right now:

- publish the app free first to maximize adoption
- distribute through the Microsoft Store for trust and discovery
- optionally mirror direct downloads through GitHub Releases
- add a subtle support / tip link instead of licensing complexity in v1

Recommended support platforms:

- `GitHub Sponsors` if you plan to distribute builds or source updates through GitHub
- `Ko-fi` if you want a simpler creator-style tip jar for non-technical users

The app includes a lightweight support-link scaffold:

- edit [CaptureCoyote.App/Services/AppLinks.cs](CaptureCoyote.App/Services/AppLinks.cs)
- set `SupportUrl` to your public tip page
- optionally set `DownloadUrl` for direct distribution later

Once `SupportUrl` is configured, CaptureCoyote will show a subtle `Support CaptureCoyote` entry in:

- Settings
- the launcher footer

## Notes

- The app is Per Monitor V2 DPI aware through the application manifest.
- Hotkey configuration in the MVP keeps the modifier chord fixed to `Ctrl + Shift` and lets you customize the trigger key.
- OCR uses the Windows OCR APIs when available and safely falls back to empty extracted text if the local machine cannot service the request.
- Recent workspace items and recovery drafts are cached in local app data so the launcher can restore work quickly without external services.
- On first launch, CaptureCoyote opens a lightweight welcome/setup window so startup behavior and editor defaults can be chosen once instead of buried in settings.
- If the settings file is corrupted or unreadable, CaptureCoyote falls back to safe defaults and logs the failure locally instead of crashing during startup.

## Release Checklist

For a v1 shipping pass, use [docs/V1_RELEASE_CHECKLIST.md](docs/V1_RELEASE_CHECKLIST.md).

## Future Roadmap

- Saved searches, tags, and smarter project-library grouping
- Scrolling capture
- OCR language selection and better extracted-text review tools
- Richer text editing and typography controls
- Redaction presets and privacy workflows
- AI-assisted issue summaries and annotation suggestions
