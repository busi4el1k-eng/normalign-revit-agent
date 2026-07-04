using System;
using System.IO;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Windows.Media.Imaging;
using System.Threading;
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
            // mode = "planning" (chat RAG) sau "edit" (bucla agentică cu tool-uri).
            Pane.SendRequested += (message, chatId, deepThink, mode) =>
            {
                Handler.EnqueueSend(new ChatSendRequest { Message = message, ChatId = chatId, DeepThink = deepThink, Mode = mode });
                RevitEvent.Raise();
            };

            // Stop -> anulează răspunsul în curs.
            Pane.StopRequested += () => Handler.CancelCurrent();

            // Login (browser loopback) -> la succes, comută pe chat.
            Pane.LoginRequested += () => _ = Task.Run(async () =>
            {
                try
                {
                    string? token = await LoginServer.RunAsync(CancellationToken.None);
                    if (!string.IsNullOrEmpty(token)) Pane!.ShowChat();
                    else Pane!.LoginFailed("Autentificarea nu s-a finalizat. Reîncearcă.");
                }
                catch (Exception ex) { Pane!.LoginFailed(ex.Message); }
            });

            // Logout -> șterge token-ul și revino la ecranul de login.
            Pane.LogoutRequested += () => { AuthStore.Clear(); Pane!.ShowLogin(); };

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

            // --- butonul din ribbon (unul singur: deschide panoul de chat/agent) ---
            const string tabName = "Normalign";
            try { app.CreateRibbonTab(tabName); } catch { /* tab-ul poate exista deja */ }

            RibbonPanel panel = app.CreateRibbonPanel(tabName, "Asistent");
            var btn = new PushButtonData(
                "NormalignChatBtn",
                "Agent AI",
                Assembly.GetExecutingAssembly().Location,
                "NormalignRevitAgent.ShowChatCommand")
            {
                ToolTip = "Asistentul AI Normalign: chat cu citări din normative românești + agent care inspectează și editează modelul deschis.",
                LongDescription = "Modul Plan — întrebări despre normative (răspuns cu citări din legislație), " +
                    "despre Revit sau despre modelul deschis; vede live view-ul activ și selecția.\n" +
                    "Modul Edit — agentul modifică modelul la cerere (parametri, tipuri, mutare, ștergere), " +
                    "fiecare operație cu Undo separat; pentru ștergeri sau modificări în masă cere " +
                    "confirmare cu butoane Da/Nu.",
            };

            // Iconițele stau în Assets\ lângă DLL (copiate la build/instalare).
            string assetsDir = Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "Assets");
            try
            {
                btn.LargeImage = new BitmapImage(new Uri(Path.Combine(assetsDir, "icon32.png")));
                btn.Image = new BitmapImage(new Uri(Path.Combine(assetsDir, "icon16.png")));
            }
            catch { /* fără iconiță dacă lipsesc fișierele — butonul rămâne funcțional */ }

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
