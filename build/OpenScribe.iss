; OpenScribe Inno Setup Script
; Produces a single .exe installer with silent install support for SCCM/GPO deployment.
;
; Expected preprocessor defines (passed by Build-OpenScribe.ps1):
;   AppVersion     - e.g. "1.0.0"
;   AppArchitecture - e.g. "x64compatible" or "arm64"
;   PublishDir     - path to dotnet publish output
;   OutputDir      - path to write the installer .exe
;   InstallerName  - output filename without extension

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef AppArchitecture
  #define AppArchitecture "x64compatible"
#endif
#ifndef PublishDir
  #define PublishDir "publish\win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "artifacts"
#endif
#ifndef InstallerName
  #define InstallerName "OpenScribe-Setup"
#endif

[Setup]
AppId={{B8F3A2E1-7C4D-4E5F-9A1B-2D3E4F5A6B7C}
AppName=OpenScribe
AppVersion={#AppVersion}
AppVerName=OpenScribe {#AppVersion}
AppPublisher=OpenScribe
AppPublisherURL=https://github.com/openscribe
DefaultDirName={autopf}\OpenScribe
DefaultGroupName=OpenScribe
AllowNoIcons=yes
OutputDir={#OutputDir}
OutputBaseFilename={#InstallerName}
SetupIconFile=..\src\OpenScribe.App\Assets\OpenScribe.ico
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesAllowed={#AppArchitecture}
ArchitecturesInstallIn64BitMode={#AppArchitecture}
MinVersion=10.0.18362
WizardStyle=modern
DisableProgramGroupPage=yes
UninstallDisplayName=OpenScribe
UninstallDisplayIcon={app}\OpenScribe.App.exe

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\OpenScribe"; Filename: "{app}\OpenScribe.App.exe"; WorkingDir: "{app}"
Name: "{group}\{cm:UninstallProgram,OpenScribe}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\OpenScribe"; Filename: "{app}\OpenScribe.App.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\OpenScribe.App.exe"; Description: "{cm:LaunchProgram,OpenScribe}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[Code]
function InitializeSetup: Boolean;
begin
  Result := True;
end;
