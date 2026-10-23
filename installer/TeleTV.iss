; TeleTV installer (Inno Setup 6)
; Built automatically by GitHub Actions. To build by hand:
;   iscc /DMyAppVersion=1.0.0 /DPublishDir=..\publish installer\TeleTV.iss

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish"
#endif

#define MyAppName "TeleTV"
#define MyAppExe "TeleTV.exe"
#define MyAppPublisher "bhxuk"
#define MyAppURL "https://github.com/bhxuk/TeleTV"

[Setup]
AppId={{7B3C9E61-2A4F-4D7B-9C1E-5F0A8D2E6B34}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=TeleTV-Setup-{#MyAppVersion}
SetupIconFile=..\src\TeleTV\Assets\teletv.ico
UninstallDisplayIcon={app}\{#MyAppExe}
UninstallDisplayName={#MyAppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
CloseApplications=no
ChangesAssociations=yes
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Setup

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "apkfiles"; Description: "Open .apk files with TeleTV (double-click an APK to install it)"; GroupDescription: "Options:"
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Options:"

[Files]
Source: "{#PublishDir}\{#MyAppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\*.dll"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"
Name: "{autoprograms}\{#MyAppName}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Classes\.apk\OpenWithProgids"; ValueType: string; ValueName: "TeleTV.apk"; ValueData: ""; Flags: uninsdeletevalue; Tasks: apkfiles
Root: HKCU; Subkey: "Software\Classes\.apks\OpenWithProgids"; ValueType: string; ValueName: "TeleTV.apk"; ValueData: ""; Flags: uninsdeletevalue; Tasks: apkfiles
Root: HKCU; Subkey: "Software\Classes\.xapk\OpenWithProgids"; ValueType: string; ValueName: "TeleTV.apk"; ValueData: ""; Flags: uninsdeletevalue; Tasks: apkfiles
Root: HKCU; Subkey: "Software\Classes\TeleTV.apk"; ValueType: string; ValueName: ""; ValueData: "Android app"; Flags: uninsdeletekey; Tasks: apkfiles
Root: HKCU; Subkey: "Software\Classes\TeleTV.apk\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExe}"",0"; Tasks: apkfiles
Root: HKCU; Subkey: "Software\Classes\TeleTV.apk\shell\open"; ValueType: string; ValueName: ""; ValueData: "Install in TeleTV"; Tasks: apkfiles
Root: HKCU; Subkey: "Software\Classes\TeleTV.apk\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExe}"" ""%1"""; Tasks: apkfiles

[Run]
Filename: "{app}\{#MyAppExe}"; Description: "Open TeleTV now"; Flags: nowait postinstall skipifsilent
Filename: "{app}\{#MyAppExe}"; Parameters: "--restarting"; Flags: nowait; Check: WizardSilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/f /t /im {#MyAppExe}"; Flags: runhidden; RunOnceId: "StopTeleTV"

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  { Close a running copy so its files can be replaced. Updates from inside TeleTV close Android cleanly first. }
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/f /t /im {#MyAppExe}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(400);
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{localappdata}\TeleTV');
    if DirExists(DataDir) then
    begin
      if UninstallSilent or (MsgBox('Also remove the downloaded Android TV system and every app installed inside it?' + #13#10 + #13#10 +
         'Choose No to keep them, so reinstalling TeleTV later doesn''t need the big download again.',
         mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDNO) then
        Exit;
      DelTree(DataDir, True, True, True);
    end;
  end;
end;
