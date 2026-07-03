using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Autodesk.Revit.UI;
using NormalignRevitAgent.Services;
using NormalignRevitAgent.Tools;

namespace NormalignRevitAgent.Revit
{
    /// <summary>One pending chat turn queued from the UI thread.</summary>
    public class ChatRequest
    {
        public string Question = "";
        public string? ChatId;
        public Action<ChatResult> OnReply = _ => { };
        public Action<string> OnError = _ => { };
    }

    /// <summary>
    /// THE key piece of the architecture.
    ///
    /// The Revit API can only be touched from Revit's own thread, inside an
    /// event Revit raised. A button click in our WPF pane is NOT such a context.
    /// So the pane enqueues a request and raises an ExternalEvent; Revit then
    /// calls Execute() below on its thread, where reading the document is legal.
    ///
    /// v1: Execute reads the model, then fires the HTTP call to Normalign.
    /// v2 (agentic): Execute will instead run whatever tool the LLM asked for
    ///     (query walls, tag elements, ...) via <see cref="ToolRegistry"/> and
    ///     return the result to the server loop. The threading seam stays the same.
    /// </summary>
    public class RevitRequestHandler : IExternalEventHandler
    {
        private readonly ConcurrentQueue<ChatRequest> _queue = new();
        private readonly NormalignClient _client;
        private readonly ToolRegistry _tools = new();

        public RevitRequestHandler(NormalignClient client) => _client = client;

        /// <summary>Called from the UI thread. Pair with App.RevitEvent.Raise().</summary>
        public void Enqueue(ChatRequest request) => _queue.Enqueue(request);

        public string GetName() => "Normalign Agent";

        // Runs on Revit's thread — safe to read the document here.
        public void Execute(UIApplication app)
        {
            while (_queue.TryDequeue(out ChatRequest? req))
            {
                var doc = app.ActiveUIDocument?.Document;

                // v1: the only "tool" we run is a read-only model summary.
                string summary = _tools.GetModelSummary.Execute(doc, "{}");

                // The HTTP call must NOT block Revit's thread, so hand it to the
                // thread pool. The UI callbacks marshal themselves back with Dispatcher.
                ChatRequest local = req;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        ChatResult result = await _client.AskAsync(local.Question, summary, local.ChatId);
                        local.OnReply(result);
                    }
                    catch (Exception ex)
                    {
                        local.OnError(ex.Message);
                    }
                });
            }
        }
    }
}
