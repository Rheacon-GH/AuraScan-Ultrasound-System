; ============================================================
; AuraScan Ultrasound System — Inno Setup Installer Script
; Version 1.2.0
; © 2026 Rheacon Systems — All Rights Reserved
; ============================================================
;
; PREREQUISITES:
;   1. Publish both projects before compiling this script:
;
;      dotnet publish "AuraScan Ultrasound System.csproj" ^
;          -c Release -r win-x64 --self-contained -o publish\workstation
;
;      dotnet publish AuraScan.Server\AuraScan.Server.csproj ^
;          -c Release -r win-x64 --self-contained -o publish\server
;
;   2. Compile with Inno Setup 6+ (ISCC.exe):
;
;      iscc Installer\AuraScan-Setup.iss
;
; ============================================================

#define MyAppName      "AuraScan Ultrasound System"
#define MyAppVersion   "1.2.0"
#define MyAppPublisher "Rheacon Systems"
#define MyAppURL       "https://github.com/Rheacon-GH/AuraScan-Ultrasound-System"
#define MyAppExeName   "AuraScan Ultrasound System.exe"
#define MyServerExe    "AuraScan.Server.exe"

; Source directories (relative to this .iss file's parent — the repo root)
#define WkPublish      "..\publish\workstation"
#define SrvPublish     "..\publish\server"
#define DocsDir        "..\Docs"

[Setup]
AppId={{E7A3B5C1-9F42-4D8E-B6A7-2C1F3E5D9A0B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
DefaultDirName={autopf}\Rheacon\AuraScan
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
; Output installer to Installer\Output\
OutputDir=Output
OutputBaseFilename=AuraScan-Setup-{#MyAppVersion}
; Compression
Compression=lzma2/ultra64
SolidCompression=yes
; Appearance
SetupIconFile={#WkPublish}\AuraScan Ultrasound System.exe
WizardStyle=modern
; Privileges
PrivilegesRequired=admin
; Architecture
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Version info
VersionInfoVersion={#MyAppVersion}.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoCopyright=© 2026 {#MyAppPublisher}
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
; Misc
DisableProgramGroupPage=yes
LicenseFile=
UninstallDisplayIcon={app}\workstation\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Types]
Name: "full";    Description: "Full installation (Workstation + Server)"
Name: "client";  Description: "Workstation only"
Name: "custom";  Description: "Custom installation"; Flags: iscustom

[Components]
Name: "workstation"; Description: "AuraScan Workstation (WPF Application)"; Types: full client custom; Flags: fixed
Name: "server";      Description: "AuraScan Server (REST API + DICOM SCP)"; Types: full
Name: "docs";        Description: "Documentation (User Guide + Developer Guide)"; Types: full

[Files]
; Workstation files
Source: "{#WkPublish}\*"; DestDir: "{app}\workstation"; Components: workstation; Flags: ignoreversion recursesubdirs createallsubdirs

; Server files
Source: "{#SrvPublish}\*"; DestDir: "{app}\server"; Components: server; Flags: ignoreversion recursesubdirs createallsubdirs

; Documentation
Source: "{#DocsDir}\AuraScan-UserGuide.md";      DestDir: "{app}\docs"; Components: docs; Flags: ignoreversion
Source: "{#DocsDir}\AuraScan-DeveloperGuide.md";  DestDir: "{app}\docs"; Components: docs; Flags: ignoreversion

[Icons]
; Start Menu — Workstation
Name: "{group}\AuraScan Ultrasound System"; Filename: "{app}\workstation\{#MyAppExeName}"; Components: workstation
Name: "{group}\AuraScan Server";            Filename: "{app}\server\{#MyServerExe}";       Components: server
Name: "{group}\User Guide";                 Filename: "{app}\docs\AuraScan-UserGuide.md";  Components: docs
Name: "{group}\Uninstall AuraScan";         Filename: "{uninstallexe}"

; Desktop shortcut
Name: "{autodesktop}\AuraScan Ultrasound System"; Filename: "{app}\workstation\{#MyAppExeName}"; Components: workstation; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Components: workstation

[Run]
; Launch after install
Filename: "{app}\workstation\{#MyAppExeName}"; Description: "Launch AuraScan Ultrasound System"; Flags: nowait postinstall skipifsilent; Components: workstation

[UninstallDelete]
; Clean up runtime-generated files
Type: filesandordirs; Name: "{app}\server\AuraScan.db"
Type: filesandordirs; Name: "{app}\server\AuraScan.db-shm"
Type: filesandordirs; Name: "{app}\server\AuraScan.db-wal"
Type: filesandordirs; Name: "{app}\server\DicomStorage"

[Code]
// Display .NET 8 Desktop Runtime requirement if workstation files are missing runtime
function InitializeSetup(): Boolean;
begin
  Result := True;
  // The installer packages self-contained builds, so no external runtime is needed.
  // This hook is reserved for future pre-flight checks.
end;
