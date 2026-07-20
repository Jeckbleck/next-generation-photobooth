#ifndef MyAppVersion
  #define MyAppVersion "0.0.0-dev"
#endif
#ifndef PublishDir
  #define PublishDir "..\.publish-temp"
#endif

#define MyAppId "{{8F3A1C9E-4B7D-4A2F-9E61-3C8D5F2A71B4}"
#define MyAppName "Photobooth Kiosk"
#define MyAppExeName "Photobooth.exe"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName=C:\Program Files\Photobooth
DefaultGroupName=Photobooth Kiosk
DisableProgramGroupPage=yes
PrivilegesRequired=admin
CloseApplications=yes
RestartApplications=no
OutputDir=..\releases
OutputBaseFilename=PhotoboothSetup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"

[Tasks]
Name: "autostart"; Description: "Launch Photobooth automatically when Windows starts"; GroupDescription: "Startup:"

[Registry]
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Photobooth Kiosk"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: autostart
