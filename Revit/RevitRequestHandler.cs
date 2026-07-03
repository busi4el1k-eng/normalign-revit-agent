using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using NormalignRevitAgent.Services;
using NormalignRevitAgent.Tools;

namespace NormalignRevitAgent.Revit
{
    /// <summary>O întrebare din chat, în așteptarea contextului din Revit.</summary>
    public class ChatSendRequest
    {
        public string Message = "";
        public string? ChatId;
    }

    /// <summary>
    /// Puntea de thread-uri dintre chat și API-ul Revit.
    ///
    /// API-ul Revit e legal doar pe thread-ul Revit, într-un context ridicat de
    /// Revit. Mesajele din WebView2 sosesc pe thread-ul de UI, deci punem cererea
    /// în coadă și facem Raise() pe ExternalEvent; Revit apelează Execute() unde
    /// citim documentul (model, view activ, selecție). HTTP-ul pleacă apoi pe
    /// thread pool ca să nu blocheze Revit.
    ///
    /// v2 (agentic): tot aici se vor executa tool-urile cerute de LLM
    /// (query_elements, tag_element, ...) prin ToolRegistry — plumbing identic.
    /// </summary>
    public class RevitRequestHandler : IExternalEventHandler
    {
        private readonly ToolRegistry _tools = new();
        private readonly NormalignApi _api = new();
        private readonly ConcurrentQueue<ChatSendRequest> _sendQueue = new();
        private int _contextSyncPending;

        public void EnqueueSend(ChatSendRequest req) => _sendQueue.Enqueue(req);
        public void RequestContextSync() => Interlocked.Exchange(ref _contextSyncPending, 1);

        public string GetName() => "Normalign Agent";

        // Rulează pe thread-ul Revit (ExternalEvent).
        public void Execute(UIApplication app)
        {
            UIDocument? uidoc = app.ActiveUIDocument;

            if (Interlocked.Exchange(ref _contextSyncPending, 0) == 1)
                PushContextChip(uidoc);

            while (_sendQueue.TryDequeue(out ChatSendRequest? req))
            {
                // Context proaspăt, citit ACUM, pe thread-ul Revit.
                (string fileName, string context) = BuildContext(uidoc);
                PushContextChip(uidoc);

                ChatSendRequest local = req;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        JsonNode data = await _api.SendChatAsync(local.Message, local.ChatId, fileName, context);
                        App.Pane?.PostJson(new JsonObject
                        {
                            ["type"] = "reply",
                            ["content"] = data["content"]?.GetValue<string>() ?? "",
                            ["chatId"] = data["chatId"]?.DeepClone(),
                            ["followUps"] = data["followUpQuestions"]?.DeepClone(),
                            ["citations"] = data["metadata"]?["citations"]?.DeepClone(),
                        });
                    }
                    catch (Exception ex)
                    {
                        App.Pane?.PostJson(new JsonObject { ["type"] = "error", ["message"] = ex.Message });
                    }
                });
            }
        }

        /// <summary>
        /// Actualizează chip-ul de context din caseta de input (doc · view · selecție).
        /// Doar în context API valid (Execute / evenimente Revit).
        /// </summary>
        public void PushContextChip(UIDocument? uidoc)
        {
            Document? doc = uidoc?.Document;
            int selCount = 0;
            try { selCount = uidoc?.Selection.GetElementIds().Count ?? 0; } catch { }

            App.Pane?.PostJson(new JsonObject
            {
                ["type"] = "context",
                ["file"] = doc != null ? $"{doc.Title}.rvt" : null,
                ["view"] = doc?.ActiveView?.Name,
                ["selection"] = selCount,
            });
        }

        // ── Ce vede agentul: rezumatul modelului + view-ul activ + selecția ──────
        private (string fileName, string context) BuildContext(UIDocument? uidoc)
        {
            Document? doc = uidoc?.Document;
            if (doc == null)
                return ("Model Revit", "Niciun model deschis în Revit.");

            var sb = new StringBuilder();

            View? view = doc.ActiveView;
            if (view != null)
                sb.AppendLine($"View activ: {view.Name} ({view.ViewType})");

            AppendSelection(sb, uidoc!, doc);

            sb.AppendLine();
            sb.Append(_tools.GetModelSummary.Execute(doc, "{}"));

            return ($"{doc.Title}.rvt", sb.ToString());
        }

        private static void AppendSelection(StringBuilder sb, UIDocument uidoc, Document doc)
        {
            ICollection<ElementId> ids;
            try { ids = uidoc.Selection.GetElementIds(); } catch { return; }
            if (ids.Count == 0) return;

            sb.AppendLine($"Elemente selectate de utilizator ({ids.Count}):");
            foreach (ElementId id in ids.Take(30))
            {
                Element? e = doc.GetElement(id);
                if (e == null) continue;

                string cat = e.Category?.Name ?? "—";
                string typeName = (doc.GetElement(e.GetTypeId()) as ElementType)?.Name ?? "";
                string level = (e.LevelId != ElementId.InvalidElementId
                    ? (doc.GetElement(e.LevelId) as Level)?.Name : null) ?? "";

                var line = new StringBuilder($"  - {cat}: {e.Name}");
                if (!string.IsNullOrEmpty(typeName) && typeName != e.Name) line.Append($" (tip: {typeName})");
                if (!string.IsNullOrEmpty(level)) line.Append($", nivel {level}");
                line.Append($" [id {e.Id.Value}]");
                sb.AppendLine(line.ToString());
            }
            if (ids.Count > 30) sb.AppendLine($"  … și încă {ids.Count - 30} elemente.");
        }
    }
}
