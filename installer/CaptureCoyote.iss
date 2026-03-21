#ifndef AppVersion
  #define AppVersion "1.2.3"
#endif

#ifndef SourceDir
  #define SourceDir "..\\artifacts\\publish\\win-x64"
#endif

[Setup]
AppId={{A10614CC-D991-4E95-9505-87E470AE0359}
AppName=CaptureCoyote
AppVersion={#AppVersion}
AppPublisher=CaptureCoyote
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\CaptureCoyote
DefaultGroupName=CaptureCoyote
OutputDir=..\artifacts\installer
OutputBaseFilename=CaptureCoyote-Setup-{#AppVersion}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\CaptureCoyote.exe
ChangesAssociations=yes

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked
Name: "launchatstartup"; Description: "Launch CaptureCoyote when Windows starts"; GroupDescription: "Startup behavior:"; Flags: checkedonce

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\CaptureCoyote"; Filename: "{app}\CaptureCoyote.exe"
Name: "{autodesktop}\CaptureCoyote"; Filename: "{app}\CaptureCoyote.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Classes\.coyote"; ValueType: string; ValueName: ""; ValueData: "CaptureCoyote.Project"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\CaptureCoyote.Project"; ValueType: string; ValueName: ""; ValueData: "CaptureCoyote Editable Screenshot"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\CaptureCoyote.Project\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\CaptureCoyote.exe,0"
Root: HKCU; Subkey: "Software\Classes\CaptureCoyote.Project\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\CaptureCoyote.exe"" ""%1"""
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "CaptureCoyote"; ValueData: """{app}\CaptureCoyote.exe"" --startup"; Tasks: launchatstartup; Flags: uninsdeletevalue
