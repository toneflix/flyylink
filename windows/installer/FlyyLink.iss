; Builds FlyyLink-Setup-<version>.exe from the published app in windows\publish\FlyyLink.
; iscc /DAppVersion=1.2.3 installer\FlyyLink.iss
; Installs for the current user only, so it needs no administrator rights and can update itself silently.

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif

#define AppName "FlyyLink"
#define AppExe "FlyyLink.exe"
#define Publish "..\publish\FlyyLink"

[Setup]
AppId={{9D7ACFE2-75B7-41CB-9AF5-1E17246CFB7E}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Toneflix
AppPublisherURL=https://flyylink.toneflix.net
AppUpdatesURL=https://flyylink.toneflix.net/#download
VersionInfoVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\{#AppName}
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\publish
OutputBaseFilename=FlyyLink-Setup-{#AppVersion}
SetupIconFile=..\src\FlyyLink\Assets\FlyyLink.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
CloseApplications=force
RestartApplications=no

[Tasks]
Name: "startup"; Description: "Start FlyyLink when I sign in to Windows"

[Files]
Source: "{#Publish}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
; A fresh copy of the app each time, so files a newer version dropped don't linger.
Type: filesandordirs; Name: "{app}\*"

[Icons]
Name: "{userprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "Your Android phone on this PC"

[Run]
; Also runs after a silent update, which starts FlyyLink again.
Filename: "{app}\{#AppExe}"; Description: "Open FlyyLink"; Flags: nowait postinstall

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/im {#AppExe} /f"; Flags: runhidden; RunOnceId: "StopFlyyLink"
Filename: "{sys}\taskkill.exe"; Parameters: "/im FlyyLink.BrowserHost.exe /f"; Flags: runhidden; RunOnceId: "StopBrowserHost"

[Registry]
; FlyyLink writes these itself on launch. Listed here so uninstalling removes them.
Root: HKCU; Subkey: "Software\Classes\*\shell\FlyyLink.Send"; Flags: uninsdeletekey dontcreatekey
Root: HKCU; Subkey: "Software\Classes\FlyyLink.tel"; Flags: uninsdeletekey dontcreatekey
Root: HKCU; Subkey: "Software\FlyyLink"; Flags: uninsdeletekey dontcreatekey
Root: HKCU; Subkey: "Software\Google\Chrome\NativeMessagingHosts\com.toneflix.flyylink"; Flags: uninsdeletekey dontcreatekey
Root: HKCU; Subkey: "Software\Chromium\NativeMessagingHosts\com.toneflix.flyylink"; Flags: uninsdeletekey dontcreatekey
Root: HKCU; Subkey: "Software\Microsoft\Edge\NativeMessagingHosts\com.toneflix.flyylink"; Flags: uninsdeletekey dontcreatekey
Root: HKCU; Subkey: "Software\BraveSoftware\Brave-Browser\NativeMessagingHosts\com.toneflix.flyylink"; Flags: uninsdeletekey dontcreatekey
Root: HKCU; Subkey: "Software\Mozilla\NativeMessagingHosts\com.toneflix.flyylink"; Flags: uninsdeletekey dontcreatekey
Root: HKCU; Subkey: "Software\RegisteredApplications"; ValueType: none; ValueName: "FlyyLink"; Flags: uninsdeletevalue dontcreatekey
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "FlyyLink"; Flags: uninsdeletevalue dontcreatekey
; Same value the app's "Start with Windows" setting writes.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "FlyyLink"; ValueData: """{app}\{#AppExe}"" --background"; Tasks: startup
