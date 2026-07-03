; ============================================================================
;  Normalign Revit Agent — installer production (Inno Setup 6)
;
;  - Instalare per-utilizator (fără drepturi de administrator).
;  - Copiază add-in-ul în %APPDATA%\Autodesk\Revit\Addins\<an>.
;  - Verifică WebView2 Runtime; dacă lipsește, îl instalează silențios din
;    redist\MicrosoftEdgeWebview2Setup.exe (bootstrapper Microsoft).
;  - ZERO secrete: nu conține email/parolă/token. Autentificarea se face la
;    prima pornire, prin login în browser (token salvat criptat local).
;
;  Compilare:  installer\build-installer.ps1   (rulează dotnet build + ISCC)
; ============================================================================

#define AppName "Normalign Revit Agent"
#define AppVersion "1.0.0"
#define Publisher "Normalign"
#define RevitYear "2027"
; Rădăcina cu binarele compilate (OutDir din .csproj)
#define BinDir "..\bin"

[Setup]
AppId={{7E9B2A64-3C1D-4E58-9A77-NORMALIGN2027}}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#Publisher}
; Instalare per-user, fără UAC/admin
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={userappdata}\Autodesk\Revit\Addins\{#RevitYear}\NormalignRevitAgent
DisableProgramGroupPage=yes
DisableDirPage=yes
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\NormalignRevitAgent.dll
OutputDir=Output
OutputBaseFilename=NormalignRevitAgent-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "ro"; MessagesFile: "compiler:Languages\Romanian.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Files]
; Add-in DLL + dependențe (WebView2 SDK, deps.json). PDB exclus.
Source: "{#BinDir}\*.dll";            DestDir: "{app}"; Flags: ignoreversion
Source: "{#BinDir}\*.deps.json";      DestDir: "{app}"; Flags: ignoreversion
Source: "{#BinDir}\runtimes\*";       DestDir: "{app}\runtimes"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#BinDir}\Assets\*";         DestDir: "{app}\Assets";   Flags: ignoreversion recursesubdirs createallsubdirs
; Manifestul .addin în rădăcina Addins\<an> (un nivel mai sus de {app})
Source: "..\NormalignRevitAgent.addin"; DestDir: "{userappdata}\Autodesk\Revit\Addins\{#RevitYear}"; Flags: ignoreversion
; Bootstrapper WebView2 (opțional — se rulează doar dacă runtime-ul lipsește)
Source: "redist\MicrosoftEdgeWebview2Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall skipifsourcedoesntexist

[UninstallDelete]
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\{#RevitYear}\NormalignRevitAgent.addin"

[Code]
// Detectează WebView2 Evergreen Runtime (client id fix Microsoft).
function WebView2Installed(): Boolean;
var
  pv: string;
begin
  Result :=
    RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', pv) or
    RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', pv) or
    RegQueryStringValue(HKCU, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', pv);
  Result := Result and (pv <> '') and (pv <> '0.0.0.0');
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  boot, msg: string;
  code: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    if not WebView2Installed() then
    begin
      boot := ExpandConstant('{tmp}\MicrosoftEdgeWebview2Setup.exe');
      if FileExists(boot) then
      begin
        // Instalare per-user, silențioasă
        Exec(boot, '/silent /install', '', SW_HIDE, ewWaitUntilTerminated, code);
      end
      else
      begin
        msg := 'Componenta „WebView2 Runtime" nu este instalată și nu a fost inclusă în acest installer.' + #13#10 +
               'Descarci și instalezi „Evergreen Bootstrapper" de la:' + #13#10 +
               'https://developer.microsoft.com/microsoft-edge/webview2/' + #13#10#13#10 +
               'Fără el, panoul Normalign nu se va afișa în Revit.';
        MsgBox(msg, mbInformation, MB_OK);
      end;
    end;
  end;
end;

// Avertizează dacă Revit rulează (fișierele ar fi blocate).
function InitializeSetup(): Boolean;
begin
  Result := True;
  if CheckForMutexes('Autodesk Revit') then
    MsgBox('Închide Revit înainte de instalare pentru a evita fișiere blocate.', mbInformation, MB_OK);
end;
