using System;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using NormalignRevitAgent.Revit;
using NormalignRevitAgent.Services;
using NormalignRevitAgent.Ui;

namespace NormalignRevitAgent
{
    /// <summary>
    /// Punctul de intrare al add-in-ului (înregistrat ca "Application" în .addin,
    /// deci OnStartup rulează o dată, la pornirea Revit).
    ///
    ///   1. Construiește serviciile partajate (panoul de chat + handler-ul Revit).
    ///   2. Înregistrează panoul dockable și butonul din ribbon.
    ///   3. Leagă puntea: chat -> Revit (context/întrebări) și API-ul Normalign.
    /// </summary>
    public class App : IExternalApplication
    {
        // Id stabil pentru panoul dockable (generat o dată, rămâne constant).
        public static readonly DockablePaneId PaneId =
            new DockablePaneId(new Guid("8d07224f-5b91-40ff-931f-ababe3976d28"));

        public static ExternalEvent? RevitEvent { get; private set; }
        public static RevitRequestHandler? Handler { get; private set; }
        public static ChatPane? Pane { get; private set; }

        private static readonly NormalignApi Api = new();

        public Result OnStartup(UIControlledApplication app)
        {
            Handler = new RevitRequestHandler();
            RevitEvent = ExternalEvent.Create(Handler);
            Pane = new ChatPane();

            // Chat montat -> trimite chip-ul de context (doc · view · selecție).
            Pane.Ready += () => { Handler.RequestContextSync(); RevitEvent.Raise(); };

            // Întrebare -> pe thread-ul Revit pentru context, apoi HTTP (în handler).
            Pane.SendRequested += (message, chatId) =>
            {
                Handler.EnqueueSend(new ChatSendRequest { Message = message, ChatId = chatId });
                RevitEvent.Raise();
            };

            // Istoric + mesaje: doar HTTP, nu ating API-ul Revit — pot pleca direct.
            Pane.HistoryRequested += () => _ = Task.Run(async () =>
            {
                try
                {
                    JsonNode chats = await Api.GetHistoryAsync();
                    Pane!.PostJson(new JsonObject { ["type"] = "history", ["chats"] = chats });
                }
                catch (Exception ex)
                {
                    Pane!.PostJson(new JsonObject { ["type"] = "error", ["message"] = ex.Message });
                }
            });

            Pane.MessagesRequested += chatId => _ = Task.Run(async () =>
            {
                try
                {
                    JsonNode messages = await Api.GetMessagesAsync(chatId);
                    Pane!.PostJson(new JsonObject
                    {
                        ["type"] = "messages",
                        ["chatId"] = chatId,
                        ["messages"] = messages,
                    });
                }
                catch (Exception ex)
                {
                    Pane!.PostJson(new JsonObject { ["type"] = "error", ["message"] = ex.Message });
                }
            });

            // Schimbare de view/document -> chip-ul de context se actualizează.
            // ViewActivated rulează în context API valid, putem citi direct.
            app.ViewActivated += OnViewActivated;

            // --- panoul dockable ---
            app.RegisterDockablePane(PaneId, "Normalign", new ChatPaneProvider(Pane));

            // --- butonul din ribbon ---
            const string tabName = "Normalign";
            try { app.CreateRibbonTab(tabName); } catch { /* tab-ul poate exista deja */ }

            RibbonPanel panel = app.CreateRibbonPanel(tabName, "Agent AI");
            var btn = new PushButtonData(
                "NormalignChatBtn",
                "Chat\nNormalign",
                Assembly.GetExecutingAssembly().Location,
                "NormalignRevitAgent.ShowChatCommand")
            {
                ToolTip = "Deschide asistentul AI Normalign pentru modelul curent."
            };
            panel.AddItem(btn);

            return Result.Succeeded;
        }

        private void OnViewActivated(object? sender, ViewActivatedEventArgs e)
        {
            try
            {
                if (sender is UIApplication uiapp)
                    Handler?.PushContextChip(uiapp.ActiveUIDocument);
            }
            catch { /* sincronizarea nu are voie să strice schimbarea view-ului */ }
        }

        public Result OnShutdown(UIControlledApplication app)
        {
            app.ViewActivated -= OnViewActivated;
            return Result.Succeeded;
        }
    }
}
