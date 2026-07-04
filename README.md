# Normalign Revit Agent

Add-in pentru **Revit 2027** care adaugă un panou de chat AI (stil Claude Code)
direct în Revit. Citește modelul deschis + view-ul activ + selecția și
interoghează backend-ul RAG Normalign (aceleași răspunsuri, citări și istoric ca
pe site). Construit ca să crească într-un asistent agentic (tool-use) fără rescriere.

---

## Cuprins
1. [Cum arată / ce face](#cum-arată--ce-face)
2. [Cum se folosește (ghid utilizator)](#cum-se-folosește-ghid-utilizator)
3. [Arhitectura pe scurt](#arhitectura-pe-scurt)
4. [Harta fișierelor (ce e unde)](#harta-fișierelor-ce-e-unde)
5. [Cum funcționează autentificarea](#cum-funcționează-autentificarea)
6. [Rulare locală (dev)](#rulare-locală-dev)
7. [Punere în producție](#punere-în-producție)
8. [Configurare](#configurare)
9. [Depanare](#depanare)

---

## Cum arată / ce face
- **Chat nativ minimal** (WebView2 + HTML local), temă preluată 1:1 din aplicația
  web, comutată automat după tema Revit (light/dark).
- **Context live din editor**: numele modelului, view-ul activ și elementele
  selectate (categorie, tip, nivel, id) — trimise ca `ifcContext` la fiecare întrebare.
- **Moduri Plan / Edit**:
  - **Plan** = chatul RAG obișnuit (cu sub-modurile Standard / Aprofundat — aprofundat
    = flux SSE cu progres + raționament). Răspunde și la întrebări despre interfața
    Revit („cum fac o secțiune?") — serverul detectează intenția și sare peste RAG.
  - **Edit** = buclă agentică (Claude tool-use prin `/api/agent`): agentul inspectează
    modelul (interogări filtrate, detalii de elemente, avertismente, captură de view
    pentru vision) și îl **modifică** (parametri, schimbare de tip, mutare, ștergere,
    evidențiere, izolare) — fiecare operație în tranzacția ei, cu Undo separat în
    Revit. UI-ul arată live tool-urile rulate. Serverul primește la fiecare rundă
    istoricul conversației (din Postgres), deci agentul ține minte ce a propus.
    - **Agent autonom (stil Claude Code)**: agentul decide singur pașii, își asumă
      ipoteze rezonabile și execută — nu dă indicații de interfață pentru lucruri
      pe care le poate face el, nu pune întrebări deschise și nu oferă meniuri de
      opțiuni. Modificările punctuale cerute explicit le face direct.
    - **Confirmare Da/Nu**: doar înainte de ștergeri sau modificări în masă, agentul
      propune planul și încheie cu o linie `[CONFIRMĂ] …`, pe care UI-ul o
      transformă în butoane de aprobare/refuz. „Da" aprobă **obiectivul**: dacă pe
      parcurs realitatea diferă de ipoteze, agentul adaptează planul și merge până
      la capăt, la același nivel de risc — re-întreabă doar dacă acțiunea devine
      categoric mai riscantă decât cea aprobată.
- **Buton Stop** — butonul de trimitere devine stop în timpul generării.
- **Citări/surse clicabile** → reader peste chat: normativele au taburi
  **Conținut (markdown) + PDF** (`#page=N`), fișele tehnice au **Conținut** (nu există
  PDF pentru ele); navigare pe pagini, imagini de pe CDN.
- **Istoric conversații** — același cont și aceleași chat-uri ca pe web.
- **Login în browser** (fără parole/chei în add-in) + **Logout**.

---

## Cum se folosește (ghid utilizator)

### Instalare (utilizator final)
1. Primești `NormalignRevitAgent-Setup-<versiune>.exe` → dublu-click. Instalare
   per-user, **fără drepturi de admin**; instalează automat WebView2 dacă lipsește.
2. Pornește Revit 2027 → la întrebarea de încărcare a add-in-ului alege **Always Load**.
3. Tab-ul **Normalign** apare în ribbon → apasă **Chat** → panoul se deschide în dreapta.
4. **Conectează-te** → se deschide browserul, te loghezi cu contul Normalign
   (același ca pe [normalign.com](https://normalign.com)) → panoul comută automat pe chat.
   Login-ul se face o singură dată per stație.

### Lucrul de zi cu zi
- **Modul Plan** (implicit) — întrebări și analiză, fără modificări:
  - întrebări normative („ce lățime minimă are un coridor de evacuare?") → răspuns
    cu citări clicabile din normative (reader cu Conținut + PDF);
  - întrebări despre Revit („cum fac o secțiune?") → pași concreți în interfață;
  - întrebări despre modelul deschis („câte uși am pe nivelul 1?") — panoul vede
    live modelul, view-ul activ și selecția;
  - sub-modul **Aprofundat**: analiză extinsă cu raționament vizibil (mai lentă).
- **Modul Edit** — agentul **modifică modelul** la cerere:
  - formulezi obiectivul („redenumește camerele după destinație", „șterge pereții
    duplicați", „consolidează tipurile de pereți importate din IFC") și agentul
    inspectează modelul, decide pașii și execută;
  - modificările mici, cerute explicit, le face **direct**; pentru ștergeri sau
    modificări în masă primești o bară **Da / Nu** — un click și execută tot;
  - fiecare operație e o tranzacție separată → **Ctrl+Z** anulează pas cu pas;
  - selecția din Revit contează: „peretele ăsta" = ce ai selectat acum.
- **Stop** — butonul de trimitere devine ■ în timpul generării; îl apeși și agentul
  se oprește (ce a apucat să modifice rămâne, cu Undo disponibil).
- **Istoric** — aceleași conversații ca pe site; le poți continua din oricare client.

### Ce poate atinge agentul în model (modul Edit)
| Acțiune | Tool | Direct sau cu confirmare? |
|---------|------|---------------------------|
| Setare parametri (nume, comentarii, valori cu unități) | `set_parameters` | direct, dacă e punctual |
| Schimbare tip / Family and Type | `change_element_type` | direct punctual; confirmare în masă |
| Mutare elemente (vector în mm) | `move_elements` | direct, dacă e punctual |
| Ștergere elemente | `delete_elements` | **întotdeauna cu confirmare** |
| Evidențiere color / izolare în view | `override_color_in_view`, `isolate_in_view` | direct (nu ating geometria) |
| Selectare + centrare view | `select_and_show` | direct (doar UI) |

Citirea (interogări, detalii, niveluri, tipuri, avertismente, capturi de view
pentru vision) nu necesită niciodată confirmare.

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
| `Revit/RevitRequestHandler.cs` | **Seam-ul de thread Revit** (`IExternalEventHandler`). Citește modelul/view-ul/selecția pe thread-ul Revit, lansează HTTP-ul (anulabil), detectează tema. Rulează și tool-urile agentului (coadă `ToolExecRequest` + `TaskCompletionSource`), iar în modul Edit pornește `AgentRunner`. |
| `Services/NormalignApi.cs` | Client HTTP către backend: `/api/chat` (JSON standard **sau** flux SSE la aprofundat), `/api/agent` (runde agentice), `/api/history`, `/api/messages`. Pune tokenul Bearer per cerere. |
| `Services/AgentRunner.cs` | **Bucla agentică (modul Edit)**: ține transcriptul Anthropic pe client (serverul e stateless per rundă), execută tool-urile Revit primite de la server, împachetează capturile de view ca blocuri de imagine (vision), emite chip-urile de activitate în UI. |
| `Services/AuthStore.cs` | Salvează/încarcă tokenul **criptat cu DPAPI** (`%APPDATA%\NormalignRevitAgent\auth.dat`). |
| `Services/LoginServer.cs` | Login loopback (stil `gh auth login`): pornește `HttpListener` pe `127.0.0.1`, deschide browserul, capturează tokenul. |
| `Services/Config.cs` | Doar `webUrl` (din `config.json` sau implicit prod). Fără secrete. |
| `Tools/IRevitTool.cs`, `ToolRegistry.cs` | Registrul de capabilități: nume + descriere + **JSON Schema** per tool, `Declare(includeWrite)` produce lista pentru `/api/agent`; tool-urile de scriere se declară doar în Edit. |
| `Tools/GetModelSummaryTool.cs` | Rezumatul modelului (niveluri cu cote, categorii, camere cu arii, tipuri de pereți) — folosit și ca `ifcContext.summary` la chat. |
| `Tools/ReadTools.cs` | Tool-uri de citire: `query_elements` (filtre combinabile), `get_element_details`, `get_selection`, `list_levels_and_grids`, `list_family_types`, `get_active_view`, `get_model_warnings`. |
| `Tools/WriteTools.cs` | Tool-uri de scriere (doar Edit, fiecare în `Transaction` proprie → Undo separat): `set_parameters`, `change_element_type` (schimbă Family and Type, inclusiv între familii din aceeași categorie — ex. consolidarea pereților importați din IFC), `move_elements`, `delete_elements`, `override_color_in_view`, `isolate_in_view`; plus `select_and_show` (nu modifică modelul). |
| `Tools/GetViewSnapshotTool.cs` | Exportă view-ul activ ca PNG → base64 → bloc de imagine pentru Claude (**vision** pe desen). |
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
| `src/app/api/chat/route.ts`, `history/route.ts`, `messages/route.ts` | Folosesc `getRequestUser()` în loc de doar Clerk. `chat` primește `client:"revit"` → prompturi Revit + intent (normative / revit_howto / model / mixed; howto/model = răspuns direct, fără RAG). |
| `src/app/api/agent/route.ts` | **Bucla agentului (Edit)**: o rundă stateless de tool-use — tool-urile Revit vin declarate de add-in; căutarea documentară (`search_normative`, `search_fise_tehnice`) o execută serverul în aceeași rundă. Persistă conversația la final. |
| `src/lib/retrieval.ts` | Căutarea hibridă (Qdrant + SPLADE + Cohere) extrasă din ruta de chat, partajată cu `/api/agent`. |
| `src/lib/prompts/revit.ts` | Prompturile specifice clientului Revit: bloc de context, reguli de răspuns direct, reguli de agent. |
| `src/proxy.ts` | `/api/chat`, `/api/agent`, `/api/history`, `/api/messages` trecute în lista publică (autorizarea se face în handler). |
| `nginx.conf` (root) | `client_max_body_size 20m` + timeout mare pe `/api/agent` (transcript cu capturi de view). |

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

### C. Distribuție — download de pe normalign.com

Fluxul recomandat (installer-ul e static, nu are nevoie de server propriu):

1. **Build**: `powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1`
   → `installer\Output\NormalignRevitAgent-Setup-<versiune>.exe`.
2. **Checksum** (pentru pagina de download):
   ```powershell
   Get-FileHash installer\Output\NormalignRevitAgent-Setup-1.0.0.exe -Algorithm SHA256
   ```
3. **Upload pe DigitalOcean Spaces** (același bucket folosit deja pentru PDF-uri),
   în folderul `downloads/`, cu acces public-read:
   ```bash
   aws s3 cp installer/Output/NormalignRevitAgent-Setup-1.0.0.exe \
     s3://digital-standart-lib/downloads/ \
     --endpoint-url https://fra1.digitaloceanspaces.com --acl public-read
   ```
   URL-ul de download devine:
   `https://digital-standart-lib.fra1.cdn.digitaloceanspaces.com/downloads/NormalignRevitAgent-Setup-1.0.0.exe`
4. **Buton pe site**: o pagină/secțiune „Descarcă add-in-ul Revit" pe normalign.com
   care trimite la URL-ul CDN + afișează SHA-256 și versiunea. Site-ul nu servește
   fișierul — doar linkul; nu crește imaginea Docker și versiunile noi nu cer
   redeploy (doar actualizarea linkului dacă se schimbă numele fișierului).
5. **Versiuni noi**: crești `AppVersion` în `installer\normalign-revit-agent.iss`,
   rebuild, upload lângă cele vechi (numele conține versiunea), actualizezi linkul.

**Utilizatorul final**: descarcă → dublu-click → instalare per-user fără admin
(WebView2 se instalează automat) → pornește Revit → **Always Load** → tab
Normalign → login în browser cu contul Normalign → gata.

**Securitate la distribuție:**
- Installer-ul **nu conține niciun secret** — nici chei API, nici tokenuri;
  autentificarea se face per-utilizator, în browser, iar tokenul rezultat se
  salvează criptat DPAPI pe stația lui.
- Download-ul e servit prin **HTTPS** (CDN); publică SHA-256 pe pagină ca
  utilizatorii să poată verifica integritatea.
- `.exe`-ul e **nesemnat** → Windows SmartScreen va arăta „Unknown publisher";
  utilizatorul trebuie să apese *More info → Run anyway*. Pentru distribuție
  serioasă, semnează codul (Azure Trusted Signing ~10 $/lună sau certificat
  OV/EV clasic) — elimină avertismentul și e singura îmbunătățire de securitate
  reală rămasă pe partea de client.
- Serverul nu are încredere în client: toate rutele API cer Bearer token valid
  (HMAC cu `DESKTOP_TOKEN_SECRET`), deci un `.exe` modificat de un atacator nu
  poate accesa decât contul celui care se loghează cu el.

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
