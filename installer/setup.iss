; Kompilacja: scripts/Build-Installer.ps1 -IsccPath "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
; Wymagany Inno Setup 6. Skrypt nie pobiera runtime ani aktualizacji.
#ifndef AppVersion
  #define AppVersion "0.7.0-alpha"
#endif
#ifndef AppFileVersion
  #define AppFileVersion "0.7.0.0"
#endif
#ifndef PublishDir
  #error "Podaj /DPublishDir ze ścieżką do świeżej publikacji self-contained win-x64."
#endif
#ifndef InstallerOutputDir
  #define InstallerOutputDir "..\artifacts\installer"
#endif
#define LauncherIconFile AddBackslash(SourcePath) + "..\src\KrainaSurvivalu.Launcher.App\Assets\Branding\ks-launcher.ico"
#if !FileExists(LauncherIconFile)
  #error "Brak ks-launcher.ico w Assets\Branding projektu aplikacji."
#endif
#if !FileExists(PublishDir + "\KrainaSurvivalu.Launcher.exe")
  #error "Brak KrainaSurvivalu.Launcher.exe w PublishDir."
#endif
#if !FileExists(PublishDir + "\config\servers.json")
  #error "Brak config\servers.json w PublishDir."
#endif
#if !FileExists(PublishDir + "\config\community.json")
  #error "Brak config\community.json w PublishDir."
#endif
#if !FileExists(PublishDir + "\config\updates.json")
  #error "Brak config\updates.json w PublishDir."
#endif
#if !FileExists(PublishDir + "\SteamWorker\KrainaSurvivalu.SteamWorker.exe")
  #error "Brak pomocnika SteamWorker w PublishDir."
#endif
#if !FileExists(PublishDir + "\Updater\KrainaSurvivalu.Launcher.Updater.exe")
  #error "Brak pomocnika Updater w PublishDir."
#endif

[Setup]
AppId={{2A39A509-1415-490D-9940-F2A553EA0D8A}
AppName=Kraina Survivalu Launcher
AppVersion={#AppVersion}
AppPublisher=Kraina Survivalu
VersionInfoVersion={#AppFileVersion}
VersionInfoProductVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\KrainaSurvivalu\Launcher
DefaultGroupName=Kraina Survivalu
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
MinVersion=10.0.17763
OutputDir={#InstallerOutputDir}
OutputBaseFilename=KrainaSurvivalu-Launcher-Setup-{#AppVersion}-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile={#LauncherIconFile}
Uninstallable=yes
UninstallDisplayName=Kraina Survivalu Launcher
UninstallDisplayIcon={app}\KrainaSurvivalu.Launcher.exe
CloseApplications=yes
RestartApplications=no
SetupLogging=yes

[Languages]
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"

[Tasks]
Name: "desktopicon"; Description: "Utwórz skrót na pulpicie"; GroupDescription: "Dodatkowe skróty:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Kraina Survivalu Launcher"; Filename: "{app}\KrainaSurvivalu.Launcher.exe"; WorkingDir: "{app}"
Name: "{group}\Odinstaluj launcher"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Kraina Survivalu Launcher"; Filename: "{app}\KrainaSurvivalu.Launcher.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\KrainaSurvivalu.Launcher.exe"; Description: "Uruchom launcher Krainy Survivalu"; Flags: nowait postinstall skipifsilent

; Nie dodawać [UninstallDelete] dla %LOCALAPPDATA%\KrainaSurvivalu\Launcher.
; settings.json należy do użytkownika i pozostaje po odinstalowaniu oraz aktualizacji.
