; Inno Setup script for Wolverine Remapper.
; Build locally with installer\build.ps1, or let the GitHub Actions release
; workflow do it. Expects:
;   {#SourceDir}\WolverineRemapper.exe   (self-contained publish output)
;   {#DepsDir}\ViGEmBus_Setup.exe        (downloaded from nefarius/ViGEmBus)
;   {#DepsDir}\HidHide_Setup.exe         (downloaded from nefarius/HidHide)

#define AppName "Wolverine Remapper"
#ifndef AppVersion
  #define AppVersion "1.2.2"
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
Name: "portuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

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
portuguese.StartupGroup=Inicialização:
portuguese.StartupTask=Iniciar com o Windows (motor pronto antes de o jogo abrir)
portuguese.DriversGroup=Drivers:
portuguese.ViGEmTask=Instalar o driver ViGEmBus (obrigatório — cria o controle Xbox virtual)
portuguese.HidHideTask=Instalar o driver HidHide (apenas para o modo de botões do Wolverine V2)
portuguese.InstallingViGEm=Instalando o driver ViGEmBus...
portuguese.InstallingHidHide=Instalando o driver HidHide...
portuguese.CtrlTitle=Qual controle você tem?
portuguese.CtrlSub=A escolha configura o aplicativo e seleciona os drivers necessários.
portuguese.CtrlPrompt=Você pode mudar isso depois no aplicativo. "Não sei" instala tudo.
portuguese.CtrlV3=Razer Wolverine V3 Pro 8K (edição PC, Synapse 4)
portuguese.CtrlV2=Razer Wolverine V2 / V2 Chroma / V2 Pro (precisa do HidHide)
portuguese.CtrlUnsure=Não sei, ou os dois

english.CtrlTitle=Which controller do you have?
english.CtrlSub=The choice sets up the app and picks the drivers you need.
english.CtrlPrompt=You can change this later in the app. "Not sure" installs everything.
english.CtrlV3=Razer Wolverine V3 Pro 8K (PC edition, Synapse 4)
english.CtrlV2=Razer Wolverine V2 / V2 Chroma / V2 Pro (needs HidHide)
english.CtrlUnsure=Not sure, or both
french.CtrlTitle=Quelle manette avez-vous ?
french.CtrlSub=Ce choix configure l'application et sélectionne les pilotes nécessaires.
french.CtrlPrompt=Modifiable plus tard dans l'application. « Je ne sais pas » installe tout.
french.CtrlV3=Razer Wolverine V3 Pro 8K (édition PC, Synapse 4)
french.CtrlV2=Razer Wolverine V2 / V2 Chroma / V2 Pro (nécessite HidHide)
french.CtrlUnsure=Je ne sais pas, ou les deux
german.CtrlTitle=Welchen Controller hast du?
german.CtrlSub=Die Wahl richtet die App ein und wählt die nötigen Treiber.
german.CtrlPrompt=Später in der App änderbar. „Nicht sicher" installiert alles.
german.CtrlV3=Razer Wolverine V3 Pro 8K (PC-Edition, Synapse 4)
german.CtrlV2=Razer Wolverine V2 / V2 Chroma / V2 Pro (braucht HidHide)
german.CtrlUnsure=Nicht sicher, oder beide
spanish.CtrlTitle=¿Qué mando tienes?
spanish.CtrlSub=La elección configura la aplicación y elige los controladores necesarios.
spanish.CtrlPrompt=Se puede cambiar después en la aplicación. «No estoy seguro» lo instala todo.
spanish.CtrlV3=Razer Wolverine V3 Pro 8K (edición PC, Synapse 4)
spanish.CtrlV2=Razer Wolverine V2 / V2 Chroma / V2 Pro (necesita HidHide)
spanish.CtrlUnsure=No estoy seguro, o ambos
dutch.CtrlTitle=Welke controller heb je?
dutch.CtrlSub=De keuze stelt de app in en kiest de benodigde stuurprogramma's.
dutch.CtrlPrompt=Later te wijzigen in de app. "Weet ik niet" installeert alles.
dutch.CtrlV3=Razer Wolverine V3 Pro 8K (pc-editie, Synapse 4)
dutch.CtrlV2=Razer Wolverine V2 / V2 Chroma / V2 Pro (heeft HidHide nodig)
dutch.CtrlUnsure=Weet ik niet, of allebei

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
; The language picked on the installer's first screen becomes the app's UI language,
; and the controller answer becomes the default controller mode of new profiles.
; Skipped for in-app updates (/UPDATE=1): the user's choices already exist.
Filename: "{app}\{#AppExe}"; Parameters: "--set-language={language} --set-controller={code:ControllerCode}"; Flags: runasoriginaluser waituntilterminated; Check: not IsUpdate
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
; In-app update (silent): relaunch the app as the user once the files are replaced.
Filename: "{app}\{#AppExe}"; Parameters: "--updated"; Flags: nowait runasoriginaluser; Check: IsUpdate

[UninstallRun]
Filename: "{app}\{#AppExe}"; Parameters: "--disable-startup"; RunOnceId: "DisableStartup"; Flags: waituntilterminated

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
var
  ControllerPage: TInputOptionWizardPage;

function ViGEmInstalled: Boolean;
begin
  Result := RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\ViGEmBus');
end;

function HidHideInstalled: Boolean;
begin
  Result := RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\HidHide');
end;

{ "Which controller do you have?" page, right after Welcome. }
procedure InitializeWizard;
begin
  ControllerPage := CreateInputOptionPage(wpWelcome,
    CustomMessage('CtrlTitle'), CustomMessage('CtrlSub'), CustomMessage('CtrlPrompt'), True, False);
  ControllerPage.Add(CustomMessage('CtrlV3'));
  ControllerPage.Add(CustomMessage('CtrlV2'));
  ControllerPage.Add(CustomMessage('CtrlUnsure'));
  ControllerPage.SelectedValueIndex := 0;
end;

{ Started by the app itself with /UPDATE=1 (silent in-place upgrade). }
function IsUpdate: Boolean;
begin
  Result := ExpandConstant('{param:UPDATE|0}') = '1';
end;

{ V2 and "not sure" need HidHide; a V3 Pro 8K does not. }
function NeedsHidHide: Boolean;
begin
  Result := ControllerPage.SelectedValueIndex <> 0;
end;

{ Passed to the app so new profiles start in the right controller mode.
  Only an explicit V3 answer (index 0) may start in keyboard mode: V3's
  paddles are bound to keyboard keys via Synapse. V2 (index 1) and "not
  sure, or both" (index 2) both need pad-button mode, matching NeedsHidHide
  above — Razer's own software can't bind a V2 paddle to a keyboard key at
  all, so guessing V3 for an unsure user risks silently swallowing real
  keyboard keys that collide with the default trigger VkCodes. }
function ControllerCode(Param: String): String;
begin
  if ControllerPage.SelectedValueIndex = 0 then Result := 'v3' else Result := 'v2';
end;

procedure CurPageChanged(CurPageID: Integer);
var
  i: Integer;
begin
  if CurPageID = wpSelectTasks then
    for i := 0 to WizardForm.TasksList.Items.Count - 1 do
      if Pos('HidHide', WizardForm.TasksList.ItemCaption[i]) > 0 then
        WizardForm.TasksList.Checked[i] := NeedsHidHide;
end;
