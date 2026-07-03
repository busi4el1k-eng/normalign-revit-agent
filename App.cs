using System;
using System.Reflection;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using NormalignRevitAgent.Revit;
using NormalignRevitAgent.Ui;

namespace NormalignRevitAgent
{
    /// <summary>
    /// Entry point of the add-in. Registered in the .addin manifest as an
    /// "Application", so OnStartup runs once when Revit launches.
    ///
    /// Responsibilities:
    ///   1. Build the shared services (WebView2 chat pane + Revit request handler).
    ///   2. Register the dockable chat pane and the ribbon button.
    ///   3. Keep the web app's model context in sync with the active document.
    /// </summary>
    public class App : IExternalApplication
    {
        // Stable id for the dockable pane (generated once, keep it constant).
        public static readonly DockablePaneId PaneId =
            new DockablePaneId(new Guid("8d07224f-5b91-40ff-931f-ababe3976d28"));

        // The single external event used to run Revit-API work on Revit's thread.
        // v1 syncs the model summary; v2 will reuse it to run agent tool calls.
        public static ExternalEvent? RevitEvent { get; private set; }
        public static RevitRequestHandler? Handler { get; private set; }
        public static ChatPane? Pane { get; private set; }

        public Result OnStartup(UIControlledApplication app)
        {
            // --- shared services ---
            Handler = new RevitRequestHandler();
            RevitEvent = ExternalEvent.Create(Handler);
            Pane = new ChatPane();

            // Web app mounted inside the pane -> push the current model context.
            Pane.WebAppReady += () =>
            {
                Handler.RequestSync();
                RevitEvent.Raise();
            };

            // User switched project/view in Revit -> re-sync (deduped by doc title).
            // ViewActivated runs in a valid API context, so we can read directly.
            app.ViewActivated += OnViewActivated;

            // --- dockable pane ---
            app.RegisterDockablePane(PaneId, "Normalign Agent", new ChatPaneProvider(Pane));

            // --- ribbon button ---
            const string tabName = "Normalign";
            try { app.CreateRibbonTab(tabName); } catch { /* tab may already exist */ }

            RibbonPanel panel = app.CreateRibbonPanel(tabName, "Agent AI");
            string asmPath = Assembly.GetExecutingAssembly().Location;

            var btn = new PushButtonData(
                "NormalignChatBtn",
                "Chat\nNormalign",
                asmPath,
                "NormalignRevitAgent.ShowChatCommand")
            {
                ToolTip = "Deschide asistentul AI Normalign pentru modelul curent."
            };
            panel.AddItem(btn);

            return Result.Succeeded;
        }

        private void OnViewActivated(object? sender, ViewActivatedEventArgs e)
        {
            try { Handler?.SyncFromDoc(e.Document); }
            catch { /* never let a sync error break view switching */ }
        }

        public Result OnShutdown(UIControlledApplication app)
        {
            app.ViewActivated -= OnViewActivated;
            return Result.Succeeded;
        }
    }
}
