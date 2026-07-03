# Normalign Revit Agent

Add-in pentru **Revit 2027** care adaugă un panou de chat AI (stil Claude Code)
direct în Revit. Citește modelul deschis + view-ul activ + selecția și
interoghează backend-ul RAG Normalign (aceleași răspunsuri, citări și istoric ca
pe site). Construit ca să crească într-un asistent agentic (tool-use) fără rescriere.

---

## Cuprins
1. [Cum arată / ce face](#cum-arată--ce-face)
2. [Arhitectura pe scurt](#arhitectura-pe-scurt)
3. [Harta fișierelor (ce e unde)](#harta-fișierelor-ce-e-unde)
4. [Cum funcționează autentificarea](#cum-funcționează-autentificarea)
5. [Rulare locală (dev)](#rulare-locală-dev)
6. [Punere în producție](#punere-în-producție)
7. [Configurare](#configurare)
8. [Depanare](#depanare)

---

## Cum arată / ce face
- **Chat nativ minimal** (WebView2 + HTML local), temă preluată 1:1 din aplicația
  web, comutată automat după tema Revit (light/dark).
- **Context live din editor**: numele modelului, view-ul activ și elementele
  selectate (categorie, tip, nivel, id) — trimise ca `ifcContext` la fiecare întrebare.
- **Moduri Standard / Aprofundat** — aprofundat = flux SSE cu progres + raționament.
- **Buton Stop** — butonul de trimitere devine stop în timpul generării.
- **Citări/surse clicabile** → PDF reader peste chat (`#page=N`).
- **Istoric conversații** — același cont și aceleași chat-uri ca pe web.
- **Login în browser** (fără parole/chei în add-in) + **Logout**.

---

## Arhitectura pe scurt

```
┌──────────────────────── REVIT (proces .NET 10, Windows) ────────────────────────┐
│                                                                                  │
│  App.cs (IExternalApplication)                                                   │
│    ├─ buton ribbon "Normalign" ─────────────► ShowChatCommand.cs                 │
│    ├─ panou dockable ─────────► Ui/ChatPane.cs  ──hostează──►  Assets/*.html      │
│    │                                │  (WebView2, UI de chat)   (login/chat)      │
│    │        punte JS ⇄ C#           │                                             │
│    └─ ExternalEvent ─► Revit/RevitRequestHandler.cs                              │
│           (singurul loc unde e legal API-ul Revit: model, view, selecție)        │
│                                     │                                             │
│   Services/                         │                                             │
│    ├─ NormalignApi.cs  ── HTTP/SSE ─┼──────────────────────────────┐             │
│    ├─ AuthStore.cs (token DPAPI)    │                              │             │
│    ├─ LoginServer.cs (loopback)     │                              │             │
│    └─ Config.cs (webUrl)            │                              ▼             │
└─────────────────────────────────────┼──────────────────────  { Bearer token }    ┘
                                       ▼
                        BACKEND NORMALIGN (Next.js, deja existent)
                        /api/chat · /api/history · /api/messages
                        /api/desktop/token · /desktop-auth
```

**Principiul cheie:** creierul (Claude, RAG, rerank, prompturi) rămâne pe server;
add-in-ul e subțire și face singurul lucru pe care doar el îl poate face —
citește modelul Revit (pe thread-ul Revit, prin `ExternalEvent`) și hostează UI-ul.

---

## Harta fișierelor (ce e unde)

### Repo add-in: `busi4el1k-eng/normalign-revit-agent` (Windows, C#)

| Fișier | Ce implementează |
|--------|------------------|
| `NormalignRevitAgent.csproj` | Proiect .NET 10-windows (WPF). Referințe: RevitAPI/RevitAPIUI (nu se copiază), pachete WebView2 + ProtectedData. Output fix în `bin\`. |
| `NormalignRevitAgent.addin` | Manifestul citit de Revit la pornire. `<Assembly>` **relativ** → funcționează pe orice PC. |
| `App.cs` | **Punctul de intrare.** Înregistrează panoul dockable + butonul ribbon; leagă evenimentele UI (send/stop/login/logout/history) de servicii; ascultă `ViewActivated` ca să reîmprospăteze contextul. |
| `ShowChatCommand.cs` | Comanda butonului din ribbon — arată panoul. |
| `Ui/ChatPane.cs` | Gazda WebView2. Navighează spre `login.html` sau `chat.html` după cum există token; puntea JS⇄C# (parsează mesajele din UI, trimite răspunsuri). |
| `Ui/ChatPaneProvider.cs` | Spune Revit-ului ce element WPF să pună în panou și unde (dreapta). |
| `Assets/chat.html` | **UI-ul de chat** (stil Claude Code): markdown, citări clicabile, PDF reader, moduri standard/aprofundat, stop, istoric, logout, temă light/dark. Servit local (virtual host), fără rețea. |
| `Assets/login.html` | Ecranul de login (buton „Conectează-te"). |
| `Revit/RevitRequestHandler.cs` | **Seam-ul de thread Revit** (`IExternalEventHandler`). Citește modelul/view-ul/selecția pe thread-ul Revit, lansează HTTP-ul (anulabil), detectează tema. Aici se vor adăuga tool-urile agentice v2. |
| `Services/NormalignApi.cs` | Client HTTP către backend: `/api/chat` (JSON standard **sau** flux SSE la aprofundat), `/api/history`, `/api/messages`. Pune tokenul Bearer per cerere. |
| `Services/AuthStore.cs` | Salvează/încarcă tokenul **criptat cu DPAPI** (`%APPDATA%\NormalignRevitAgent\auth.dat`). |
| `Services/LoginServer.cs` | Login loopback (stil `gh auth login`): pornește `HttpListener` pe `127.0.0.1`, deschide browserul, capturează tokenul. |
| `Services/Config.cs` | Doar `webUrl` (din `config.json` sau implicit prod). Fără secrete. |
| `Tools/IRevitTool.cs`, `ToolRegistry.cs`, `GetModelSummaryTool.cs` | Registrul de „capabilități" peste model. v1 = rezumat model; v2 = query/tag/etc. |
| `install.ps1` / `install.bat` | Build + copiere în `%APPDATA%\Autodesk\Revit\Addins\2027` (dev, fără admin). |
| `installer/normalign-revit-agent.iss` | **Installer production** (Inno Setup): per-user, instalează WebView2 dacă lipsește, fără secrete. |
| `installer/build-installer.ps1` | Build Release + descarcă bootstrapper WebView2 + compilează `.exe`. |

### Repo web: `digital-standart-web` (backend, TypeScript) — fișiere pentru add-in

| Fișier | Ce implementează |
|--------|------------------|
| `src/lib/desktop-token.ts` | **Emite/verifică** tokenul personal (HMAC-SHA256, `DESKTOP_TOKEN_SECRET`). |
| `src/lib/desktop-auth.ts` | `getRequestUser()` — autentificare unificată: sesiune Clerk (web) **sau** token de desktop (Bearer). |
| `src/app/api/desktop/token/route.ts` | Ruta protejată Clerk care emite tokenul după login. |
| `src/app/desktop-auth/page.tsx` | Pagina de callback: după login cere tokenul și redirecționează pe `127.0.0.1:<port>`. |
| `src/app/login/page.tsx` | Modificat: onorează `redirect_url` (doar căi relative same-origin). |
| `src/app/api/chat/route.ts`, `history/route.ts`, `messages/route.ts` | Folosesc `getRequestUser()` în loc de doar Clerk. |
| `src/proxy.ts` | `/api/chat`, `/api/history`, `/api/messages` trecute în lista publică (autorizarea se face în handler). |

---

## Cum funcționează autentificarea

Fără parole sau chei în add-in / installer. Flux (o singură dată per stație):

1. La pornire, dacă nu există token → panoul arată `login.html`.
2. „Conectează-te" → add-in-ul pornește un `HttpListener` pe `http://127.0.0.1:<port_liber>`
   și deschide în browser `{webUrl}/desktop-auth?port=<port>`.
3. Nelogat → redirect la `/login?redirect_url=…` (login Clerk) și înapoi.
4. Logat → pagina cere `/api/desktop/token` (protejat Clerk) → primește un token
   semnat HMAC și redirecționează la `http://127.0.0.1:<port>/callback?token=…`.
5. Add-in-ul capturează tokenul, îl salvează **criptat DPAPI** și comută pe chat.
6. Fiecare cerere ulterioară trimite `Authorization: Bearer <token>`; serverul îl
   verifică cu `DESKTOP_TOKEN_SECRET` și încarcă utilizatorul.
7. **Logout** = șterge tokenul local → revine la ecranul de login.

Secretul de semnare există **doar** ca variabilă de mediu pe server.

---

## Rulare locală (dev)

```powershell
# 1. Backend (în WSL / mașina de dev)
cd digital-standart-web
# adaugă în .env:  DESKTOP_TOKEN_SECRET="<hex random 32+ bytes>"
npm run dev              # http://localhost:3000

# 2. Add-in (Windows)
cd C:\Users\<tu>\source\repos\NormalignRevitAgent
powershell -ExecutionPolicy Bypass -File install.ps1
```
Setează `%APPDATA%\NormalignRevitAgent\config.json` → `{ "webUrl": "http://localhost:3000" }`.
Repornește Revit → **Always Load** → tab **Normalign** → **Chat**. Loghează-te
(trebuie să fii logat în Clerk în browserul default).

---

## Punere în producție

### A. Backend (o singură dată)
1. Adaugă pe server variabila de mediu:
   ```
   DESKTOP_TOKEN_SECRET="<hex random 48+ caractere, secret>"
   ```
   (generează cu `openssl rand -hex 32`). **Nu** o pune în add-in/installer.
2. Deploy-ul normal al aplicației web (conține deja `desktop-token`, `/desktop-auth`,
   `/api/desktop/token`, rutele actualizate și `proxy.ts`).
3. Verifică: `GET https://normalign.com/api/messages?chatId=x` fără header → 401.

### B. Add-in — construiește installer-ul (.exe)
1. Pe stația de build, o singură dată: instalează **Inno Setup 6** (gratuit,
   https://jrsoftware.org/isdl.php).
2. Rulează:
   ```powershell
   powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1
   ```
   Rezultatul: `installer\Output\NormalignRevitAgent-Setup-1.0.0.exe`.
3. `config.json` implicit lipsește → add-in-ul folosește `https://normalign.com`.
   (Pentru un URL diferit, distribuie și un `config.json` sau lasă utilizatorul să-l creeze.)

### C. Distribuție
Trimite `.exe`-ul utilizatorilor. La dublu-click: instalare per-user (fără admin),
instalează WebView2 dacă lipsește, pune add-in-ul în `%APPDATA%\...\Addins\2027`.
La prima pornire a Revit → **Always Load** → login în browser → gata.

> Alt Revit (2026/2028): schimbă `RevitYear` în `.iss` și, dacă e alt .NET,
> `TargetFramework` în `.csproj` (Revit 2027 = .NET 10).

---

## Configurare
`%APPDATA%\NormalignRevitAgent\config.json` (opțional):
```json
{ "webUrl": "http://localhost:3000" }
```
Implicit: `https://normalign.com`. Tokenul de auth **nu** stă aici — e în
`auth.dat` (criptat), gestionat de login/logout.

---

## Depanare
- **Tab-ul Normalign nu apare** → Revit era deschis la instalare, sau ai apăsat
  „Do Not Load". Repornește și alege **Always Load**. Verifică jurnalul din
  `%LOCALAPPDATA%\Autodesk\Revit\Autodesk Revit 2027\Journals`.
- **Panou gol / eroare WebView2** → instalează „WebView2 Evergreen Runtime".
- **401 la trimitere** → token expirat/lipsă: apasă Logout apoi Login din nou;
  pe server verifică `DESKTOP_TOKEN_SECRET`.
- **Login nu revine în Revit** → firewall pe loopback (127.0.0.1) sau nu erai
  logat în Clerk în browser. Reîncearcă.
