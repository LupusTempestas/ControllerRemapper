; Inno Setup script for Wolverine Remapper.
; Build locally with installer\build.ps1, or let the GitHub Actions release
; workflow do it. Expects:
;   {#SourceDir}\WolverineRemapper.exe   (self-contained publish output)
;   {#DepsDir}\ViGEmBus_Setup.exe        (downloaded from nefarius/ViGEmBus)
;   {#DepsDir}\HidHide_Setup.exe         (downloaded from nefarius/HidHide)

#define AppName "Wolverine Remapper"
#ifndef AppVersion
  #define AppVersion "1.1.0"
#endif
#define AppPublisher "LupusTempestas"
#define AppURL "https://github.com/LupusTempestas/ControllerRemapper"
#define AppExe "WolverineRemapper.exe"
#ifndef SourceDir
  #define SourceDir "..\dist"
#endif
#ifndef DepsDir
  #define DepsDir "deps"
#endif

[Setup]
AppId={{7D2A9C4E-5B31-4F8E-9A6D-2C1E8F4B7A10}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/issues
AppUpdatesURL={#AppURL}/releases
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
LicenseFile=..\LICENSE
SetupIconFile=..\src\WolverineRemapper\Assets\app.ico
OutputDir=output
OutputBaseFilename=WolverineRemapper-Setup-{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Driver installers need elevation, so the whole setup runs elevated.
PrivilegesRequired=admin
WizardStyle=modern
; Always ask for the language first (also forwarded to the app, see [Run]).
ShowLanguageDialog=yes
CloseApplications=yes
RestartApplications=no
MinVersion=10.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "french";  MessagesFile: "compiler:Languages\French.isl"
Name: "german";  MessagesFile: "compiler:Languages\German.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "dutch";   MessagesFile: "compiler:Languages\Dutch.isl"

[CustomMessages]
english.StartupGroup=Startup:
english.StartupTask=Start with Windows (engine ready before your game launches)
english.DriversGroup=Drivers:
english.ViGEmTask=Install ViGEmBus driver (required — creates the virtual Xbox controller)
english.HidHideTask=Install HidHide driver (only for Wolverine V2 pad-button mode)
english.InstallingViGEm=Installing ViGEmBus driver...
english.InstallingHidHide=Installing HidHide driver...
french.StartupGroup=Démarrage :
french.StartupTask=Lancer avec Windows (moteur prêt avant le jeu)
french.DriversGroup=Pilotes :
french.ViGEmTask=Installer le pilote ViGEmBus (requis — crée la manette Xbox virtuelle)
french.HidHideTask=Installer le pilote HidHide (uniquement pour le mode Wolverine V2)
french.InstallingViGEm=Installation du pilote ViGEmBus...
french.InstallingHidHide=Installation du pilote HidHide...
german.StartupGroup=Autostart:
german.StartupTask=Mit Windows starten (Engine bereit, bevor das Spiel startet)
german.DriversGroup=Treiber:
german.ViGEmTask=ViGEmBus-Treiber installieren (erforderlich — erzeugt den virtuellen Xbox-Controller)
german.HidHideTask=HidHide-Treiber installieren (nur für den Wolverine-V2-Modus)
german.InstallingViGEm=ViGEmBus-Treiber wird installiert...
german.InstallingHidHide=HidHide-Treiber wird installiert...
spanish.StartupGroup=Inicio:
spanish.StartupTask=Iniciar con Windows (motor listo antes de abrir el juego)
spanish.DriversGroup=Controladores:
spanish.ViGEmTask=Instalar el controlador ViGEmBus (obligatorio — crea el mando Xbox virtual)
spanish.HidHideTask=Instalar el controlador HidHide (solo para el modo Wolverine V2)
spanish.InstallingViGEm=Instalando el controlador ViGEmBus...
spanish.InstallingHidHide=Instalando el controlador HidHide...
dutch.StartupGroup=Opstarten:
dutch.StartupTask=Starten met Windows (engine klaar voordat je game start)
dutch.DriversGroup=Stuurprogramma's:
dutch.ViGEmTask=ViGEmBus-stuurprogramma installeren (vereist — maakt de virtuele Xbox-controller)
dutch.HidHideTask=HidHide-stuurprogramma installeren (alleen voor de Wolverine V2-modus)
dutch.InstallingViGEm=ViGEmBus-stuurprogramma wordt geïnstalleerd...
dutch.InstallingHidHide=HidHide-stuurprogramma wordt geïnstalleerd...

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startup";     Description: "{cm:StartupTask}";       GroupDescription: "{cm:StartupGroup}"
Name: "vigem";       Description: "{cm:ViGEmTask}";         GroupDescription: "{cm:DriversGroup}"; Check: not ViGEmInstalled
Name: "hidhide";     Description: "{cm:HidHideTask}";       GroupDescription: "{cm:DriversGroup}"; Flags: unchecked; Check: not HidHideInstalled

[Files]
Source: "{#SourceDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#DepsDir}\ViGEmBus_Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Tasks: vigem
Source: "{#DepsDir}\HidHide_Setup.exe";  DestDir: "{tmp}"; Flags: deleteafterinstall; Tasks: hidhide

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\{#AppName} (engine auto-start)"; Filename: "{app}\{#AppExe}"; Parameters: "--autostart"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{tmp}\ViGEmBus_Setup.exe"; Parameters: "/quiet /norestart"; StatusMsg: "{cm:InstallingViGEm}"; Tasks: vigem; Flags: waituntilterminated
Filename: "{tmp}\HidHide_Setup.exe";  Parameters: "/quiet /norestart"; StatusMsg: "{cm:InstallingHidHide}"; Tasks: hidhide; Flags: waituntilterminated
; "Start with Windows" is a per-user Run key. Setup runs elevated, so the app
; writes it itself as the ORIGINAL user (runasoriginaluser) — the same code path
; the Settings tab and the tray toggle use.
Filename: "{app}\{#AppExe}"; Parameters: "--enable-startup"; Tasks: startup; Flags: runasoriginaluser waituntilterminated
; The language picked on the installer's first screen becomes the app's UI language.
Filename: "{app}\{#AppExe}"; Parameters: "--set-language={language}"; Flags: runasoriginaluser waituntilterminated
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\{#AppExe}"; Parameters: "--disable-startup"; RunOnceId: "DisableStartup"; Flags: waituntilterminated

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
function ViGEmInstalled: Boolean;
begin
  Result := RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\ViGEmBus');
end;

function HidHideInstalled: Boolean;
begin
  Result := RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\HidHide');
end;
