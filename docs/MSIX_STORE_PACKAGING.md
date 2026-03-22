# CaptureCoyote MSIX Store Packaging

CaptureCoyote now includes a dedicated Windows Application Packaging Project for Microsoft Store submission:

- `CaptureCoyote.Package/CaptureCoyote.Package.wapproj`

This packaging track is separate from the unpackaged EXE installer flow so the normal `dotnet build` workflow stays clean.

## Why use this path

The Microsoft Store rejected the unpackaged EXE submission because the installer and PE files were unsigned. MSIX is the cleaner Store-native path because it avoids the external EXE hosting/signing friction and fits a free Store release better.

## Important Store note

If you switch from the unpackaged Win32 submission to an MSIX packaged submission and want to keep the same app name in Partner Center, review Microsoft's guidance about reusing the existing app name. If needed, remove the old unpackaged submission path before publishing the packaged one.

## First-time setup in Visual Studio

1. Open `CaptureCoyote.sln` in Visual Studio.
2. Right-click the solution and choose `Add > Existing Project...`.
3. Add `CaptureCoyote.Package/CaptureCoyote.Package.wapproj`.
4. Right-click the package project and choose `Set as Startup Project`.
5. Right-click the package project and choose `Associate App with the Store...`.
6. Sign in with the same Partner Center account that reserved `CaptureCoyote`.
7. Choose the reserved app so Visual Studio updates the package identity and publisher fields in `Package.appxmanifest`.

## Build the Store package

1. In Visual Studio, switch to `Release` and `x64`.
2. Right-click `CaptureCoyote.Package`.
3. Choose `Publish > Create App Packages...`.
4. Select `Microsoft Store`.
5. Follow the wizard to generate the package output.

The output should include an `.msixupload` or equivalent Store-ready package that you can submit in Partner Center.

## Current packaged behaviors

- Startup-on-Windows now uses a packaged startup task when CaptureCoyote has package identity.
- `.coyote` file association is declared in the package manifest for the packaged app path.

## Files to review

- `CaptureCoyote.Package/Package.appxmanifest`
- `CaptureCoyote.Infrastructure/Services/StartupLaunchService.cs`
- `CaptureCoyote.App/App.xaml.cs`
