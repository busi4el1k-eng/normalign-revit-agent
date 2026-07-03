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
        public bool DeepThink;
        /// <summary>"planning" = chatul RAG obișnuit; "edit" = bucla agentică.</summary>
        public string Mode = "planning";
    }

    /// <summary>Un tool cerut de agent, de executat pe thread-ul Revit.</summary>
    public class ToolExecRequest
    {
        public string Name = "";
        public string ArgsJson = "{}";
        public readonly TaskCompletionSource<string> Tcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>
    /// Puntea de thread-uri dintre chat și API-ul Revit. API-ul Revit e legal doar
    /// pe thread-ul Revit; punem cererea în coadă, Raise() pe ExternalEvent, iar
    /// Revit apelează Execute() unde citim documentul (model, view activ, selecție)
    /// sau rulăm tool-urile agentului. HTTP-ul pleacă apoi pe thread pool, anulabil.
    /// </summary>
    public class RevitRequestHandler : IExternalEventHandler
    {
        private readonly ToolRegistry _tools = new();
        private readonly NormalignApi _api = new();
        private readonly ConcurrentQueue<ChatSendRequest> _sendQueue = new();
        private readonly ConcurrentQueue<ToolExecRequest> _toolQueue = new();
        private int _contextSyncPending;
        private CancellationTokenSource? _currentCts;

        public void EnqueueSend(ChatSendRequest req) => _sendQueue.Enqueue(req);
        public void RequestContextSync() => Interlocked.Exchange(ref _contextSyncPending, 1);

        /// <summary>Anulează răspunsul în curs (butonul Stop din chat).</summary>
        public void CancelCurrent()
        {
            try { _currentCts?.Cancel(); } catch { }
        }

        public string GetName() => "Normalign Agent";

        public void Execute(UIApplication app)
        {
            UIDocument? uidoc = app.ActiveUIDocument;

            if (Interlocked.Exchange(ref _contextSyncPending, 0) == 1)
                PushContextChip(uidoc);

            // Tool-urile agentului au prioritate: bucla lui așteaptă rezultatul.
            while (_toolQueue.TryDequeue(out ToolExecRequest? tr))
            {
                string result;
                try
                {
                    result = _tools.TryGet(tr.Name, out IRevitTool tool)
                        ? tool.Execute(app, tr.ArgsJson)
                        : $"Eroare: tool necunoscut '{tr.Name}'.";
                }
                catch (Exception ex) { result = $"Eroare: {ex.Message}"; }
                tr.Tcs.TrySetResult(result);
            }

            while (_sendQueue.TryDequeue(out ChatSendRequest? req))
            {
                (string fileName, string context) = BuildContext(uidoc);
                PushContextChip(uidoc);

                var cts = new CancellationTokenSource();
                _currentCts = cts;
                ChatSendRequest local = req;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (local.Mode == "edit")
                        {
                            // Bucla agentică: serverul decide tool-urile, noi le executăm
                            // pe thread-ul Revit prin coada de mai sus.
                            var runner = new AgentRunner(_api, ExecuteToolAsync);
                            await runner.RunAsync(local.Message, local.ChatId, fileName, context,
                                _tools.Declare(includeWrite: true),
                                msg => App.Pane?.PostJson(msg), cts.Token);
                        }
                        else
                        {
                            await _api.SendChatAsync(local.Message, local.ChatId, fileName, context,
                                local.DeepThink, msg => App.Pane?.PostJson(msg), cts.Token);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        App.Pane?.PostJson(new JsonObject { ["type"] = "stopped" });
                    }
                    catch (Exception ex)
                    {
                        App.Pane?.PostJson(new JsonObject { ["type"] = "error", ["message"] = ex.Message });
                    }
                    finally
                    {
                        if (ReferenceEquals(_currentCts, cts)) _currentCts = null;
                        cts.Dispose();
                    }
                });
            }
        }

        /// <summary>
        /// Programează un tool pe thread-ul Revit și așteaptă rezultatul.
        /// Apelabil de pe orice thread (folosit de AgentRunner).
        /// </summary>
        private Task<string> ExecuteToolAsync(string name, string argsJson)
        {
            var tr = new ToolExecRequest { Name = name, ArgsJson = argsJson };
            _toolQueue.Enqueue(tr);
            App.RevitEvent?.Raise();
            return tr.Tcs.Task;
        }

        /// <summary>Chip-ul de context din input (doc · view · selecție) + tema Revit.</summary>
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
                ["theme"] = RevitThemeName(),
            });
        }

        /// <summary>Tema curentă a interfeței Revit ("dark"/"light").</summary>
        public static string RevitThemeName()
        {
            try { return UIThemeManager.CurrentTheme == UITheme.Light ? "light" : "dark"; }
            catch { return "dark"; }
        }

        // ── Context vizibil agentului: view activ + selecție + rezumat model ────
        private (string fileName, string context) BuildContext(UIDocument? uidoc)
        {
            Document? doc = uidoc?.Document;
            if (doc == null) return ("Model Revit", "Niciun model deschis în Revit.");

            var sb = new StringBuilder();
            View? view = doc.ActiveView;
            if (view != null) sb.AppendLine($"View activ: {view.Name} ({view.ViewType})");

            AppendSelection(sb, uidoc!, doc);
            sb.AppendLine();
            sb.Append(GetModelSummaryTool.Summarize(doc));
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
