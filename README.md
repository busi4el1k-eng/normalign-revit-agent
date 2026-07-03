# Normalign Revit Agent

A Revit 2027 add-in: a dockable AI chat panel that reads the open model and asks
the Normalign RAG backend (`/api/chat`) about it. Structured so it can grow into a
full agentic assistant (tool-use over the live model) without a rewrite.

## Requirements (already installed on this machine)
- Revit 2027 (uses .NET 10)
- .NET 10 SDK
- Visual Studio 2022/2026

## Build
```
dotnet build NormalignRevitAgent.csproj -c Debug
```
Output: `bin\NormalignRevitAgent.dll` (+ `bin\NormalignRevitAgent.addin`).

## Install into Revit
Copy the manifest so Revit loads the add-in on startup:
```
copy NormalignRevitAgent.addin  "C:\ProgramData\Autodesk\Revit\Addins\2027\"
```
(The manifest's `<Assembly>` already points at `bin\NormalignRevitAgent.dll`.)
Then launch Revit → ribbon tab **Normalign** → **Chat Normalign** to open the panel.

## Configure the backend (Services/Config.cs)
`/api/chat` is Clerk-protected, so pick one:
- **Local dev:** run the web app and set `ChatUrl = "http://localhost:3000/api/chat"`.
- **Prod:** add a machine-to-machine API-key path to the chat route, put the key
  in `Config.ApiKey` (sent as `Authorization: Bearer …`).

## Architecture (how v2 drops in)
- `App.cs` — startup: registers the dockable pane + ribbon button.
- `Ui/ChatPane.cs` — the WPF chat UI.
- `Revit/RevitRequestHandler.cs` — **the threading seam.** Runs Revit-API work on
  Revit's thread via `ExternalEvent`. v1 reads the model; v2 will run agent tools here.
- `Tools/` — `IRevitTool` + `ToolRegistry`. v1 has `get_model_summary`. Add
  `query_elements`, `tag_element`, … as new `IRevitTool`s and have a server-side
  agent loop pick them by name. Nothing else changes.
- `Services/NormalignClient.cs` — HTTP to `/api/chat`, sends the model summary as `ifcContext`.
