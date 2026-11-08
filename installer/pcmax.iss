; PC Max — per-machine installer (Inno Setup 6).
;
; Build (the release workflow does this):
;   iscc /DAppVersion=1.2.3 /DPublishDir=C:\path\to\publish installer\pcmax.iss
;
; Installs into %ProgramFiles%\PC Max, which only administrators can write. That matters: the app runs elevated
; (and, if the user keeps "Arrancar com o Windows", from an elevated logon task), so its files must never be
; somewhere a normal user or a non-admin process could replace them.

#ifndef AppVersion
  #error "Passa /DAppVersion=X.Y.Z ao iscc."
#endif
#ifndef PublishDir
  #error "Passa /DPublishDir=<pasta do dotnet publish> ao iscc."
#endif
#ifndef OutputDir
  #define OutputDir "Output"
#endif

[Setup]
; Never change AppId: Windows uses it to recognise upgrades and the uninstaller.
AppId={{6B0E9D57-3F2A-4C8E-9B1D-5C7A2E4F8A11}
AppName=PC Max
AppVersion={#AppVersion}
AppVerName=PC Max {#AppVersion}
AppPublisher=PC Max
AppPublisherURL=https://github.com/rezyto/pc-optimizer-max
AppSupportURL=https://github.com/rezyto/pc-optimizer-max/issues
AppUpdatesURL=https://github.com/rezyto/pc-optimizer-max/releases
VersionInfoVersion={#AppVersion}
VersionInfoProductName=PC Max
DefaultDirName={autopf}\PC Max
DefaultGroupName=PC Max
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#OutputDir}
OutputBaseFilename=PcMax-Setup-{#AppVersion}
SetupIconFile=..\src\PcMax.App\Assets\pcmax.ico
UninstallDisplayIcon={app}\PcMax.exe
UninstallDisplayName=PC Max
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Updates run while the app is open: close it (Restart Manager, then force) instead of failing on locked files.
CloseApplications=force
RestartApplications=no

[Languages]
Name: "portuguese"; MessagesFile: "compiler:Languages\Portuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\PC Max"; Filename: "{app}\PcMax.exe"
Name: "{autodesktop}\PC Max"; Filename: "{app}\PcMax.exe"; Tasks: desktopicon

[Run]
; The app's manifest requires administrator rights; runascurrentuser starts it from the (already elevated) setup,
; instead of as the original user, which would fail with "elevation required".
Filename: "{app}\PcMax.exe"; Description: "{cm:LaunchProgram,PC Max}"; Flags: postinstall nowait skipifsilent runascurrentuser
; Silent install = automatic update started by the app: reopen it in the tray.
Filename: "{app}\PcMax.exe"; Parameters: "--minimized"; Flags: nowait runascurrentuser; Check: WizardSilent

[UninstallRun]
; [UninstallRun] runs before any file is removed, in this order.
; 1. Stop a running copy so it cannot re-apply anything while we clean up.
Filename: "{sys}\taskkill.exe"; Parameters: "/F /T /IM PcMax.exe"; Flags: runhidden waituntilterminated; RunOnceId: "PcMaxKill"
; 2. Resume paused apps, revert every tweak still recorded and remove the "PC Max" logon task.
Filename: "{app}\PcMax.exe"; Parameters: "--uninstall-cleanup"; Flags: runhidden waituntilterminated; RunOnceId: "PcMaxUninstallCleanup"
