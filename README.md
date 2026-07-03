# Normalign Revit Agent

Add-in Revit 2027: un panou de chat AI (stil Claude Code) care citește modelul
Revit deschis + view-ul activ + selecția și interoghează backend-ul Normalign.
Construit ca să crească într-un asistent agentic (tool-use) fără rescriere.

## Funcționalități
- Chat nativ minimal (WebView2 + HTML local), temă preluată din aplicația web,
  comutată după tema Revit (light/dark).
- Context live din editor: model, view activ, elemente selectate — trimis ca
  `ifcContext` la fiecare întrebare.
- Moduri **Standard** / **Aprofundat** (flux SSE cu progres + raționament).
- Buton **Stop** (butonul de trimitere devine stop în timpul generării).
- Citări/surse clicabile → **PDF reader** peste chat (`#page=N`).
- **Istoric** conversații (același cont ca pe web).
- **Login în browser** (fără parole/chei în add-in) + **Logout**.

## Cerințe
- Revit 2027 (.NET 10) · .NET 10 SDK · WebView2 Runtime (instalat de installer).

## Autentificare (fără secrete hardcodate)
La prima pornire, panoul arată ecranul de login. „Conectează-te" deschide
`{webUrl}/desktop-auth` în browser; după login-ul Clerk primești un **token
personal** (semnat HMAC pe server), salvat **criptat cu DPAPI** în
`%APPDATA%\NormalignRevitAgent\auth.dat`. Logout-ul îl șterge.

Pe **server** trebuie setat un singur secret nou (vezi aplicația web):
```
DESKTOP_TOKEN_SECRET="<48+ hex random>"
```

## Configurare (opțional)
`%APPDATA%\NormalignRevitAgent\config.json` — doar URL-ul backend-ului:
```json
{ "webUrl": "http://localhost:3000" }
```
Implicit `https://normalign.com`.

## Build & instalare (dev)
```
dotnet build NormalignRevitAgent.csproj -c Debug
powershell -ExecutionPolicy Bypass -File install.ps1
```
Deploy în `%APPDATA%\Autodesk\Revit\Addins\2027`. Repornește Revit → **Always Load**.

## Installer production (.exe)
```
powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1
```
Cerință o singură dată pe stația de build: **Inno Setup 6** (gratuit,
https://jrsoftware.org/isdl.php). Rezultatul: `installer\Output\NormalignRevitAgent-Setup-x.y.z.exe`
— instalare per-user (fără admin), verifică/instalează WebView2, fără secrete.

## Arhitectură
- `App.cs` — startup: panou dockable, buton ribbon, orchestrare login/chat.
- `Ui/ChatPane.cs` + `Assets/{login,chat}.html` — UI (WebView2).
- `Revit/RevitRequestHandler.cs` — seam-ul de thread Revit (context + ExternalEvent).
- `Tools/` — `IRevitTool` + `ToolRegistry` (extensie pentru v2 agentic).
- `Services/` — `NormalignApi` (HTTP + SSE), `AuthStore` (DPAPI), `LoginServer`
  (loopback OAuth), `Config`.
