; Instalador de Lanzador Switch (Inno Setup 6). Lo compila herramientas\empaquetar.ps1:
;   ISCC /DVersion=1.2.0 /DOrigen=<publicar\Lanzador> /DSalida=<publicar> instalador\LanzadorSwitch.iss
;
; - No pide permisos de administrador: instala para el usuario actual, en una carpeta donde pueda escribir,
;   porque el lanzador guarda sus datos (configuración, caché, respaldos) en la carpeta "datos", junto al ejecutable.
; - Al actualizar o desinstalar nunca toca "datos" (al desinstalar pregunta si también se borra).
; - El lanzador se actualiza solo ejecutando este instalador con /SILENT /DIR="<su carpeta>" /RELANZAR=1.

#ifndef Version
  #define Version "0.0.0"
#endif
#ifndef Origen
  #define Origen "..\publicar\Lanzador"
#endif
#ifndef Salida
  #define Salida "..\publicar"
#endif

#define Nombre "Lanzador Switch"
#define Repositorio "https://github.com/corpjorge/lanzador-switch"

[Setup]
; No cambiar el AppId: es lo que identifica la instalación al actualizar.
AppId={{6F0C2B7A-3D51-4E8B-9A64-1C7E5B2D9F30}
AppName={#Nombre}
AppVersion={#Version}
AppVerName={#Nombre} {#Version}
AppPublisher=corpjorge
AppPublisherURL={#Repositorio}
AppSupportURL={#Repositorio}/issues
AppUpdatesURL={#Repositorio}/releases
VersionInfoVersion={#Version}
VersionInfoProductName={#Nombre}
DefaultDirName={code:CarpetaPorDefecto}
UsePreviousAppDir=yes
DisableDirPage=no
DirExistsWarning=no
DisableProgramGroupPage=yes
DisableWelcomePage=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#Salida}
OutputBaseFilename=LanzadorSwitch-{#Version}-win-x64-instalador
SetupIconFile=..\src\Lanzador.App\Icono\Lanzador.ico
UninstallDisplayIcon={app}\Lanzador.exe
UninstallDisplayName={#Nombre}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Si el lanzador está abierto, se cierra para poder reemplazar sus archivos; lo vuelve a abrir [Run], no Windows.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Messages]
spanish.SelectDirLabel3=El lanzador guarda su configuración en esta misma carpeta, así que debe ser una donde puedas escribir (no «Archivos de programa»). Los emuladores que instales desde el lanzador irán en la carpeta que la contiene: con C:\Switch\Lanzador, quedan en C:\Switch\Eden, C:\Switch\Ryujinx…

[CustomMessages]
spanish.SinPermiso=No se puede escribir en esa carpeta sin permisos de administrador.%n%nElige otra, por ejemplo C:\Switch\Lanzador o una carpeta dentro de tus documentos.
spanish.BorrarDatos=Tu configuración, la caché y los respaldos siguen en:%n%n%1%n%n¿Quieres borrarlos también?
spanish.TareaEscritorio=Crear un acceso directo en el escritorio

[Tasks]
Name: "desktopicon"; Description: "{cm:TareaEscritorio}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; Todo lo que publicó empaquetar.ps1, menos una carpeta "datos" que hubiera por haber abierto el lanzador ahí.
Source: "{#Origen}\*"; DestDir: "{app}"; Excludes: "\datos,\datos\*"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#Nombre}"; Filename: "{app}\Lanzador.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\{#Nombre}"; Filename: "{app}\Lanzador.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
; Instalación normal: casilla «Abrir Lanzador Switch» al terminar.
Filename: "{app}\Lanzador.exe"; Description: "{cm:LaunchProgram,{#Nombre}}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent
; Actualización pedida desde el lanzador (/SILENT /RELANZAR=1): se vuelve a abrir solo.
Filename: "{app}\Lanzador.exe"; WorkingDir: "{app}"; Flags: nowait; Check: Relanzar

[Code]
// True si el usuario actual puede crear archivos en esa carpeta (o crearla, si todavía no existe).
function SePuedeEscribir(Carpeta: String): Boolean;
var
  Prueba: String;
begin
  // Se sube hasta la primera carpeta que exista: ahí es donde hace falta el permiso.
  while (Carpeta <> '') and not DirExists(Carpeta) do
  begin
    if ExtractFileDir(Carpeta) = Carpeta then
      Break;
    Carpeta := ExtractFileDir(Carpeta);
  end;
  if not DirExists(Carpeta) then
  begin
    Result := False;
    Exit;
  end;
  Prueba := AddBackslash(Carpeta) + 'lanzador-switch-' + GetDateTimeString('yyyymmddhhnnss', #0, #0) + '.tmp';
  if CreateDir(Prueba) then
  begin
    RemoveDir(Prueba);
    Result := True;
  end
  else
    Result := False;
end;

// C:\Switch\Lanzador si se puede escribir en C:\ (lo habitual); si no, una carpeta del usuario.
function CarpetaPorDefecto(Param: String): String;
begin
  if SePuedeEscribir(ExpandConstant('{sd}\Switch')) then
    Result := ExpandConstant('{sd}\Switch\Lanzador')
  else
    Result := ExpandConstant('{localappdata}\Switch\Lanzador');
end;

function Relanzar: Boolean;
begin
  Result := WizardSilent and (ExpandConstant('{param:RELANZAR|0}') = '1');
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = wpSelectDir) and not WizardSilent and not SePuedeEscribir(WizardDirValue) then
  begin
    MsgBox(CustomMessage('SinPermiso'), mbError, MB_OK);
    Result := False;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Datos: String;
begin
  if CurUninstallStep <> usPostUninstall then
    Exit;
  Datos := ExpandConstant('{app}\datos');
  // Los datos del usuario se conservan salvo que diga que no; en una desinstalación silenciosa nunca se borran.
  if DirExists(Datos) and not UninstallSilent then
    if MsgBox(FmtMessage(CustomMessage('BorrarDatos'), [Datos]), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
    begin
      DelTree(Datos, True, True, True);
      RemoveDir(ExpandConstant('{app}'));
    end;
end;
