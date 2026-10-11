#ifndef AppVersion
  #define AppVersion "2.6.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\portable"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif

[Setup]
AppId={{18B40A8E-5D1D-44E4-BB1E-C8951FD05A31}
AppName=FoxyVPN
AppVersion={#AppVersion}
AppPublisher=FoxyVPN contributors
DefaultDirName={autopf}\FoxyVPN
DefaultGroupName=FoxyVPN
OutputDir={#OutputDir}
OutputBaseFilename=FoxyVPN-Setup-{#AppVersion}-x64
SetupIconFile=..\FoxyVPN.Windows\Assets\foxy_app_icon.ico
UninstallDisplayIcon={app}\FoxyVPN.exe
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
WizardStyle=modern
Compression=lzma2
SolidCompression=yes

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\FoxyVPN"; Filename: "{app}\FoxyVPN.exe"
Name: "{autodesktop}\FoxyVPN"; Filename: "{app}\FoxyVPN.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"

[Run]
Filename: "{app}\FoxyVPN.exe"; Description: "Launch FoxyVPN"; Flags: nowait postinstall skipifsilent
