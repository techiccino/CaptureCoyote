# CaptureCoyote V1 Release Checklist

Use this checklist before calling a build `v1`.

## Build and Packaging

- Confirm `Directory.Build.props` version matches the intended release number.
- Run:

```powershell
dotnet build CaptureCoyote.sln
powershell -ExecutionPolicy Bypass -File .\installer\Build-Installer.ps1 -Configuration Release -RuntimeIdentifier win-x64
```

- Verify the publish output exists in `artifacts\publish\win-x64`.
- Verify the installer exists in `artifacts\installer`.

## Fresh Machine Install

- Install the app from the generated installer.
- Confirm the app icon appears correctly in:
  - Start menu
  - taskbar
  - desktop shortcut, if created
- Confirm uninstall works from Windows Apps/Programs.

## File Association

- Double-click a `.coyote` file from Explorer.
- Confirm CaptureCoyote opens directly to that project.
- Right-click a `.coyote` file and confirm the icon/association looks correct.

## Capture Flow

- Region capture works from launcher.
- Window capture locks onto the intended window and does not flick to full screen.
- Full-screen capture works.
- Delayed capture shows a visible countdown and the app hides cleanly before capture.
- Global hotkeys work after launch.

## Editor Flow

- Save Editable creates a `.coyote` file that reopens with annotations intact.
- Save As and Quick Save export flat images correctly.
- Copy to clipboard works.
- Duplicate selects the duplicate so it can be moved immediately.
- Pin to Screen opens and updates the floating reference window.
- OCR Text opens in its dedicated window and copies cleanly.

## Recovery and Search

- Unsaved editor changes produce a recovery draft.
- Recovery draft restores correctly from the launcher.
- Search Snips shows new saved projects without restarting the app.
- Search filters for type, mode, date, and sort behave correctly.

## Resilience

- Corrupt or rename `settings.json` and confirm the app falls back to defaults instead of crashing.
- Try opening an invalid or damaged `.coyote` file and confirm the user gets an error message.
- Check that logs are written under:

```text
%LocalAppData%\CaptureCoyote\Logs
```

## Optional Final Mile

- Code-sign the installer and executable.
- Test SmartScreen behavior on a clean Windows machine.
- Write short release notes covering:
  - editable projects
  - OCR/search
  - tray workflow
  - recovery drafts
