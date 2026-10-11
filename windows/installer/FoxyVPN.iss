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


[Code]
function HasDotNet8DesktopRuntimeInRegistryView(
  const RootKey: HKEY; const SubkeyName: String): Boolean;
var
  Names: TArrayOfString;
  I: Integer;
begin
  Result := False;
  if not RegGetValueNames(RootKey, SubkeyName, Names) then
    Exit;

  for I := 0 to GetArrayLength(Names) - 1 do
  begin
    if Copy(Names[I], 1, 2) = '8.' then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

function IsDotNet8DesktopRuntimeInstalled(): Boolean;
const
  RuntimeKey = 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App';
begin
  { Installer can run with different registry views depending on Windows and
    how .NET was installed. Check both views before asking to download anything. }
  Result := HasDotNet8DesktopRuntimeInRegistryView(HKEY_LOCAL_MACHINE_64, RuntimeKey);
  if not Result then
    Result := HasDotNet8DesktopRuntimeInRegistryView(HKEY_LOCAL_MACHINE_32, RuntimeKey);
  if not Result then
    Result := HasDotNet8DesktopRuntimeInRegistryView(
      HKEY_LOCAL_MACHINE_64,
      'SOFTWARE\WOW6432Node\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App');
end;

function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
  RuntimePath: String;
begin
  { Prerequisites are optional during installation: never block FoxyVPN setup
    solely because the runtime is missing, cannot be downloaded, or fails to install. }
  Result := True;
  if IsDotNet8DesktopRuntimeInstalled() then
    Exit;

  if MsgBox(
    'The Microsoft .NET 8 Desktop Runtime was not detected. You can install it now to help FoxyVPN run, or choose No to skip this prerequisite and continue installing FoxyVPN. Skipping is allowed, but FoxyVPN may not start if a compatible runtime is not already available.',
    mbConfirmation, MB_YESNO) <> IDYES then
  begin
    Exit;
  end;

  try
    DownloadTemporaryFile(
      'https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe',
      'dotnet-windowsdesktop-runtime.exe', '', nil);
  except
    MsgBox(
      'The .NET Desktop Runtime could not be downloaded. FoxyVPN installation will continue without it. You can install the x64 .NET 8 Desktop Runtime later from https://dotnet.microsoft.com/download/dotnet/8.0.',
      mbInformation, MB_OK);
    Exit;
  end;

  RuntimePath := ExpandConstant('{tmp}\dotnet-windowsdesktop-runtime.exe');
  if not Exec(RuntimePath, '/install /quiet /norestart', '',
      SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    MsgBox(
      'The .NET Desktop Runtime installer could not be started. FoxyVPN installation will continue without it. You can install the runtime later from https://dotnet.microsoft.com/download/dotnet/8.0.',
      mbInformation, MB_OK);
    Exit;
  end;

  if (ResultCode <> 0) and (ResultCode <> 3010) and (ResultCode <> 1641) then
  begin
    MsgBox(
      'The .NET Desktop Runtime setup did not complete successfully. FoxyVPN installation will continue anyway. If FoxyVPN does not start, install the x64 .NET 8 Desktop Runtime from https://dotnet.microsoft.com/download/dotnet/8.0.',
      mbInformation, MB_OK);
    Exit;
  end;

  if not IsDotNet8DesktopRuntimeInstalled() then
    MsgBox(
      'The .NET 8 Desktop Runtime was not detected after setup. FoxyVPN installation will continue anyway. If FoxyVPN does not start, install the x64 .NET 8 Desktop Runtime from https://dotnet.microsoft.com/download/dotnet/8.0.',
      mbInformation, MB_OK);
end;
