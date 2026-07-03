using System;
using System.Reflection;
using Autodesk.Revit.UI;
using NormalignRevitAgent.Revit;
using NormalignRevitAgent.Services;
using NormalignRevitAgent.Ui;

namespace NormalignRevitAgent
{
    /// <summary>
    /// Entry point of the add-in. Registered in the .addin manifest as an
    /// "Application", so OnStartup runs once when Revit launches.
    ///
    /// Responsibilities:
    ///   1. Build the shared services (HTTP client + Revit request handler).
    ///   2. Register the dockable chat pane.
    ///   3. Add a ribbon button that shows the pane.
    /// </summary>
    public class App : IExternalApplication
    {
        // Stable id for the dockable pane (generated once, keep it constant).
        public static readonly DockablePaneId PaneId =
            new DockablePaneId(new Guid("8d07224f-5b91-40ff-931f-ababe3976d28"));

        // The single external event used to run Revit-API work on Revit's thread.
        // v1 uses it to read the model; v2 will reuse it to run agent tool calls.
        public static ExternalEvent? RevitEvent { get; private set; }
        public static RevitRequestHandler? Handler { get; private set; }
        public static ChatPane? Pane { get; private set; }

        public Result OnStartup(UIControlledApplication app)
        {
            // --- shared services ---
            var client = new NormalignClient();
            Handler = new RevitRequestHandler(client);
            RevitEvent = ExternalEvent.Create(Handler);
            Pane = new ChatPane(RevitEvent, Handler);

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

        public Result OnShutdown(UIControlledApplication app) => Result.Succeeded;
    }
}
